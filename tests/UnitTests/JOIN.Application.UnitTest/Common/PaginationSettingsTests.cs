// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Application.Common;

namespace JOIN.Application.UnitTest.Common;

/// <summary>
/// Unit tests for <see cref="PaginationSettings.Sanitize"/> — the shared clamping logic
/// introduced by specs/33-centralized-pagination-settings.md to replace the per-handler
/// clamping that used to be duplicated across every paged query handler.
/// </summary>
public sealed class PaginationSettingsTests
{
    private static PaginationSettings Default() => new()
    {
        DefaultPageNumber = 1,
        DefaultPageSize = 10,
        MaxPageSize = 50,
        MinPageSize = 1
    };

    [Fact]
    public void Sanitize_WhenBothValuesAreNull_ShouldUseDefaults()
    {
        var settings = Default();

        var (pageNumber, pageSize) = settings.Sanitize(null, null);

        pageNumber.Should().Be(1);
        pageSize.Should().Be(10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Sanitize_WhenPageNumberIsZeroOrNegative_ShouldFallBackToDefaultPageNumber(int invalidPageNumber)
    {
        var settings = Default();

        var (pageNumber, _) = settings.Sanitize(invalidPageNumber, 10);

        pageNumber.Should().Be(settings.DefaultPageNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Sanitize_WhenPageSizeIsBelowMinimum_ShouldFallBackToDefaultPageSize(int invalidPageSize)
    {
        var settings = Default();

        var (_, pageSize) = settings.Sanitize(1, invalidPageSize);

        pageSize.Should().Be(settings.DefaultPageSize);
    }

    [Fact]
    public void Sanitize_WhenPageSizeExceedsMax_ShouldCapAtMaxPageSize()
    {
        var settings = Default();

        var (_, pageSize) = settings.Sanitize(1, 1000);

        pageSize.Should().Be(settings.MaxPageSize);
    }

    [Fact]
    public void Sanitize_WhenValuesAreWithinRange_ShouldReturnThemUnchanged()
    {
        var settings = Default();

        var (pageNumber, pageSize) = settings.Sanitize(3, 25);

        pageNumber.Should().Be(3);
        pageSize.Should().Be(25);
    }

    [Fact]
    public void Sanitize_WhenPageSizeEqualsMaxPageSize_ShouldReturnItUnchanged()
    {
        var settings = Default();

        var (_, pageSize) = settings.Sanitize(1, settings.MaxPageSize);

        pageSize.Should().Be(settings.MaxPageSize);
    }

    [Fact]
    public void Sanitize_WhenMaxPageSizeIsBelowMinPageSize_ShouldSelfCorrectToMinPageSize()
    {
        var settings = new PaginationSettings
        {
            DefaultPageNumber = 1,
            DefaultPageSize = 10,
            MaxPageSize = 5,
            MinPageSize = 20
        };

        var (_, pageSize) = settings.Sanitize(1, 1000);

        pageSize.Should().Be(20);
    }

    [Fact]
    public void Sanitize_WhenDefaultPageSizeIsBelowMinPageSize_ShouldSelfCorrectDefaultToMinPageSize()
    {
        var settings = new PaginationSettings
        {
            DefaultPageNumber = 1,
            DefaultPageSize = 1,
            MaxPageSize = 50,
            MinPageSize = 5
        };

        var (_, pageSize) = settings.Sanitize(1, 0);

        pageSize.Should().Be(5);
    }

    [Fact]
    public void Sanitize_WhenDefaultPageSizeExceedsMaxPageSize_ShouldSelfCorrectDefaultDownToMax()
    {
        var settings = new PaginationSettings
        {
            DefaultPageNumber = 1,
            DefaultPageSize = 200,
            MaxPageSize = 50,
            MinPageSize = 1
        };

        var (_, pageSize) = settings.Sanitize(1, 0);

        pageSize.Should().Be(50);
    }

    [Fact]
    public void Sanitize_WhenMinPageSizeIsZeroOrNegative_ShouldTreatItAsOne()
    {
        var settings = new PaginationSettings
        {
            DefaultPageNumber = 1,
            DefaultPageSize = 10,
            MaxPageSize = 50,
            MinPageSize = 0
        };

        var (_, pageSize) = settings.Sanitize(1, -5);

        pageSize.Should().Be(10);
    }

    [Fact]
    public void Sanitize_WhenDefaultPageNumberIsZeroOrNegative_ShouldTreatItAsOne()
    {
        var settings = new PaginationSettings
        {
            DefaultPageNumber = 0,
            DefaultPageSize = 10,
            MaxPageSize = 50,
            MinPageSize = 1
        };

        var (pageNumber, _) = settings.Sanitize(null, null);

        pageNumber.Should().Be(1);
    }
}
