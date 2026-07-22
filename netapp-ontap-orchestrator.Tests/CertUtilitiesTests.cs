
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Xunit;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    public class CertUtilitiesTests
    {
        private static int CountCertBlocks(string pem) =>
            pem == null ? 0 : pem.Split(new[] { "-----BEGIN CERTIFICATE-----" }, StringSplitOptions.None).Length - 1;

        // ── ONTAP_CERTS: PFX -> leaf + intermediates + key ─────────────────

        [Fact]
        public void ConvertPfxForCertsStore_SelfSigned_ReturnsLeafAndKey_NoIntermediates()
        {
            var pfx = TestCertFactory.CreateSelfSignedPfxBase64("cert-and-key.example.com");

            var (leafPem, intermediatePems, keyPem) =
                CertUtilities.ConvertPfxForCertsStore(pfx, TestCertFactory.TestPassword);

            CountCertBlocks(leafPem).Should().Be(1, "public_certificate carries exactly the leaf");
            intermediatePems.Should().BeEmpty("a self-signed cert has no issuer chain");
            keyPem.Should().Contain("-----BEGIN PRIVATE KEY-----");
        }

        [Fact]
        public void ConvertPfxForCertsStore_Chain_SplitsLeafFromIntermediates()
        {
            var pfx = TestCertFactory.CreateChainPfxBase64("leaf.example.com", "test-root-ca");

            var (leafPem, intermediatePems, keyPem) =
                CertUtilities.ConvertPfxForCertsStore(pfx, TestCertFactory.TestPassword);

            // Leaf is the single end-entity; the root lands in the intermediates array.
            CountCertBlocks(leafPem).Should().Be(1);
            using var leaf = X509Certificate2.CreateFromPem(leafPem);
            leaf.Subject.Should().Contain("leaf.example.com");

            intermediatePems.Should().HaveCount(1);
            intermediatePems.Should().OnlyContain(p => CountCertBlocks(p) == 1);
            using var issuer = X509Certificate2.CreateFromPem(intermediatePems[0]);
            issuer.Subject.Should().Contain("test-root-ca");

            keyPem.Should().Contain("-----BEGIN PRIVATE KEY-----");
        }

        [Fact]
        public void ConvertPfxForCertsStore_KeyMatchesLeaf()
        {
            var pfx = TestCertFactory.CreateSelfSignedPfxBase64("match.example.com");

            var (leafPem, _, keyPem) = CertUtilities.ConvertPfxForCertsStore(pfx, TestCertFactory.TestPassword);

            // CreateFromPem pairs leaf + key and throws on mismatch — a strong correctness check.
            using var reassembled = X509Certificate2.CreateFromPem(leafPem, keyPem);
            reassembled.Subject.Should().Contain("match.example.com");
            reassembled.HasPrivateKey.Should().BeTrue();
        }

        [Fact]
        public void ConvertPfxForCertsStore_NoPrivateKey_Throws()
        {
            var der = TestCertFactory.CreateSelfSignedCertDerBase64();

            Action act = () => CertUtilities.ConvertPfxForCertsStore(der, null);

            act.Should().Throw<InvalidOperationException>();
        }

        // ── ONTAP_TRUSTED: contents -> single public cert, never a chain ───

        [Fact]
        public void ConvertToSingleCertPem_FromDer_ReturnsOneCertWithoutKey()
        {
            var der = TestCertFactory.CreateSelfSignedCertDerBase64("trust-der.example.com");

            var pem = CertUtilities.ConvertToSingleCertPem(der, null);

            CountCertBlocks(pem).Should().Be(1);
            pem.Should().NotContain("PRIVATE KEY");
            using var parsed = X509Certificate2.CreateFromPem(pem);
            parsed.Subject.Should().Contain("trust-der.example.com");
        }

        [Fact]
        public void ConvertToSingleCertPem_FromChainContents_ReturnsExactlyOneCert()
        {
            // Even if the contents carry a chain (and a key), the TRUSTED path must emit a single cert
            // and no key — ONTAP rejects intermediates and keys for the *_ca types.
            var chainPfx = TestCertFactory.CreateChainPfxBase64();

            var pem = CertUtilities.ConvertToSingleCertPem(chainPfx, TestCertFactory.TestPassword);

            CountCertBlocks(pem).Should().Be(1, "trust anchors take exactly one certificate");
            pem.Should().NotContain("PRIVATE KEY");
        }

        // ── Input validation ────────────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Convert_NullOrEmptyContents_ThrowsArgumentException(string? contents)
        {
            Action certs = () => CertUtilities.ConvertPfxForCertsStore(contents!, "pw");
            Action trusted = () => CertUtilities.ConvertToSingleCertPem(contents!, "pw");

            certs.Should().Throw<ArgumentException>();
            trusted.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Convert_InvalidBase64_ThrowsArgumentException()
        {
            Action act = () => CertUtilities.ConvertToSingleCertPem("not!valid!base64!", null);
            act.Should().Throw<ArgumentException>();
        }
    }
}
