// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

using Keyfactor.Extensions.Orchestrators.NetAppOntap.Jobs;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    // Test doubles: Initialize is a no-op so no real HTTP client wiring happens; tests inject a
    // mocked OntapClient (via the public _client property) and pre-populate JobParameters.

    internal class TestableManagement : Management
    {
        public TestableManagement(IPAMSecretResolver resolver) : base(resolver) { }

        public override void Initialize(ManagementJobConfiguration config) { }
        public override void Initialize(InventoryJobConfiguration config) { }
        public override void Initialize(DiscoveryJobConfiguration config) { }

        public OntapJobParameters PublicJobParameters
        {
            get => JobParameters;
            set => JobParameters = value;
        }
    }

    internal class TestableInventory : Inventory
    {
        public TestableInventory(IPAMSecretResolver resolver) : base(resolver) { }

        public override void Initialize(ManagementJobConfiguration config) { }
        public override void Initialize(InventoryJobConfiguration config) { }
        public override void Initialize(DiscoveryJobConfiguration config) { }

        public OntapJobParameters PublicJobParameters
        {
            get => JobParameters;
            set => JobParameters = value;
        }
    }

    internal class TestableDiscovery : Discovery
    {
        public TestableDiscovery(IPAMSecretResolver resolver) : base(resolver) { }

        public override void Initialize(ManagementJobConfiguration config) { }
        public override void Initialize(InventoryJobConfiguration config) { }
        public override void Initialize(DiscoveryJobConfiguration config) { }

        public OntapJobParameters PublicJobParameters
        {
            get => JobParameters;
            set => JobParameters = value;
        }
    }
}
