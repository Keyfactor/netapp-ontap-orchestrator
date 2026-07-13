// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

using FluentAssertions;
using Xunit;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    public class StorePropertyReaderTests
    {
        private const string Name = "IgnoreSSLWarning";

        [Theory]
        [InlineData("{\"IgnoreSSLWarning\":\"true\"}")]     // string "true"
        [InlineData("{\"IgnoreSSLWarning\":true}")]         // real bool
        [InlineData("{\"ignoresslwarning\":\"true\"}")]     // case-insensitive key
        [InlineData("{\"IgnoreSSLWarning\":{\"value\":\"true\"}}")] // wrapped object
        public void ReadBool_TrueVariants_ReturnTrue(string json)
        {
            StorePropertyReader.ReadBool(json, Name).Should().BeTrue();
        }

        [Theory]
        [InlineData("{\"IgnoreSSLWarning\":\"false\"}")]
        [InlineData("{\"IgnoreSSLWarning\":false}")]
        [InlineData("{\"IgnoreSSLWarning\":{\"value\":\"false\"}}")]
        [InlineData("{\"SomethingElse\":\"true\"}")]        // key absent
        public void ReadBool_FalseOrMissing_ReturnFalse(string json)
        {
            StorePropertyReader.ReadBool(json, Name).Should().BeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("{ malformed")]
        public void ReadBool_NullEmptyOrMalformed_FailSafeToFalse(string json)
        {
            StorePropertyReader.ReadBool(json, Name).Should().BeFalse();
        }
    }
}
