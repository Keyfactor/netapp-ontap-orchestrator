// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

using FluentAssertions;
using Xunit;

namespace Keyfactor.Extensions.Orchestrators.NetAppOntap.Tests
{
    public class OntapApiExceptionTests
    {
        // The exact 409 body observed from the simulator on a duplicate-name POST.
        private const string DuplicateBody =
            "{ \"error\": { \"message\": \"duplicate entry\", \"code\": \"1\", \"target\": \"uuid\" } }";

        [Fact]
        public void FromResponse_ParsesDuplicateEnvelope()
        {
            var ex = OntapApiException.FromResponse(409, DuplicateBody);

            ex.StatusCode.Should().Be(409);
            ex.IsConflict.Should().BeTrue();
            ex.ErrorCode.Should().Be("1");
            ex.Target.Should().Be("uuid");
            ex.Message.Should().Contain("duplicate entry");
            ex.Message.Should().Contain("409");
        }

        [Fact]
        public void FromResponse_NonConflictStatus_IsConflictFalse()
        {
            var ex = OntapApiException.FromResponse(403,
                "{ \"error\": { \"message\": \"permission denied\", \"code\": \"13\" } }");

            ex.IsConflict.Should().BeFalse();
            ex.ErrorCode.Should().Be("13");
            ex.Message.Should().Contain("permission denied");
        }

        [Fact]
        public void FromResponse_NonJsonBody_FallsBackToRawText()
        {
            var ex = OntapApiException.FromResponse(500, "<html>gateway error</html>");

            ex.StatusCode.Should().Be(500);
            ex.Message.Should().Contain("500");
            ex.Message.Should().Contain("gateway error");
        }

        [Fact]
        public void FromResponse_EmptyBody_StillReportsStatus()
        {
            var ex = OntapApiException.FromResponse(502, "");

            ex.Message.Should().Contain("502");
        }
    }
}
