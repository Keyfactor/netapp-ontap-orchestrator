
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Models
{
    /// <summary>
    /// Maps a record from GET /api/security/certificates?fields=*
    /// Only the fields relevant to inventory/management are modeled here; extend as needed.
    /// IMPORTANT: the API never returns the private key on GET (it is write-only on install),
    /// so there is no private-key property here by design.
    /// </summary>
    public class OntapCertificate
    {
        [JsonProperty("uuid")]
        public string Uuid { get; set; }

        // Unique certificate name *within a scope* (per-SVM / per-cluster).  Not globally unique.
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("common_name")]
        public string CommonName { get; set; }

        // server | client | server_ca | client_ca | root_ca
        [JsonProperty("type")]
        public string Type { get; set; }

        // Present only for SVM-scoped certs.  Absent (null) => cluster scope.
        [JsonProperty("svm")]
        public OntapSvmRef Svm { get; set; }

        [JsonProperty("serial_number")]
        public string SerialNumber { get; set; }

        [JsonProperty("expiry_time")]
        public string ExpiryTime { get; set; }

        // PEM of the public certificate.
        [JsonProperty("public_certificate")]
        public string PublicCertificate { get; set; }

        // Inventory metadata. (A factory-vs-admin discriminator turned out to be unnecessary: ONTAP
        // blocks deletion of preinstalled roots server-side with HTTP 400 / code 3735681, so Remove is
        // inherently safe without one.)
        [JsonProperty("authority_key_identifier")]
        public string AuthorityKeyIdentifier { get; set; }

        [JsonIgnore]
        public bool IsClusterScoped => Svm == null || string.IsNullOrEmpty(Svm.Name);
    }

    public class OntapSvmRef
    {
        [JsonProperty("uuid")]
        public string Uuid { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// <summary>Generic ONTAP REST collection envelope: { "records": [...], "num_records": N }.</summary>
    public class OntapCollection<T>
    {
        [JsonProperty("records")]
        public List<T> Records { get; set; } = new List<T>();

        [JsonProperty("num_records")]
        public int NumRecords { get; set; }
    }
}
