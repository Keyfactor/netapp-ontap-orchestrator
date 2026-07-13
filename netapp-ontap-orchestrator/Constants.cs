
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap
{
    internal static class Constants
    {
        // Prefix used for logging / ExtensionName.
        public const string EXTENSION_NAME = "NetAppOntap";

        // Store types (must match the capability segment in manifest.json and the
        // Certificate Store Type ShortName configured in Keyfactor Command).
        public const string STORE_TYPE_CERTS = "ONTAP_CERTS";     // end-entity + signer certs (server, client)
        public const string STORE_TYPE_TRUSTED = "ONTAP_TRUSTED"; // trust anchors (server_ca, client_ca)

        // Reserved store-path token that indicates CLUSTER scope (as opposed to an SVM name).
        // Cluster scope on the REST API == the ABSENCE of an "svm" field on GET and the OMISSION of
        // "svm" on POST.  This token is a store-path convenience only; it must NEVER be sent to ONTAP
        // as an svm name.  The square brackets are deliberate: they put the value outside ONTAP's SVM
        // name character set, so it can never collide with a real SVM name, while still reading clearly
        // as "cluster" to an operator scanning the store list.
        //
        // IMPORTANT: the bare word "cluster" is NOT treated as this token -- doing so would reopen the
        // collision (an SVM legitimately named "cluster").  Operators must enter the literal "[cluster]",
        // brackets included; document this in the store-type parameter description.  Blank is accepted
        // defensively as cluster scope, but the store path is required so blank should not occur.
        public const string SCOPE_CLUSTER = "[cluster]";
    }

    /// <summary>
    /// ONTAP security-certificate "type" enum values (see /api/security/certificates).
    ///   server     - cert + private key; ONTAP acting as SSL server
    ///   client     - cert + private key; ONTAP acting as SSL client
    ///   server_ca  - trust anchor; used by ONTAP-as-client to verify an external server (the pre-installed public roots are these)
    ///   client_ca  - trust anchor; used by ONTAP-as-server to verify an incoming client cert
    ///   root_ca    - self-signed local signing CA (has a private key). Generated on the appliance; NOT installed. Out of scope for v1.
    /// </summary>
    public static class OntapCertType
    {
        public const string SERVER = "server";
        public const string CLIENT = "client";
        public const string SERVER_CA = "server_ca";
        public const string CLIENT_CA = "client_ca";
        public const string ROOT_CA = "root_ca";

        // The cert types each store type claims during inventory / accepts on management.
        public static readonly IReadOnlyList<string> CertsStoreTypes = new[] { SERVER, CLIENT };
        public static readonly IReadOnlyList<string> TrustedStoreTypes = new[] { SERVER_CA, CLIENT_CA };

        public static IReadOnlyList<string> ForStoreType(string storeType)
        {
            switch (storeType)
            {
                case Constants.STORE_TYPE_CERTS: return CertsStoreTypes;
                case Constants.STORE_TYPE_TRUSTED: return TrustedStoreTypes;
                default: throw new ArgumentException($"Unknown store type '{storeType}'.");
            }
        }
    }

    public static class KeyfactorJobType
    {
        public const string INVENTORY = "Inventory";
        public const string MANAGEMENT = "Management";
        public const string DISCOVERY = "Discovery";
    }

    /// <summary>Selected ONTAP REST error codes (the string in the error object's "code" field).</summary>
    public static class OntapErrorCodes
    {
        // HTTP 400 when attempting to DELETE a preinstalled (factory) trust certificate.
        // ONTAP blocks this server-side, so no factory-vs-admin discriminator is needed on our end.
        public const string PREINSTALLED_CERT_DELETE = "3735681";
    }

    /// <summary>Names of store-type custom fields (must match the "Name" values in integration-manifest.json).</summary>
    public static class StorePropertyNames
    {
        // When true, TLS server-certificate validation is skipped (lab clusters / the Simulator).
        public const string IGNORE_SSL_WARNING = "IgnoreSSLWarning";
    }

    /// <summary>
    /// Keys for entry parameters supplied per-certificate by Command on Management jobs.
    /// </summary>
    public static class EntryParameterKeys
    {
        // Required on both store types: which ONTAP cert "type" to create/install this entry as.
        //   ONTAP_TRUSTED -> server_ca | client_ca
        //   ONTAP_CERTS   -> server | client
        public const string CERT_TYPE = "CertType";
    }
}
