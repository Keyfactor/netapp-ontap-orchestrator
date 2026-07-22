
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Keyfactor.Extensions.Orchestrators.NetAppOntap.Models;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Moq;
using Xunit;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    public class InventoryTests
    {
        private static OntapCertificate Cert(string name, string type, string svm = "vs0") =>
            new OntapCertificate
            {
                Uuid = Guid.NewGuid().ToString(),
                Name = name,
                Type = type,
                PublicCertificate = TestCertFactory.CreateSelfSignedCertPem(name),
                Svm = svm == null ? null : new OntapSvmRef { Name = svm }
            };

        private static (TestableInventory job, Mock<OntapClient> client, List<OntapCertificate> returned)
            BuildJob(string storeType, List<OntapCertificate> toReturn, string scope = "vs0")
        {
            var resolver = new Mock<IPAMSecretResolver>();
            var client = new Mock<OntapClient>();
            client.Setup(c => c.GetCertificates(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                  .ReturnsAsync(toReturn);

            var job = new TestableInventory(resolver.Object)
            {
                PublicJobParameters = new OntapJobParameters
                {
                    StoreType = storeType,
                    JobHistoryId = 1,
                    StoreProperties = new OntapStoreProperties { Scope = scope }
                }
            };
            job._client = client.Object;
            return (job, client, toReturn);
        }

        private static List<CurrentInventoryItem> RunAndCapture(TestableInventory job)
        {
            List<CurrentInventoryItem> captured = new();
            SubmitInventoryUpdate cb = items => { captured = items.ToList(); return true; };
            var result = job.ProcessJob(new InventoryJobConfiguration { JobId = Guid.NewGuid() }, cb);
            result.Result.Should().Be(Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Success);
            return captured;
        }

        [Fact]
        public void CertsStore_RequestsServerAndClientTypes()
        {
            IReadOnlyList<string>? passedTypes = null;
            var resolver = new Mock<IPAMSecretResolver>();
            var client = new Mock<OntapClient>();
            client.Setup(c => c.GetCertificates(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                  .Callback<string, IReadOnlyList<string>>((_, types) => passedTypes = types)
                  .ReturnsAsync(new List<OntapCertificate>());

            var job = new TestableInventory(resolver.Object)
            {
                PublicJobParameters = new OntapJobParameters
                {
                    StoreType = Constants.STORE_TYPE_CERTS,
                    StoreProperties = new OntapStoreProperties { Scope = "vs0" }
                }
            };
            job._client = client.Object;

            RunAndCapture(job);

            passedTypes.Should().BeEquivalentTo(new[] { OntapCertType.SERVER, OntapCertType.CLIENT });
        }

        [Fact]
        public void ClusterScope_PassesNullSvmToClient()
        {
            string? passedSvm = "sentinel";
            var resolver = new Mock<IPAMSecretResolver>();
            var client = new Mock<OntapClient>();
            client.Setup(c => c.GetCertificates(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                  .Callback<string, IReadOnlyList<string>>((svm, _) => passedSvm = svm)
                  .ReturnsAsync(new List<OntapCertificate>());

            var job = new TestableInventory(resolver.Object)
            {
                PublicJobParameters = new OntapJobParameters
                {
                    StoreType = Constants.STORE_TYPE_CERTS,
                    StoreProperties = new OntapStoreProperties { Scope = Constants.SCOPE_CLUSTER }
                }
            };
            job._client = client.Object;

            RunAndCapture(job);

            passedSvm.Should().BeNull("cluster scope must omit the svm on the API call");
        }

        [Fact]
        public void ServerAndClientCerts_AreFlaggedAsHavingPrivateKey()
        {
            var (job, _, _) = BuildJob(Constants.STORE_TYPE_CERTS, new List<OntapCertificate>
            {
                Cert("srv", OntapCertType.SERVER),
                Cert("cli", OntapCertType.CLIENT)
            });

            var items = RunAndCapture(job);

            items.Should().HaveCount(2);
            items.Should().OnlyContain(i => i.PrivateKeyEntry);
        }

        [Fact]
        public void TrustAnchors_AreNotFlaggedAsHavingPrivateKey()
        {
            var (job, _, _) = BuildJob(Constants.STORE_TYPE_TRUSTED, new List<OntapCertificate>
            {
                Cert("root1", OntapCertType.SERVER_CA),
                Cert("client-trust", OntapCertType.CLIENT_CA)
            });

            var items = RunAndCapture(job);

            items.Should().HaveCount(2);
            items.Should().OnlyContain(i => !i.PrivateKeyEntry);
        }

        [Fact]
        public void InventoryItem_CarriesCertTypeParameterAndAliasFromName()
        {
            var (job, _, _) = BuildJob(Constants.STORE_TYPE_TRUSTED, new List<OntapCertificate>
            {
                Cert("my-root", OntapCertType.SERVER_CA)
            });

            var item = RunAndCapture(job).Single();

            item.Alias.Should().Be("my-root");
            item.Parameters.Should().ContainKey(EntryParameterKeys.CERT_TYPE);
            item.Parameters[EntryParameterKeys.CERT_TYPE].Should().Be(OntapCertType.SERVER_CA);
            item.Certificates.Should().ContainSingle();
        }
    }
}
