
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap
{
    internal static class StorePropertyReader
    {
        /// <summary>
        /// Reads a boolean store-type custom field from the serialized Properties JSON. Tolerant of
        /// Command serializing the value as a real bool, a "true"/"false" string, or an object wrapping
        /// the value (e.g. { "value": "true" }). Key match is case-insensitive. Defaults to false.
        /// </summary>
        public static bool ReadBool(string propertiesJson, string name)
        {
            if (string.IsNullOrWhiteSpace(propertiesJson)) return false;

            try
            {
                var jObj = JObject.Parse(propertiesJson);
                if (!jObj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) || token == null)
                    return false;

                if (token.Type == JTokenType.Object)
                    token = token["value"];

                if (token == null || token.Type == JTokenType.Null)
                    return false;

                if (token.Type == JTokenType.Boolean)
                    return token.Value<bool>();

                return bool.TryParse(token.ToString(), out var parsed) && parsed;
            }
            catch
            {
                // Malformed or unexpected Properties JSON: fail safe to false.
                return false;
            }
        }

        /// <summary>
        /// Reads a boolean from a Dictionary&lt;string, object&gt; (the shape of DiscoveryJobConfiguration.JobProperties).
        /// Tolerant of the value being a bool, a "true"/"false" string, or null/missing.  Defaults to false.
        /// </summary>
        public static bool ReadBool(Dictionary<string, object> jobProperties, string name)
        {
            if (jobProperties == null) return false;

            // Case-insensitive key lookup (Dictionary default is ordinal).
            object raw = null;
            foreach (var kvp in jobProperties)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    raw = kvp.Value;
                    break;
                }
            }
            if (raw == null) return false;

            if (raw is bool b) return b;

            return bool.TryParse(raw.ToString(), out var parsed) && parsed;
        }
    }
}
