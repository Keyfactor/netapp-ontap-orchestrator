
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Models
{
    /// <summary>
    /// ONTAP REST error envelope, e.g.:
    ///   { "error": { "message": "duplicate entry", "code": "1", "target": "uuid" } }
    /// Note: "code" is returned as a string.
    /// </summary>
    public class OntapErrorEnvelope
    {
        [JsonProperty("error")]
        public OntapError Error { get; set; }
    }

    public class OntapError
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("target")]
        public string Target { get; set; }
    }
}
