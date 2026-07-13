
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using Keyfactor.Extensions.Orchestrators.NetAppOntap.Models;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Jobs
{
    // Finds all certificates in scope (cluster or a specific SVM) whose ONTAP "type" belongs to this
    // store type, and returns them to Command.  Private keys are never returned (the API doesn't expose them).
    [Job(KeyfactorJobType.INVENTORY)]
    public class Inventory : JobBase<Inventory>, IInventoryJobExtension
    {
        public Inventory(IPAMSecretResolver resolver) : base(resolver) {
            _logger = LogHandler.GetClassLogger(GetType());
        }

        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventory)
        {
            _logger.MethodEntry();
            _logger.LogTrace($"received new inventory job. Job ID = {config.JobId}");

            Initialize(config);

            try
            {
                var types = OntapCertType.ForStoreType(JobParameters.StoreType);
                var svmName = JobParameters.StoreProperties.SvmNameOrNull;

                _logger.LogTrace($"inventorying types [{string.Join(",", types)}] in " +
                                 $"{(svmName == null ? "cluster" : $"SVM '{svmName}'")} scope");

                var certs = _client.GetCertificates(svmName, types).GetAwaiter().GetResult();

                var inventoryItems = new List<CurrentInventoryItem>();
                var warnings = new List<string>();

                foreach (var cert in certs)
                {
                    try
                    {
                        inventoryItems.Add(ToInventoryItem(cert));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"skipping certificate '{cert.Name}' ({cert.Uuid}): {ex.Message}");
                        warnings.Add($"{cert.Name}: {ex.Message}");
                    }
                }

                if (!submitInventory.Invoke(inventoryItems))
                    return FailureJobResult("Inventory callback failed. Review the orchestrator logs.");

                var msg = $"Successfully processed {inventoryItems.Count} certificate(s).";
                if (warnings.Count > 0)
                    msg += $"\n{warnings.Count} certificate(s) could not be processed; review the logs.";

                return SuccessJobResult(msg);
            }
            catch (Exception ex)
            {
                return FailureJobResult($"Error performing Inventory job: {ex.Message}\nReview the orchestrator logs.");
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private CurrentInventoryItem ToInventoryItem(OntapCertificate cert)
        {
            // Command expects Base64-encoded DER (cer) certificates.  ONTAP returns PEM.
            // Trust anchors (server_ca/client_ca) have no private key; server/client do (even though
            // the key can never be read back), so reflect that in PrivateKeyEntry.
            var base64Der = ToBase64Der(cert.PublicCertificate);
            var hasKey = string.Equals(cert.Type, OntapCertType.SERVER, StringComparison.OrdinalIgnoreCase)
                      || string.Equals(cert.Type, OntapCertType.CLIENT, StringComparison.OrdinalIgnoreCase);

            var item = new CurrentInventoryItem
            {
                Alias = cert.Name,                       // name is unique within scope
                Certificates = new[] { base64Der },
                PrivateKeyEntry = hasKey,
                UseChainLevel = false,
                Parameters = new Dictionary<string, object>
                {
                    { EntryParameterKeys.CERT_TYPE, cert.Type }
                }
            };

            // NOTE: factory and admin-added trust anchors are inventoried uniformly. This is safe because
            // ONTAP blocks deletion of preinstalled roots server-side (HTTP 400 / code 3735681), so a Remove
            // on a built-in fails cleanly rather than doing damage. Visibly flagging factory roots would be
            // optional polish, not a safety requirement, and ONTAP exposes no clean discriminator for it.
            return item;
        }

        private static string ToBase64Der(string pem)
        {
            if (string.IsNullOrWhiteSpace(pem))
                throw new ArgumentException("certificate PEM was empty");

            // Load the leaf and re-export as raw DER, then Base64.
            using (var x509 = X509Certificate2.CreateFromPem(pem))
            {
                return Convert.ToBase64String(x509.Export(X509ContentType.Cert));
            }
        }
    }
}
