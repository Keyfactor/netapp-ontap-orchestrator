
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Keyfactor.Extensions.Orchestrators.NetAppOntap.Models;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap
{
    /// <summary>
    /// Thin wrapper over the ONTAP REST API (/api/security/certificates and /api/svm/svms).
    /// One instance per job; call InitializeClient before use.
    /// </summary>
    public class OntapClient
    {
        private readonly ILogger _logger;
        private HttpClient _http;

        public OntapClient()
        {
            _logger = LogHandler.GetClassLogger<OntapClient>();
        }

        public virtual void InitializeClient(string host, string username, string password, bool ignoreSslWarning = false)
        {
            _logger.MethodEntry();

            // Secure by default: validate the server certificate.  Only bypass validation when the store
            // explicitly opts in via the IgnoreSSLWarning custom field -- for lab clusters / the Simulator,
            // which present a self-signed cert.  (Note: strict validation also enforces hostname/SAN match,
            // so connect by a name present in the certificate, not by IP.)
            var handler = new HttpClientHandler();
            if (ignoreSslWarning)
            {
                _logger.LogWarning("IgnoreSSLWarning is enabled; skipping TLS certificate validation for the ONTAP connection.");
                handler.ServerCertificateCustomValidationCallback = (_, __, ___, ____) => true;
            }

            var baseUri = host.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? host.TrimEnd('/')
                : $"https://{host.TrimEnd('/')}";

            _http = new HttpClient(handler) { BaseAddress = new Uri(baseUri + "/") };

            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/hal+json"));

            _logger.LogTrace($"initialized ONTAP client against {baseUri}/api");
            _logger.MethodExit();
        }

        /// <summary>
        /// GET /api/security/certificates?fields=*  filtered to the given scope and set of types.
        /// Pass svmName = null for cluster scope.
        /// </summary>
        public virtual async Task<List<OntapCertificate>> GetCertificates(string svmName, IReadOnlyList<string> types)
        {
            _logger.MethodEntry();

            // TODO: confirm the exact query-param filtering ONTAP supports for type and scope.
            //   - type filter:   ?type=server|client ... (comma / repeated?) -- verify on sim
            //   - svm filter:    ?svm.name={name} for SVM scope; for cluster scope, records simply have no svm.
            // Safest baseline: request fields=* and filter client-side by type + IsClusterScoped/svm.name.
            var response = await _http.GetAsync("api/security/certificates?fields=*");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            var collection = JsonConvert.DeserializeObject<OntapCollection<OntapCertificate>>(body)
                             ?? new OntapCollection<OntapCertificate>();

            var typeSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);

            var filtered = collection.Records.Where(c =>
                typeSet.Contains(c.Type) &&
                (string.IsNullOrEmpty(svmName)
                    ? c.IsClusterScoped
                    : string.Equals(c.Svm?.Name, svmName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _logger.LogTrace($"returning {filtered.Count} of {collection.NumRecords} certificate(s) after scope/type filtering");
            _logger.MethodExit();
            return filtered;
        }

        /// <summary>
        /// GET /api/svm/svms?fields=name  -- used by Discovery to enumerate scopes.
        /// </summary>
        public virtual async Task<List<string>> GetSvmNames()
        {
            _logger.MethodEntry();
            var response = await _http.GetAsync("api/svm/svms?fields=name");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            var collection = JsonConvert.DeserializeObject<OntapCollection<OntapSvmRef>>(body)
                             ?? new OntapCollection<OntapSvmRef>();

            _logger.MethodExit();
            return collection.Records.Select(s => s.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
        }

        /// <summary>
        /// Finds a certificate by name within a scope.  Needed for the delete-then-add replace path.
        /// </summary>
        public virtual async Task<OntapCertificate> FindByName(string name, string svmName)
        {
            var all = await GetCertificates(svmName, new[]
            {
                OntapCertType.SERVER, OntapCertType.CLIENT, OntapCertType.SERVER_CA, OntapCertType.CLIENT_CA
            });
            return all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// POST /api/security/certificates  -- install a CA-signed cert (leaf + intermediates + private key)
        /// or a trust anchor (single public cert only).  Omit svmName for cluster scope.
        ///   - CERTS (server/client): publicCertPem = leaf, intermediateCertPems = chain, privateKeyPem = key.
        ///   - TRUSTED (server_ca/client_ca): publicCertPem = single cert; intermediateCertPems/privateKeyPem null
        ///     (ONTAP rejects intermediates for *_ca types, and a private key for *_ca types).
        /// </summary>
        public virtual async Task InstallCertificate(
            string name, string type, string publicCertPem,
            IEnumerable<string> intermediateCertPems, string privateKeyPem, string svmName)
        {
            _logger.MethodEntry();

            var payload = new Dictionary<string, object>
            {
                ["name"] = name,
                ["type"] = type,
                ["public_certificate"] = publicCertPem
            };

            var intermediates = intermediateCertPems?.Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (intermediates != null && intermediates.Count > 0)
                payload["intermediate_certificates"] = intermediates;

            if (!string.IsNullOrEmpty(privateKeyPem)) payload["private_key"] = privateKeyPem;
            if (!string.IsNullOrEmpty(svmName)) payload["svm"] = new { name = svmName };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Confirmed on the simulator: a POST whose name already exists in-scope returns HTTP 409
            // with { "error": { "message": "duplicate entry", "code": "1", "target": "uuid" } }.
            // AddCertificate pre-checks and does delete-then-POST, so a 409 here means a race or a
            // scope/lookup mismatch; surface it as a typed OntapApiException (IsConflict == true).
            var response = await _http.PostAsync("api/security/certificates", content);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw OntapApiException.FromResponse((int)response.StatusCode, err);
            }
            _logger.MethodExit();
        }

        /// <summary>DELETE /api/security/certificates/{uuid}.</summary>
        public virtual async Task DeleteCertificate(string uuid)
        {
            _logger.MethodEntry();
            var response = await _http.DeleteAsync($"api/security/certificates/{uuid}");

            // Confirmed on the simulator: preinstalled (factory) trust roots cannot be deleted via REST --
            // ONTAP returns HTTP 400 with code 3735681.  The typed exception preserves the message/code so
            // Management can surface it clearly (see OntapErrorCodes.PREINSTALLED_CERT_DELETE).
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw OntapApiException.FromResponse((int)response.StatusCode, err);
            }
            _logger.MethodExit();
        }
    }
}
