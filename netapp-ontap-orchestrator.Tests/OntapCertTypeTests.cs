
//  Copyright 2026 Keyfactor
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
//  and limitations under the License.

using System;
using FluentAssertions;
using Xunit;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    /// <summary>
    /// The type partition is what keeps ONTAP_CERTS and ONTAP_TRUSTED inventories non-overlapping,
    /// and keeps root_ca out of both.
    /// </summary>
    public class OntapCertTypeTests
    {
        [Fact]
        public void CertsStore_ClaimsServerAndClient_Only()
        {
            var types = OntapCertType.ForStoreType(Constants.STORE_TYPE_CERTS);
            types.Should().BeEquivalentTo(new[] { OntapCertType.SERVER, OntapCertType.CLIENT });
            types.Should().NotContain(OntapCertType.ROOT_CA);
        }

        [Fact]
        public void TrustedStore_ClaimsServerCaAndClientCa_Only()
        {
            var types = OntapCertType.ForStoreType(Constants.STORE_TYPE_TRUSTED);
            types.Should().BeEquivalentTo(new[] { OntapCertType.SERVER_CA, OntapCertType.CLIENT_CA });
            types.Should().NotContain(OntapCertType.ROOT_CA);
        }

        [Fact]
        public void UnknownStoreType_Throws()
        {
            Action act = () => OntapCertType.ForStoreType("SOMETHING_ELSE");
            act.Should().Throw<ArgumentException>();
        }
    }
}
