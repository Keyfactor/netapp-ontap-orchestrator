
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

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Jobs
{
    // Enumerates the scopes on a cluster (the cluster itself + every SVM) and returns them as
    // candidate store paths, so an operator doesn't have to register each SVM by hand.
    [Job(KeyfactorJobType.DISCOVERY)]
    public class Discovery : JobBase<Discovery>, IDiscoveryJobExtension
    {
        public Discovery(IPAMSecretResolver resolver) : base(resolver) {
            _logger = LogHandler.GetClassLogger(GetType());
        }

        public JobResult ProcessJob(DiscoveryJobConfiguration config, SubmitDiscoveryUpdate submitDiscovery)
        {
            _logger.MethodEntry();
            _logger.LogTrace($"received new discovery job. Job ID = {config.JobId}");

            Initialize(config);

            try
            {
                // Store paths: the cluster sentinel, and one for each SVM.
                var storePaths = new List<string> { Constants.SCOPE_CLUSTER };

                var svmNames = _client.GetSvmNames().GetAwaiter().GetResult();
                storePaths.AddRange(svmNames);

                _logger.LogTrace($"discovered {storePaths.Count} scope(s): {string.Join(", ", storePaths)}");

                if (!submitDiscovery.Invoke(storePaths))
                    return FailureJobResult("Discovery callback failed. Review the orchestrator logs.");

                return SuccessJobResult($"Discovery completed. Found {storePaths.Count} scope(s) " +
                                        $"(cluster + {svmNames.Count} SVM(s)).");
            }
            catch (Exception ex)
            {
                return FailureJobResult($"Error performing Discovery job: {ex.Message}\nReview the orchestrator logs.");
            }
            finally
            {
                _logger.MethodExit();
            }
        }
    }
}
