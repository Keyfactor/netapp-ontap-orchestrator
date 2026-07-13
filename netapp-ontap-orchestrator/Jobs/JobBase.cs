
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Reflection;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Jobs
{
    public class JobBase<T> : IOrchestratorJobExtension
    {
        public string ExtensionName => Constants.EXTENSION_NAME;

        internal ILogger _logger { get; set; }
        internal IPAMSecretResolver _resolver { get; set; }
        public virtual OntapClient _client { get; set; }
        internal protected virtual OntapJobParameters JobParameters { get; set; }

        public JobBase(IPAMSecretResolver resolver)
        {
            _logger = LogHandler.GetClassLogger(GetType());
            _resolver = resolver;
            _client = new OntapClient();
        }

        // ---- Initialize overloads (one per job config type) ---------------------------------

        public virtual void Initialize(InventoryJobConfiguration config)
        {
            _logger.MethodEntry();
            LogPluginVersion();

            JobParameters = new OntapJobParameters
            {
                StoreType = StoreTypeFromCapability(config.Capability),
                JobType = KeyfactorJobType.INVENTORY,
                JobId = config.JobId,
                JobHistoryId = config.JobHistoryId
            };

            SetStoreProperties(config.CertificateStoreDetails, config.ServerUsername, config.ServerPassword);
            InitializeClient();
            _logger.MethodExit();
        }

        public virtual void Initialize(ManagementJobConfiguration config)
        {
            _logger.MethodEntry();
            LogPluginVersion();

            JobParameters = new OntapJobParameters
            {
                StoreType = StoreTypeFromCapability(config.Capability),
                JobType = KeyfactorJobType.MANAGEMENT,
                JobId = config.JobId,
                JobHistoryId = config.JobHistoryId
            };

            SetStoreProperties(config.CertificateStoreDetails, config.ServerUsername, config.ServerPassword);
            SetCertProperties(config);
            InitializeClient();
            _logger.MethodExit();
        }

        public virtual void Initialize(DiscoveryJobConfiguration config)
        {
            _logger.MethodEntry();
            LogPluginVersion();

            JobParameters = new OntapJobParameters
            {
                StoreType = StoreTypeFromCapability(config.Capability),
                JobType = KeyfactorJobType.DISCOVERY,
                JobId = config.JobId,
                JobHistoryId = config.JobHistoryId
            };

            // Discovery has no store path/scope; it enumerates scopes against the cluster.
            JobParameters.StoreProperties.ClusterManagementHost = config.ClientMachine;
            JobParameters.StoreProperties.Username = ResolvePamField("ServerUsername", config.ServerUsername);
            JobParameters.StoreProperties.Password = ResolvePamField("ServerPassword", config.ServerPassword);
            InitializeClient();
            _logger.MethodExit();
        }

        // ---- Shared setup helpers -----------------------------------------------------------

        // Capability looks like "CertStores.ONTAP_CERTS.Inventory" -> segment [1] is the store type.
        private string StoreTypeFromCapability(string capability)
        {
            var storeType = capability?.Split('.').Length > 1 ? capability.Split('.')[1] : null;
            _logger.LogTrace($"storeType resolved from capability '{capability}': {storeType}");
            return storeType;
        }

        private void SetStoreProperties(CertificateStore storeProps, string serverUsername, string serverPassword)
        {
            _logger.MethodEntry();

            JobParameters.StoreProperties.ClusterManagementHost = storeProps.ClientMachine;

            // The store path is the scope: "[cluster]" (or blank) => cluster scope; otherwise an SVM name.
            JobParameters.StoreProperties.Scope = storeProps.StorePath?.Trim();
            _logger.LogTrace($"scope from store path: '{JobParameters.StoreProperties.Scope}' " +
                             $"(cluster scope = {JobParameters.StoreProperties.IsClusterScope})");

            JobParameters.StoreProperties.Username = ResolvePamField("ServerUsername", serverUsername);
            JobParameters.StoreProperties.Password = ResolvePamField("ServerPassword", serverPassword);

            JobParameters.StoreProperties.IgnoreSslWarning =
                StorePropertyReader.ReadBool(storeProps.Properties, StorePropertyNames.IGNORE_SSL_WARNING);
            _logger.LogTrace($"IgnoreSslWarning = {JobParameters.StoreProperties.IgnoreSslWarning}");

            _logger.MethodExit();
        }

        private void SetCertProperties(ManagementJobConfiguration config)
        {
            _logger.MethodEntry();

            var cert = config.JobCertificate;
            JobParameters.CertProperties.Alias = cert?.Alias;
            JobParameters.CertProperties.Contents = cert?.Contents;
            JobParameters.CertProperties.PrivateKeyPassword = cert?.PrivateKeyPassword;
            JobParameters.CertProperties.Thumbprint = cert?.Thumbprint;
            JobParameters.CertProperties.Overwrite = config.Overwrite;

            // CertType entry parameter (server|client for CERTS, server_ca|client_ca for TRUSTED).
            if (config.JobProperties != null &&
                config.JobProperties.TryGetValue(EntryParameterKeys.CERT_TYPE, out var certType))
            {
                JobParameters.CertProperties.CertType = certType?.ToString();
            }
            _logger.LogTrace($"CertType entry parameter: {JobParameters.CertProperties.CertType ?? "(not provided)"}");

            _logger.MethodExit();
        }

        private void InitializeClient()
        {
            var sp = JobParameters.StoreProperties;
            _client.InitializeClient(sp.ClusterManagementHost, sp.Username, sp.Password, sp.IgnoreSslWarning);
        }

        private string ResolvePamField(string name, string value)
        {
            _logger.LogTrace($"attempting to resolve PAM-eligible field {name}");
            return _resolver.Resolve(value);
        }

        protected void LogPluginVersion()
        {
            var asm = Assembly.GetExecutingAssembly().GetName();
            _logger.LogTrace($"Keyfactor Orchestrator Extension for NetApp ONTAP - {asm?.Name} v{asm?.Version}");
        }

        // ---- JobResult helpers --------------------------------------------------------------

        private protected JobResult SuccessJobResult(string message = null) => new JobResult
        {
            Result = OrchestratorJobStatusJobResult.Success,
            JobHistoryId = JobParameters.JobHistoryId,
            FailureMessage = message
        };

        private protected JobResult WarningJobResult(string message = null) => new JobResult
        {
            Result = OrchestratorJobStatusJobResult.Warning,
            JobHistoryId = JobParameters.JobHistoryId,
            FailureMessage = message
        };

        private protected JobResult FailureJobResult(string message = null) => new JobResult
        {
            Result = OrchestratorJobStatusJobResult.Failure,
            JobHistoryId = JobParameters.JobHistoryId,
            FailureMessage = message
        };
    }
}
