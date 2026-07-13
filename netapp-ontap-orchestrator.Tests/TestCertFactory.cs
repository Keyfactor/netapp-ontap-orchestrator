// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    /// <summary>
    /// Builds real, self-contained certificate material in-memory for tests using the BCL
    /// (System.Security.Cryptography) — no external fixture files and no BouncyCastle dependency,
    /// which avoids the duplicate Org.BouncyCastle assembly that Keyfactor.PKI already brings in.
    /// Returns PEM so it round-trips through X509Certificate2.CreateFromPem exactly like the real
    /// inventory path parses ONTAP's public_certificate field.
    /// </summary>
    internal static class TestCertFactory
    {
        public static string CreateSelfSignedCertPem(string commonName = "test.example.com")
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return cert.ExportCertificatePem();
        }

        /// <summary>Base64-encoded PFX containing a self-signed RSA cert AND its private key (for ONTAP_CERTS).</summary>
        public static string CreateSelfSignedPfxBase64(string commonName = "test.example.com", string password = TestPassword)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return Convert.ToBase64String(cert.Export(X509ContentType.Pfx, password));
        }

        /// <summary>Base64-encoded DER of the public certificate only, no key (for ONTAP_TRUSTED).</summary>
        public static string CreateSelfSignedCertDerBase64(string commonName = "trust.example.com")
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return Convert.ToBase64String(cert.Export(X509ContentType.Cert));
        }

        /// <summary>
        /// Base64-encoded PFX containing a leaf (with private key) signed by a root CA, plus the root
        /// certificate (public only) — a two-element chain, for exercising intermediate handling.
        /// </summary>
        public static string CreateChainPfxBase64(
            string leafCn = "leaf.example.com", string rootCn = "test-root-ca", string password = TestPassword)
        {
            using var rootRsa = RSA.Create(2048);
            var rootReq = new CertificateRequest($"CN={rootCn}", rootRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootReq.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using var rootCert = rootReq.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

            using var leafRsa = RSA.Create(2048);
            var leafReq = new CertificateRequest($"CN={leafCn}", leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

            var serial = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            using var leafNoKey = leafReq.Create(
                rootCert, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), serial);
            using var leafWithKey = leafNoKey.CopyWithPrivateKey(leafRsa);
            using var rootPublicOnly = new X509Certificate2(rootCert.Export(X509ContentType.Cert));

            var collection = new X509Certificate2Collection { leafWithKey, rootPublicOnly };
            return Convert.ToBase64String(collection.Export(X509ContentType.Pfx, password));
        }

        public const string TestPassword = "pfx-pass";
    }
}
