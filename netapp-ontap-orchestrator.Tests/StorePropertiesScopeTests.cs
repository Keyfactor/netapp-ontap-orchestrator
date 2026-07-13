// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

using FluentAssertions;
using Xunit;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    /// <summary>
    /// The store path encodes scope: "[cluster]" (or blank) => cluster scope (svm omitted on the API),
    /// anything else => that SVM name.  These assert the translation the whole integration depends on.
    /// </summary>
    public class StorePropertiesScopeTests
    {
        [Theory]
        [InlineData("[cluster]")]
        [InlineData("[CLUSTER]")]  // case-insensitive reserved token
        [InlineData("")]          // blank => cluster (defensive; store path is required)
        [InlineData("   ")]       // whitespace => cluster
        [InlineData(null)]        // null => cluster
        public void ClusterScope_YieldsNullSvmName(string? scope)
        {
            var sp = new OntapStoreProperties { Scope = scope };

            sp.IsClusterScope.Should().BeTrue();
            sp.SvmNameOrNull.Should().BeNull("cluster scope must omit the svm field on the REST call");
        }

        [Theory]
        [InlineData("vs0")]
        [InlineData("data_svm_1")]
        [InlineData("cluster")]   // the BARE word is a normal SVM name, NOT the cluster token
        public void SvmScope_YieldsThatSvmName(string scope)
        {
            var sp = new OntapStoreProperties { Scope = scope };

            sp.IsClusterScope.Should().BeFalse();
            sp.SvmNameOrNull.Should().Be(scope);
        }

        [Fact]
        public void BareClusterWord_IsTreatedAsRegularSvm_NotClusterScope()
        {
            // Collision-proofing guard: only the bracketed token "[cluster]" means cluster scope.
            // A customer may legitimately have an SVM named "cluster"; it must route as an SVM.
            // Do NOT "helpfully" add a bare-"cluster" branch to IsClusterScope -- that reopens the collision.
            var sp = new OntapStoreProperties { Scope = "cluster" };

            sp.IsClusterScope.Should().BeFalse();
            sp.SvmNameOrNull.Should().Be("cluster");
        }

        [Fact]
        public void Scope_LiteralSvmNamePreserved()
        {
            // Scope is set from StorePath.Trim() in JobBase; here we assert the property itself
            // does not further mangle a legitimate SVM name.
            var sp = new OntapStoreProperties { Scope = "vs0" };
            sp.SvmNameOrNull.Should().Be("vs0");
        }
    }
}
