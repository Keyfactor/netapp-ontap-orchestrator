
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using Keyfactor.Extensions.Orchestrators.NetAppOntap.Models;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap
{
    /// <summary>
    /// Raised when an ONTAP REST call returns a non-success status. Carries the HTTP status and the
    /// parsed ONTAP error fields so callers can react to specific conditions (e.g. IsConflict for 409)
    /// and so the surfaced message is the ONTAP text rather than raw JSON.
    /// </summary>
    public class OntapApiException : Exception
    {
        public int StatusCode { get; }
        public string ErrorCode { get; }
        public string Target { get; }

        /// <summary>True for HTTP 409 (e.g. a duplicate certificate name in scope).</summary>
        public bool IsConflict => StatusCode == 409;

        public OntapApiException(int statusCode, string errorCode, string target, string message)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
            Target = target;
        }

        /// <summary>
        /// Builds an exception from an HTTP status and response body, parsing the ONTAP error envelope
        /// when present and falling back to the raw body otherwise.
        /// </summary>
        public static OntapApiException FromResponse(int statusCode, string body)
        {
            string message = null, code = null, target = null;
            try
            {
                var envelope = JsonConvert.DeserializeObject<OntapErrorEnvelope>(body);
                if (envelope?.Error != null)
                {
                    message = envelope.Error.Message;
                    code = envelope.Error.Code;
                    target = envelope.Error.Target;
                }
            }
            catch
            {
                // Non-JSON or unexpected body; fall back to the raw text below.
            }

            var sb = new System.Text.StringBuilder($"ONTAP returned HTTP {statusCode}");
            if (!string.IsNullOrEmpty(message)) sb.Append($": {message}");
            else if (!string.IsNullOrEmpty(body)) sb.Append($": {Truncate(body, 500)}");

            var extras = new List<string>();
            if (!string.IsNullOrEmpty(code)) extras.Add($"code {code}");
            if (!string.IsNullOrEmpty(target)) extras.Add($"target {target}");
            if (extras.Count > 0) sb.Append($" ({string.Join(", ", extras)})");

            return new OntapApiException(statusCode, code, target, sb.ToString());
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
