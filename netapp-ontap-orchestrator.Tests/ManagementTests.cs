
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Keyfactor.Extensions.Orchestrators.NetAppOntap.Models;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Moq;
using Xunit;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    public class ManagementTests
    {
        private static Mock<OntapClient> NewClientMock()
        {
            var m = new Mock<OntapClient>();
            m.Setup(c => c.FindByName(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((OntapCertificate?)null);
            m.Setup(c => c.DeleteCertificate(It.IsAny<string>())).Returns(Task.CompletedTask);
            m.Setup(c => c.InstallCertificate(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
            return m;
        }

        private static TestableManagement BuildJob(
            Mock<OntapClient> client, string storeType, string certType,
            string alias = "my-cert", string scope = "vs0", bool overwrite = false, string contents = null)
        {
            var resolver = new Mock<IPAMSecretResolver>();
            var job = new TestableManagement(resolver.Object)
            {
                PublicJobParameters = new OntapJobParameters
                {
                    StoreType = storeType,
                    JobHistoryId = 1,
                    StoreProperties = new OntapStoreProperties { Scope = scope, ClusterManagementHost = "10.0.0.1" },
                    CertProperties = new OntapCertProperties
                    {
                        CertType = certType,
                        Alias = alias,
                        Overwrite = overwrite,
                        // Trust-store tests use a public-only DER; CERTS install tests pass a PFX explicitly.
                        Contents = contents ?? TestCertFactory.CreateSelfSignedCertDerBase64()
                    }
                }
            };
            job._client = client.Object;
            return job;
        }

        private static ManagementJobConfiguration Cfg(CertStoreOperationType op) =>
            new ManagementJobConfiguration { OperationType = op, JobId = Guid.NewGuid() };

        // ── Guard: root_ca is never installed ──────────────────────────────

        [Fact]
        public void Add_RootCaCertType_FailsAndDoesNotInstall()
        {
            var client = NewClientMock();
            var job = BuildJob(client, Constants.STORE_TYPE_CERTS, OntapCertType.ROOT_CA);

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Add));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("root_ca");
            client.Verify(c => c.InstallCertificate(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ── Guard: CertType must be valid for the store type ───────────────

        [Fact]
        public void Add_CertTypeInvalidForStoreType_Fails()
        {
            var client = NewClientMock();
            // "server" is a CERTS type; invalid for a TRUSTED store.
            var job = BuildJob(client, Constants.STORE_TYPE_TRUSTED, OntapCertType.SERVER);

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Add));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            client.Verify(c => c.InstallCertificate(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ── Add of a new trust anchor: single cert, no intermediates, no key ──

        [Fact]
        public void Add_NewTrustAnchor_InstallsSingleCertWithoutKeyOrIntermediates()
        {
            var client = NewClientMock(); // FindByName returns null
            var job = BuildJob(client, Constants.STORE_TYPE_TRUSTED, OntapCertType.SERVER_CA);

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Add));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            client.Verify(c => c.DeleteCertificate(It.IsAny<string>()), Times.Never);
            // publicCert present; intermediates null/empty; private key null for a trust anchor.
            client.Verify(c => c.InstallCertificate(
                "my-cert", OntapCertType.SERVER_CA,
                It.Is<string>(s => !string.IsNullOrEmpty(s)),
                It.Is<IEnumerable<string>>(i => i == null || !i.Any()),
                null,
                "vs0"), Times.Once);
        }

        // ── Add of a CERTS entry: leaf + private key flow through ──────────

        [Fact]
        public void Add_NewServerCert_InstallsWithPublicCertAndPrivateKey()
        {
            var client = NewClientMock();
            var pfx = TestCertFactory.CreateSelfSignedPfxBase64("server.example.com");
            var job = BuildJob(client, Constants.STORE_TYPE_CERTS, OntapCertType.SERVER, contents: pfx);
            job.PublicJobParameters.CertProperties.PrivateKeyPassword = TestCertFactory.TestPassword;

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Add));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            client.Verify(c => c.InstallCertificate(
                "my-cert", OntapCertType.SERVER,
                It.Is<string>(s => s != null && s.Contains("BEGIN CERTIFICATE")),
                It.IsAny<IEnumerable<string>>(),
                It.Is<string>(k => k != null && k.Contains("BEGIN PRIVATE KEY")),
                "vs0"), Times.Once);
        }

        // ── Existing cert + no overwrite: fail, touch nothing ──────────────

        [Fact]
        public void Add_ExistingCert_NoOverwrite_FailsAndDoesNotModify()
        {
            var client = NewClientMock();
            client.Setup(c => c.FindByName("my-cert", "vs0"))
                .ReturnsAsync(new OntapCertificate { Uuid = "uuid-1", Name = "my-cert", Type = OntapCertType.SERVER_CA });

            var job = BuildJob(client, Constants.STORE_TYPE_TRUSTED, OntapCertType.SERVER_CA, overwrite: false);

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Add));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            client.Verify(c => c.DeleteCertificate(It.IsAny<string>()), Times.Never);
            client.Verify(c => c.InstallCertificate(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        // ── Existing cert + overwrite: delete then install ─────────────────

        [Fact]
        public void Add_ExistingCert_Overwrite_DeletesThenInstalls()
        {
            var client = NewClientMock();
            client.Setup(c => c.FindByName("my-cert", "vs0"))
                .ReturnsAsync(new OntapCertificate { Uuid = "uuid-1", Name = "my-cert", Type = OntapCertType.SERVER_CA });

            var job = BuildJob(client, Constants.STORE_TYPE_TRUSTED, OntapCertType.SERVER_CA, overwrite: true);

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Add));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            client.Verify(c => c.DeleteCertificate("uuid-1"), Times.Once);
            client.Verify(c => c.InstallCertificate(
                "my-cert", OntapCertType.SERVER_CA, It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), "vs0"), Times.Once);
        }

        // ── Remove ─────────────────────────────────────────────────────────

        [Fact]
        public void Remove_Found_DeletesByUuid()
        {
            var client = NewClientMock();
            client.Setup(c => c.FindByName("my-cert", "vs0"))
                .ReturnsAsync(new OntapCertificate { Uuid = "uuid-9", Name = "my-cert", Type = OntapCertType.SERVER_CA });

            var job = BuildJob(client, Constants.STORE_TYPE_TRUSTED, OntapCertType.SERVER_CA);

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Remove));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            client.Verify(c => c.DeleteCertificate("uuid-9"), Times.Once);
        }

        [Fact]
        public void Remove_NotFound_SucceedsAsNoOp()
        {
            var client = NewClientMock(); // FindByName returns null
            var job = BuildJob(client, Constants.STORE_TYPE_TRUSTED, OntapCertType.SERVER_CA);

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Remove));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Success);
            client.Verify(c => c.DeleteCertificate(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void Remove_PreinstalledRoot_ReturnsFriendlyFailure()
        {
            // The exact 400 body observed from the simulator when deleting a factory root.
            const string body =
                "{ \"error\": { \"message\": \"Cannot delete preinstalled \\\"server-ca\\\" certificates. " +
                "Use the CLI to complete the operation.\", \"code\": \"3735681\", \"target\": \"uuid\" } }";

            var client = NewClientMock();
            client.Setup(c => c.FindByName(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new OntapCertificate { Uuid = "uuid-factory", Name = "my-cert", Type = OntapCertType.SERVER_CA });
            client.Setup(c => c.DeleteCertificate("uuid-factory"))
                .ThrowsAsync(OntapApiException.FromResponse(400, body));

            var job = BuildJob(client, Constants.STORE_TYPE_TRUSTED, OntapCertType.SERVER_CA);

            var result = job.ProcessJob(Cfg(CertStoreOperationType.Remove));

            result.Result.Should().Be(OrchestratorJobStatusJobResult.Failure);
            result.FailureMessage.Should().Contain("preinstalled");
            result.FailureMessage.Should().Contain("CLI");
        }
    }
}
