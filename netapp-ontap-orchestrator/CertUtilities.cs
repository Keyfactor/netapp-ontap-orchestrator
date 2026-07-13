
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Pkcs;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap
{
    /// <summary>
    /// Converts the certificate material Keyfactor Command delivers on a Management/Add job into the
    /// PEM values the ONTAP REST API expects on POST /api/security/certificates.
    ///
    /// ONTAP splits the chain across distinct fields, and the split differs by certificate type:
    ///   server / client  : public_certificate = leaf, intermediate_certificates = [intermediates], private_key = key
    ///   server_ca/client_ca : public_certificate = a single certificate; intermediate_certificates is NOT accepted
    ///                         (ONTAP error 3735696) and no private key is permitted (error 3735618).
    ///
    /// Certificates are read via .NET (public data only); the private key is exported via BouncyCastle to
    /// avoid the platform-specific .NET/CNG private-key export failure on Windows.
    /// </summary>
    public static class CertUtilities
    {
        /// <summary>
        /// ONTAP_CERTS: base64 PFX -> (leaf PEM, intermediate PEMs, private key PEM [PKCS#8]).
        /// The leaf goes in public_certificate; the intermediates go in the intermediate_certificates array.
        /// Throws InvalidOperationException if the PFX carries no private key.
        /// </summary>
        public static (string leafPem, List<string> intermediatePems, string privateKeyPem)
            ConvertPfxForCertsStore(string base64Pfx, string password)
        {
            var pfxBytes = DecodeBase64(base64Pfx, nameof(base64Pfx));
            var orderedPems = LoadOrderedCertPems(pfxBytes, password, requirePrivateKey: true);

            var leafPem = orderedPems[0];
            // Everything after the leaf is the issuer chain, sent in intermediate_certificates.
            // Confirmed on the simulator (9.18.1): ONTAP accepts a full chain here, including a
            // self-signed root in the array, so no root filtering is required.
            var intermediatePems = orderedPems.Skip(1).ToList();
            var privateKeyPem = GetPrivateKeyPem(pfxBytes, password);

            return (leafPem, intermediatePems, privateKeyPem);
        }

        /// <summary>
        /// ONTAP_TRUSTED: base64 contents (PFX / PKCS7 / DER, key optional) -> a SINGLE certificate PEM.
        /// Trust anchors take exactly one certificate in public_certificate; intermediates are not accepted
        /// for the *_ca types, and no private key is emitted.
        /// </summary>
        public static string ConvertToSingleCertPem(string base64Contents, string password)
        {
            var bytes = DecodeBase64(base64Contents, nameof(base64Contents));
            var orderedPems = LoadOrderedCertPems(bytes, password, requirePrivateKey: false);
            return orderedPems[0];
        }

        private static byte[] DecodeBase64(string b64, string paramName)
        {
            if (string.IsNullOrEmpty(b64))
                throw new ArgumentException("Certificate contents cannot be null or empty", paramName);
            try
            {
                return Convert.FromBase64String(b64);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("Invalid base64 string format", paramName, ex);
            }
        }

        /// <summary>
        /// Reads certificates (public data only) and returns them as individual PEM blocks with the leaf
        /// first, followed by the issuer chain ordered by walking issuer/subject links rather than trusting
        /// any library's chain association. Certificates are disposed before returning.
        /// </summary>
        private static List<string> LoadOrderedCertPems(byte[] bytes, string password, bool requirePrivateKey)
        {
            var collection = new X509Certificate2Collection();
            // EphemeralKeySet: keep everything in memory; never touch the machine/user key store.
            collection.Import(bytes, password, X509KeyStorageFlags.EphemeralKeySet);
            try
            {
                var all = collection.Cast<X509Certificate2>().ToList();
                if (!all.Any())
                    throw new InvalidOperationException("No certificates found in the certificate contents.");

                X509Certificate2 leaf;
                if (requirePrivateKey)
                {
                    leaf = all.FirstOrDefault(c => c.HasPrivateKey)
                           ?? throw new InvalidOperationException("The supplied certificate does not contain a private key.");
                }
                else
                {
                    // Prefer a key-bearing entry if present; otherwise the end-entity (a cert that is not the
                    // issuer of any other cert in the set); otherwise just the first.
                    leaf = all.FirstOrDefault(c => c.HasPrivateKey)
                           ?? all.FirstOrDefault(c => !all.Any(o =>
                                  !ReferenceEquals(o, c) && o.IssuerName.RawData.SequenceEqual(c.SubjectName.RawData)))
                           ?? all.First();
                }

                var ordered = new List<X509Certificate2> { leaf };
                var remaining = all.Where(c => !ReferenceEquals(c, leaf)).ToList();
                var current = leaf;
                while (remaining.Count > 0 && !IsSelfSigned(current))
                {
                    var issuer = remaining.FirstOrDefault(
                        c => c.SubjectName.RawData.SequenceEqual(current.IssuerName.RawData));
                    if (issuer == null) break;
                    ordered.Add(issuer);
                    remaining.Remove(issuer);
                    current = issuer;
                }
                // Defensive: include any certs we couldn't place rather than silently dropping them.
                ordered.AddRange(remaining);

                return ordered.Select(ToPem).ToList();
            }
            finally
            {
                foreach (var cert in collection) cert.Dispose();
            }
        }

        private static string ToPem(X509Certificate2 cert)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-----BEGIN CERTIFICATE-----");
            sb.AppendLine(FormatBase64(Convert.ToBase64String(cert.RawData)));
            sb.AppendLine("-----END CERTIFICATE-----");
            return sb.ToString().TrimEnd();
        }

        private static bool IsSelfSigned(X509Certificate2 cert)
            => cert.SubjectName.RawData.SequenceEqual(cert.IssuerName.RawData);

        /// <summary>
        /// Exports the PFX private key as an unencrypted PKCS#8 PEM block via BouncyCastle, sourcing the
        /// key from a PKCS12 store to avoid the platform-specific .NET/CNG export path. Works for RSA and EC.
        /// </summary>
        private static string GetPrivateKeyPem(byte[] pfxBytes, string password)
        {
            var store = new Pkcs12StoreBuilder().Build();
            using (var ms = new MemoryStream(pfxBytes))
            {
                store.Load(ms, password?.ToCharArray() ?? new char[0]);
            }

            var keyAlias = store.Aliases.Cast<string>().FirstOrDefault(a => store.IsKeyEntry(a));
            if (keyAlias == null)
                throw new InvalidOperationException("The supplied certificate does not contain a private key.");

            return ExportPrivateKeyPem(store.GetKey(keyAlias).Key);
        }

        private static string ExportPrivateKeyPem(AsymmetricKeyParameter privateKey)
        {
            var pkcs8 = PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey);
            var der = pkcs8.GetDerEncoded();

            var sb = new StringBuilder();
            sb.AppendLine("-----BEGIN PRIVATE KEY-----");
            sb.AppendLine(FormatBase64(Convert.ToBase64String(der)));
            sb.AppendLine("-----END PRIVATE KEY-----");
            return sb.ToString().TrimEnd();
        }

        /// <summary>Wraps a base64 string at 64 characters per line (PEM convention).</summary>
        private static string FormatBase64(string base64)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < base64.Length; i += 64)
            {
                sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
            }
            return sb.ToString().TrimEnd();
        }
    }
}
