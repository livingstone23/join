using JOIN.Application.DTO.Customers.LeadSettings;

namespace JOIN.Application.DTO.Customers.Leads;

/// <summary>
/// A lead as stored (command results, SPEC 61). <c>Status</c>: 1 Open, 2 Inactive, 3 Won, 4 Lost.
/// </summary>
public sealed record LeadDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public Guid PersonId { get; init; }
    public string Topic { get; init; } = string.Empty;
    public int Status { get; init; }
    public Guid LeadStageId { get; init; }
    public Guid OwnerUserId { get; init; }
    public Guid? SalesTeamId { get; init; }
    public Guid LeadSourceId { get; init; }
    public Guid RelationshipTypeId { get; init; }
    public Guid? ReferredByPersonId { get; init; }
    public Guid? LeadReasonId { get; init; }
    public string? StatusNotes { get; init; }
    public Guid CurrencyId { get; init; }
    public decimal ExchangeRate { get; init; }
    public DateOnly ExchangeRateDate { get; init; }
    public decimal EstimatedValue { get; init; }
    public decimal EstimatedValueBase { get; init; }
    public DateOnly? EstimatedCloseDate { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? AreaId { get; init; }
    public DateTime? LastInteractionAt { get; init; }
    public DateTime? InactivatedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public Guid? CustomerId { get; init; }
    /// <summary>Accepted lines at win, in the lead currency (SPEC 63).</summary>
    public decimal? WonValue { get; init; }
    public decimal? WonExchangeRate { get; init; }
    public decimal? WonValueBase { get; init; }
    public string? CompetitorName { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Result of <c>POST /Leads/{id}/win</c> (SPEC 63): the closed lead and, when its relationship type creates customers,
/// the customer and what happened to it (<c>Created</c>, <c>Activated</c> or <c>Confirmed</c>).
/// </summary>
public sealed record LeadWinResultDto
{
    public LeadDto Lead { get; init; } = new();
    public Guid? CustomerId { get; init; }
    public string? CustomerCode { get; init; }
    public string? CustomerAction { get; init; }
}

/// <summary>Result of <c>POST /Leads</c>: the lead and the open leads the person already had (policy <c>Warn</c>).</summary>
public sealed record LeadCreatedDto
{
    public LeadDto Lead { get; init; } = new();
    public IReadOnlyList<LeadDuplicateDto> DuplicateOpenLeads { get; init; } = [];
}

/// <summary>An open lead of a person (duplicates warning).</summary>
public sealed record LeadDuplicateDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>A lead in the inbox and the board (SPEC 61).</summary>
public record LeadListItemDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public Guid PersonId { get; init; }
    public string PersonName { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public int Status { get; init; }
    public Guid LeadStageId { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public int StageProbability { get; init; }
    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public Guid? SalesTeamId { get; init; }
    public string? SalesTeamName { get; init; }
    public Guid LeadSourceId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public Guid RelationshipTypeId { get; init; }
    public string RelationshipTypeName { get; init; } = string.Empty;
    public Guid CurrencyId { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public decimal EstimatedValue { get; init; }
    public decimal EstimatedValueBase { get; init; }
    public DateOnly? EstimatedCloseDate { get; init; }
    public DateTime? LastInteractionAt { get; init; }
    public string? LastContactTypeName { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>The lead detail (<c>GET /Leads/{id}</c>) with person, primary contacts, totals and product lines.</summary>
public sealed record LeadDetailDto : LeadListItemDto
{
    public int PersonType { get; init; }
    public bool PersonIsActive { get; init; }
    public string? PersonEmail { get; init; }
    public string? PersonPhone { get; init; }
    public Guid? ReferredByPersonId { get; init; }
    public string? ReferredByPersonName { get; init; }
    public Guid? LeadReasonId { get; init; }
    public string? ReasonName { get; init; }
    public string? StatusNotes { get; init; }
    public decimal ExchangeRate { get; init; }
    public DateOnly ExchangeRateDate { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public DateTime? InactivatedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public Guid? CustomerId { get; init; }
    public string? CustomerCode { get; init; }
    public decimal? WonValue { get; init; }
    public decimal? WonExchangeRate { get; init; }
    public decimal? WonValueBase { get; init; }
    public string? CompetitorName { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? AreaId { get; init; }
    public int InteractionsCount { get; init; }
    public int CommentsCount { get; init; }
    public IReadOnlyList<LeadProductDto> Products { get; init; } = [];
}

/// <summary>An offered product line. <c>Status</c>: 1 Offered, 2 Accepted, 3 Rejected.</summary>
public sealed record LeadProductDto
{
    public Guid Id { get; init; }
    public Guid LeadId { get; init; }
    public Guid ProductId { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal LineTotal { get; init; }
    public int Status { get; init; }
    public Guid? OfferedInInteractionId { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>A contact. <c>Direction</c>: 1 Outbound, 2 Inbound. <c>Outcome</c>: 1 Interested, 2 ProposalRequested, 3 NoAnswer, 4 NotInterested, 99 Other.</summary>
public sealed record LeadInteractionDto
{
    public Guid Id { get; init; }
    public Guid LeadId { get; init; }
    public Guid ContactTypeCompanyId { get; init; }
    public string ContactTypeCode { get; init; } = string.Empty;
    public string ContactTypeName { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public int Direction { get; init; }
    public DateTime OccurredAt { get; init; }
    public int? DurationMinutes { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public int Outcome { get; init; }
    public string Summary { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>A comment on a lead.</summary>
public sealed record LeadCommentDto
{
    public Guid Id { get; init; }
    public Guid LeadId { get; init; }
    public Guid? LeadInteractionId { get; init; }
    public string Body { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// One entry of the lead timeline. <c>Kind</c>: <c>Interaction</c>, <c>Comment</c>, <c>Document</c> (SPEC 62), <c>Stage</c>, <c>Ownership</c> or
/// <c>Status</c>.
/// </summary>
public sealed record LeadTimelineItemDto
{
    public string Kind { get; init; } = string.Empty;
    public Guid Id { get; init; }
    public DateTime At { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public Guid? RefId { get; init; }
}

/// <summary>An open stage of the board with its count, base-currency value and first cards.</summary>
public sealed record LeadBoardColumnDto
{
    public Guid LeadStageId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Probability { get; init; }
    public int SortOrder { get; init; }
    public int Count { get; init; }
    public decimal ValueBase { get; init; }
    public IReadOnlyList<LeadListItemDto> Cards { get; init; } = [];
}

/// <summary>An active currency of the company for the lead form.</summary>
public sealed record LeadCurrencyOptionDto
{
    public Guid CurrencyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public bool IsBase { get; init; }
}

/// <summary>Active catalogs of the company for whoever works leads (<c>GET /Leads/catalogs</c>).</summary>
public sealed record LeadCatalogsDto
{
    public IReadOnlyList<LeadStageDto> Stages { get; init; } = [];
    public IReadOnlyList<LeadSourceDto> Sources { get; init; } = [];
    public IReadOnlyList<LeadReasonDto> Reasons { get; init; } = [];
    public IReadOnlyList<RelationshipTypeDto> RelationshipTypes { get; init; } = [];
    public IReadOnlyList<ContactTypeCompanyDto> ContactTypes { get; init; } = [];
    public IReadOnlyList<LeadCurrencyOptionDto> Currencies { get; init; } = [];
}

/// <summary>A document of a lead (SPEC 62). <c>DocumentType</c>: Pdf, Word, Excel, Text, Image, Other.</summary>
public sealed record LeadDocumentDto : JOIN.Application.DTO.Common.SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid LeadId { get; init; }
    public Guid? LeadInteractionId { get; init; }
    public string? ContactTypeName { get; init; }
    public DateTime? InteractionOccurredAt { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string OriginalName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public Guid UploadedByUserId { get; init; }
    public string? UploadedByUserName { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Attachment limits of the company for leads (SPEC 62). <c>AllowedDocumentTypes</c> is the <c>DocumentType</c> flags
/// value (Pdf 1, Word 2, Excel 4, Text 8, Image 16, Other 32); <c>AllowedDocumentTypeNames</c> lists them.
/// </summary>
public sealed record LeadAttachmentSettingsDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public int AllowedDocumentTypes { get; init; }
    public IReadOnlyList<string> AllowedDocumentTypeNames { get; init; } = [];
    public long MaxFileSizeBytes { get; init; }
    public int MaxFilesPerInteraction { get; init; }
    public int? MaxFilesPerDay { get; init; }
}
