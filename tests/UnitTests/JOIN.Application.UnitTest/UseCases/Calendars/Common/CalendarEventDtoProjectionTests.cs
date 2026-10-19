using FluentAssertions;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 67 — verifies that <see cref="CalendarEventQueryService.ToDto"/> projects the new
/// <c>RequesterPersonName</c>, <c>CustomerCode</c> and <c>IsCustomerActive</c> fields straight from the
/// <see cref="CalendarEventDetailRow"/> left-joined by <c>CalendarQueryReader.ByIdSql</c>. The shape is the
/// contract the frontend consumes (FE-42).
/// </summary>
public sealed class CalendarEventDtoProjectionTests
{
    private const string TimeZone = "America/Managua";

    /// <summary>
    /// When the row carries both the person name and the customer code (the unified "Persona/Cliente" lookup
    /// already linked them, FE-42), the DTO exposes the three fields populated.
    /// </summary>
    [Fact]
    public void ToDto_WhenRowHasPersonAndCustomer_ProjectsAllThreeFields()
    {
        var personId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var row = new CalendarEventDetailRow
        {
            Id = Guid.NewGuid(),
            CalendarId = Guid.NewGuid(),
            CalendarName = "Mi calendario",
            CalendarColor = "#1E88E5",
            OwnerUserId = Guid.NewGuid(),
            OwnerName = "Ada Lovelace",
            ActivityCompanyId = Guid.NewGuid(),
            ActivityCode = "MEETING",
            ActivityName = "Reunión",
            ActivityColor = "#1E88E5",
            AllowsOverlap = false,
            StatusCompanyId = Guid.NewGuid(),
            StatusCode = "PENDING",
            StatusName = "Pendiente",
            StatusColor = "#FB8C00",
            BlocksTime = true,
            IsFinal = false,
            ChannelCode = "WEB",
            OriginType = (int)CalendarEventOriginType.User,
            Title = "Sync",
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddHours(1),
            RequesterPersonId = personId,
            RequesterPersonName = "Ada Lovelace",
            CustomerId = customerId,
            CustomerCode = "C-001",
            IsCustomerActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var dto = CalendarEventQueryService.ToDto(row, TimeZone);

        dto.RequesterPersonId.Should().Be(personId);
        dto.RequesterPersonName.Should().Be("Ada Lovelace");
        dto.CustomerId.Should().Be(customerId);
        dto.CustomerCode.Should().Be("C-001");
        dto.IsCustomerActive.Should().BeTrue();
    }

    /// <summary>
    /// When the activity has no person or customer (both columns null), the three fields stay null so the
    /// frontend treats the link as empty without calling extra endpoints.
    /// </summary>
    [Fact]
    public void ToDto_WhenRowHasNoLinks_ProjectsNulls()
    {
        var row = new CalendarEventDetailRow
        {
            Id = Guid.NewGuid(),
            CalendarId = Guid.NewGuid(),
            CalendarName = "Mi calendario",
            CalendarColor = "#1E88E5",
            OwnerUserId = Guid.NewGuid(),
            OwnerName = "Ada Lovelace",
            ActivityCompanyId = Guid.NewGuid(),
            ActivityCode = "MEETING",
            ActivityName = "Reunión",
            ActivityColor = "#1E88E5",
            AllowsOverlap = false,
            StatusCompanyId = Guid.NewGuid(),
            StatusCode = "PENDING",
            StatusName = "Pendiente",
            StatusColor = "#FB8C00",
            BlocksTime = true,
            IsFinal = false,
            ChannelCode = "WEB",
            OriginType = (int)CalendarEventOriginType.User,
            Title = "Focus",
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };

        var dto = CalendarEventQueryService.ToDto(row, TimeZone);

        dto.RequesterPersonId.Should().BeNull();
        dto.RequesterPersonName.Should().BeNull();
        dto.CustomerId.Should().BeNull();
        dto.CustomerCode.Should().BeNull();
        dto.IsCustomerActive.Should().BeNull();
    }

    /// <summary>
    /// Inactive customer keeps the code visible and flags <c>IsCustomerActive = false</c>; FE-42 distinguishes
    /// "(cli)" from "(cli inactivo)" without an extra call.
    /// </summary>
    [Fact]
    public void ToDto_WhenCustomerIsInactive_ProjectsCodeAndFalse()
    {
        var row = new CalendarEventDetailRow
        {
            Id = Guid.NewGuid(),
            CalendarId = Guid.NewGuid(),
            CalendarName = "Mi calendario",
            CalendarColor = "#1E88E5",
            OwnerUserId = Guid.NewGuid(),
            OwnerName = "Ada Lovelace",
            ActivityCompanyId = Guid.NewGuid(),
            ActivityCode = "MEETING",
            ActivityName = "Reunión",
            ActivityColor = "#1E88E5",
            AllowsOverlap = false,
            StatusCompanyId = Guid.NewGuid(),
            StatusCode = "PENDING",
            StatusName = "Pendiente",
            StatusColor = "#FB8C00",
            BlocksTime = true,
            IsFinal = false,
            ChannelCode = "WEB",
            OriginType = (int)CalendarEventOriginType.User,
            Title = "Sync",
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddHours(1),
            RequesterPersonId = Guid.NewGuid(),
            RequesterPersonName = "Ada Lovelace",
            CustomerId = Guid.NewGuid(),
            CustomerCode = "C-OLD",
            IsCustomerActive = false,
            CreatedAt = DateTime.UtcNow
        };

        var dto = CalendarEventQueryService.ToDto(row, TimeZone);

        dto.CustomerCode.Should().Be("C-OLD");
        dto.IsCustomerActive.Should().BeFalse();
    }
}