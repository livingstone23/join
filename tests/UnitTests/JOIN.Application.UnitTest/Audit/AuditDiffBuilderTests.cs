// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Infrastructure.Audit;

namespace JOIN.Application.UnitTest.Audit;

/// <summary>
/// SPEC 29 F11 — verifies the contract of <see cref="AuditDiffBuilder"/>.
/// Six cases from the spec: diff captures only changed keys; empty diff returns
/// <c>(null, null)</c>; blacklisted keys are discarded entirely; a key present
/// only on the new side appears with <c>OldValue = null</c>; <c>null</c> and
/// <see cref="string.Empty"/> count as different; Created with <c>oldValues = null</c>.
/// </summary>
public sealed class AuditDiffBuilderTests
{
    [Fact]
    public void Build_WhenOnlyOneFieldChanged_ReturnsSingleKeyPair()
    {
        var oldValues = new Dictionary<string, object?>
        {
            ["CanRead"] = true,
            ["Name"] = "Admin"
        };
        var newValues = new Dictionary<string, object?>
        {
            ["CanRead"] = false,
            ["Name"] = "Admin"
        };

        var (oldJson, newJson) = AuditDiffBuilder.Build(oldValues, newValues);

        oldJson.Should().NotBeNull();
        newJson.Should().NotBeNull();
        oldJson.Should().Contain("CanRead");
        oldJson.Should().NotContain("Name");
        newJson.Should().Contain("CanRead");
        newJson.Should().NotContain("Name");
    }

    [Fact]
    public void Build_WhenNothingChanged_ReturnsNullNull()
    {
        var values = new Dictionary<string, object?>
        {
            ["CanRead"] = true,
            ["CanCreate"] = true
        };

        var (oldJson, newJson) = AuditDiffBuilder.Build(values, values);

        oldJson.Should().BeNull();
        newJson.Should().BeNull();
    }

    [Fact]
    public void Build_WhenFieldIsBlacklisted_DropsItEntirely()
    {
        var oldValues = new Dictionary<string, object?>
        {
            ["PasswordHash"] = "old-hash",
            ["Email"] = "u@x.com"
        };
        var newValues = new Dictionary<string, object?>
        {
            ["PasswordHash"] = "new-hash",
            ["Email"] = "u@x.com"
        };

        var (oldJson, newJson) = AuditDiffBuilder.Build(oldValues, newValues);

        oldJson.Should().BeNull();
        newJson.Should().BeNull();
    }

    [Fact]
    public void Build_WhenKeyOnlyOnNewSide_OldValueIsNull()
    {
        var oldValues = new Dictionary<string, object?>
        {
            ["Email"] = "u@x.com"
        };
        var newValues = new Dictionary<string, object?>
        {
            ["Email"] = "u@x.com",
            ["Phone"] = "+1234"
        };

        var (oldJson, newJson) = AuditDiffBuilder.Build(oldValues, newValues);

        newJson.Should().NotBeNull();
        newJson.Should().Contain("Phone");
        oldJson.Should().NotBeNull();
        oldJson.Should().Contain("Phone");
    }

    [Fact]
    public void Build_WhenNullFlipsToEmpty_CountsAsChange()
    {
        var oldValues = new Dictionary<string, object?> { ["Description"] = null };
        var newValues = new Dictionary<string, object?> { ["Description"] = string.Empty };

        var (oldJson, newJson) = AuditDiffBuilder.Build(oldValues, newValues);

        oldJson.Should().NotBeNull();
        newJson.Should().NotBeNull();
    }

    [Fact]
    public void Build_WhenCreatedWithNullOldValues_TreatsAllNewKeysAsCreated()
    {
        var newValues = new Dictionary<string, object?>
        {
            ["Email"] = "u@x.com",
            ["FirstName"] = "Ana"
        };

        var (oldJson, newJson) = AuditDiffBuilder.Build(null, newValues);

        // Created rows still carry the new keys with null values on the old side so the
        // handler can build a Changes[] entry showing the field appeared.
        oldJson.Should().NotBeNull();
        newJson.Should().NotBeNull();
        oldJson.Should().Contain("Email");
        oldJson.Should().Contain("FirstName");
        newJson.Should().Contain("Email");
        newJson.Should().Contain("FirstName");
    }
}
