
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap
{
    public class OntapJobParameters
    {
        public string JobType { get; set; }
        public string StoreType { get; set; }   // ONTAP_CERTS | ONTAP_TRUSTED
        public Guid JobId { get; set; }
        public long JobHistoryId { get; set; }

        public OntapStoreProperties StoreProperties { get; set; }
        public OntapCertProperties CertProperties { get; set; }

        public OntapJobParameters()
        {
            StoreProperties = new OntapStoreProperties();
            CertProperties = new OntapCertProperties();
        }
    }

    public class OntapStoreProperties
    {
        // Cluster management host/IP (Command's ClientMachine).  REST base becomes https://{host}/api
        public string ClusterManagementHost { get; set; }

        // Raw value from the store path.  "[cluster]" (or blank) => cluster scope; otherwise an SVM name.
        public string Scope { get; set; }

        // Basic-auth credentials, resolved through the PAM secret resolver.
        public string Username { get; set; }
        public string Password { get; set; }

        // When true, TLS server-certificate validation is skipped when connecting to ONTAP (for lab
        // clusters / the Simulator, which present a self-signed cert). Defaults to false: secure by default.
        public bool IgnoreSslWarning { get; set; }

        // True when this store targets cluster scope rather than a specific SVM.
        public bool IsClusterScope =>
            string.IsNullOrWhiteSpace(Scope) ||
            string.Equals(Scope, Constants.SCOPE_CLUSTER, StringComparison.OrdinalIgnoreCase);

        // The SVM name to send to the REST API, or null for cluster scope (svm omitted on POST / absent on GET).
        public string SvmNameOrNull => IsClusterScope ? null : Scope;
    }

    public class OntapCertProperties
    {
        public string Alias { get; set; }             // maps to ONTAP cert "name" (unique within scope)
        public string Contents { get; set; }          // Base64 PFX (CERTS add) or public cert (TRUSTED add) from Command
        public string PrivateKeyPassword { get; set; }
        public string Thumbprint { get; set; }
        public bool Overwrite { get; set; }

        // Which ONTAP "type" to install this entry as.  From the CertType entry parameter.
        //   ONTAP_CERTS   -> server | client
        //   ONTAP_TRUSTED -> server_ca | client_ca
        public string CertType { get; set; }
    }
}
