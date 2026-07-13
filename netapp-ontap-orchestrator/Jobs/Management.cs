
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Jobs
{
    [Job(KeyfactorJobType.MANAGEMENT)]
    public class Management : JobBase<Management>, IManagementJobExtension
    {
        public Management(IPAMSecretResolver resolver) : base(resolver)
        {
            _logger = LogHandler.GetClassLogger(GetType());
        }

        public JobResult ProcessJob(ManagementJobConfiguration config)
        {
            _logger.MethodEntry();
            var op = config.OperationType;
            _logger.LogTrace($"received new Management > {op} job. Job ID = {config.JobId}");

            Initialize(config);

            try
            {
                switch (op)
                {
                    case CertStoreOperationType.Add:
                        return AddCertificate();
                    case CertStoreOperationType.Remove:
                        return RemoveCertificate();
                    default:
                        return FailureJobResult($"Unsupported operation: {op}");
                }
            }
            catch (Exception ex)
            {
                var msg = $"Management > {op} job failed: {ex.Message}";
                _logger.LogError(msg);
                return FailureJobResult(msg);
            }
            finally
            {
                _logger.MethodExit();
            }
        }

        private JobResult AddCertificate()
        {
            _logger.MethodEntry();

            var certType = JobParameters.CertProperties.CertType;
            var storeType = JobParameters.StoreType;
            var alias = JobParameters.CertProperties.Alias;
            var svmName = JobParameters.StoreProperties.SvmNameOrNull;

            // --- Guard 1: root_ca is never installed; it must be generated on the appliance. ---
            if (string.Equals(certType, OntapCertType.ROOT_CA, StringComparison.OrdinalIgnoreCase))
                return FailureJobResult(
                    "A root_ca certificate cannot be added through this integration. " +
                    "Local signing CAs must be generated on the appliance. " +
                    "(Managing local CAs is out of scope for this release.)");

            // --- Guard 2: CertType must be valid for this store type. ---
            var allowed = OntapCertType.ForStoreType(storeType);
            if (string.IsNullOrEmpty(certType) || !allowed.Contains(certType, StringComparer.OrdinalIgnoreCase))
                return FailureJobResult(
                    $"CertType '{certType ?? "(none)"}' is not valid for store type {storeType}. " +
                    $"Expected one of: {string.Join(", ", allowed)}.");

            // --- Guard 3: extract the material ONTAP needs.  Trust anchors are public-only; ---
            // --- a CERTS entry carries cert + private key.  CertUtilities enforces the key rules. ---
            var isTrust = storeType == Constants.STORE_TYPE_TRUSTED;

            var contents = JobParameters.CertProperties.Contents;
            if (string.IsNullOrEmpty(contents))
                return FailureJobResult("No certificate contents were supplied for the Add operation.");

            string publicCertPem;
            List<string> intermediatePems = null;
            string privateKeyPem = null;
            try
            {
                if (isTrust)
                {
                    // Trust anchor: a single public certificate. No intermediates, no private key
                    // (ONTAP rejects both for server_ca/client_ca types).
                    publicCertPem = CertUtilities.ConvertToSingleCertPem(
                        contents, JobParameters.CertProperties.PrivateKeyPassword);
                }
                else
                {
                    // CERTS: leaf -> public_certificate, chain -> intermediate_certificates, key -> private_key.
                    (publicCertPem, intermediatePems, privateKeyPem) = CertUtilities.ConvertPfxForCertsStore(
                        contents, JobParameters.CertProperties.PrivateKeyPassword);
                }
            }
            catch (Exception ex)
            {
                return FailureJobResult($"Failed to process the supplied certificate for '{alias}': {ex.Message}");
            }

            // --- Replace semantics: POST is create-only (confirmed HTTP 409 on duplicate name in ---
            // --- scope), so overwrite is implemented as delete-then-add.                           ---
            var existing = _client.FindByName(alias, svmName).GetAwaiter().GetResult();
            if (existing != null)
            {
                if (!JobParameters.CertProperties.Overwrite)
                    return FailureJobResult(
                        $"A certificate named '{alias}' already exists in " +
                        $"{(svmName == null ? "cluster" : $"SVM '{svmName}'")} scope and Overwrite is not set.");

                _logger.LogTrace($"overwrite requested; deleting existing '{alias}' ({existing.Uuid}) before re-adding");
                _client.DeleteCertificate(existing.Uuid).GetAwaiter().GetResult();
            }

            _client.InstallCertificate(alias, certType, publicCertPem, intermediatePems, privateKeyPem, svmName).GetAwaiter().GetResult();

            _logger.MethodExit();
            return SuccessJobResult($"Successfully added certificate '{alias}' (type {certType}).");
        }

        private JobResult RemoveCertificate()
        {
            _logger.MethodEntry();

            var alias = JobParameters.CertProperties.Alias;
            var svmName = JobParameters.StoreProperties.SvmNameOrNull;

            var existing = _client.FindByName(alias, svmName).GetAwaiter().GetResult();
            if (existing == null)
                return SuccessJobResult($"Certificate '{alias}' was not found in scope; nothing to remove.");

            // Preinstalled (factory) trust roots cannot be deleted via REST -- ONTAP returns HTTP 400
            // with code 3735681 and directs the operator to the CLI.  Surface that clearly rather than
            // as a generic failure.  (No factory-vs-admin discriminator is needed; ONTAP enforces this.)
            try
            {
                _client.DeleteCertificate(existing.Uuid).GetAwaiter().GetResult();
            }
            catch (OntapApiException ex) when (ex.ErrorCode == OntapErrorCodes.PREINSTALLED_CERT_DELETE)
            {
                return FailureJobResult(
                    $"'{alias}' is a preinstalled certificate and cannot be removed through this integration. " +
                    "Preinstalled trust anchors must be managed with the ONTAP CLI.");
            }

            _logger.MethodExit();
            return SuccessJobResult($"Successfully removed certificate '{alias}'.");
        }
    }
}
