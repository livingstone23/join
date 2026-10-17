using JOIN.Application.DTO.Common;

namespace JOIN.Application.DTO.Customers.LeadSettings;

/// <summary>Fields shared by every commercial catalog row (SPEC 59).</summary>
public abstract record CommercialCatalogItemDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>A lead stage. <c>Kind</c>: 1 Initial, 2 Open, 3 Won, 4 Lost.</summary>
public sealed record LeadStageDto : CommercialCatalogItemDto
{
    public int Probability { get; init; }
    public int Kind { get; init; }
}

/// <summary>A lead source ("razón de registro").</summary>
public sealed record LeadSourceDto : CommercialCatalogItemDto
{
    public string? Description { get; init; }
}

/// <summary>A lead reason. <c>UsedFor</c>: 1 Won, 2 Lost, 3 Inactive.</summary>
public sealed record LeadReasonDto : CommercialCatalogItemDto
{
    public int UsedFor { get; init; }
}

/// <summary>An expected relationship type. <c>OnWin</c>: 0 None, 1 Customer.</summary>
public sealed record RelationshipTypeDto : CommercialCatalogItemDto
{
    public int OnWin { get; init; }
}

/// <summary>A global master contact type (SPEC 59).</summary>
public sealed record ContactTypeDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string? CommunicationChannelCode { get; init; }
    public bool SchedulesCalendarEvent { get; init; }
    public bool AsksDuration { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>A company's copy of a master contact type, with the master's fixed data (SPEC 59).</summary>
public sealed record ContactTypeCompanyDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid ContactTypeId { get; init; }
    public string Code { get; init; } = string.Empty;
    /// <summary>The name the company uses.</summary>
    public string Name { get; init; } = string.Empty;
    public string MasterName { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string? CommunicationChannelCode { get; init; }
    public bool SchedulesCalendarEvent { get; init; }
    public bool AsksDuration { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Calendar activity used to schedule a next action with this type (SPEC 65); null = cannot schedule.</summary>
    public Guid? CalendarActivityCompanyId { get; init; }
}

/// <summary>
/// Lead module rules of the company (SPEC 59). <see cref="IsDefault"/> is <c>true</c> when the company has no row yet
/// and the values are the defaults. <c>DuplicateOpenLeadPolicy</c>: 1 Warn, 2 Block, 3 Allow.
/// </summary>
public sealed record LeadCompanySettingsDto
{
    public Guid? Id { get; init; }
    public Guid CompanyId { get; init; }
    public int DuplicateOpenLeadPolicy { get; init; }
    public bool RequireIdentificationForCustomer { get; init; }
    public string CodePrefix { get; init; } = string.Empty;
    public int CodeSequenceLength { get; init; }
    public bool IsDefault { get; init; }
}
