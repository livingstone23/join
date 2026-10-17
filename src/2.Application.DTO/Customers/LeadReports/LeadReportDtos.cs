namespace JOIN.Application.DTO.Customers.LeadReports;

/// <summary>Commercial summary of the period (SPEC 64 item 1), amounts in base currency.</summary>
public sealed record LeadReportSummaryDto
{
    public int OpenCount { get; init; }
    public int CreatedCount { get; init; }
    public int WonCount { get; init; }
    public int ConversionPercent { get; init; }
    public decimal PipelineValueBase { get; init; }
    public decimal WeightedPipelineValueBase { get; init; }
    public decimal WonValueBase { get; init; }
    public decimal? AvgDaysToClose { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
}

/// <summary>An open stage of the funnel (SPEC 64 item 2): current snapshot.</summary>
public sealed record LeadReportFunnelStageDto
{
    public Guid LeadStageId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public int Probability { get; init; }
    public int Count { get; init; }
    public decimal ValueBase { get; init; }
    public decimal WeightedValueBase { get; init; }
}

/// <summary>Figures of a team in the period (SPEC 64 item 3); <c>SalesTeamId</c> null = "Sin grupo".</summary>
public sealed record LeadReportTeamDto
{
    public Guid? SalesTeamId { get; init; }
    public string SalesTeamName { get; init; } = string.Empty;
    public int CreatedCount { get; init; }
    public int WonCount { get; init; }
    public int ConversionPercent { get; init; }
    public decimal WonValueBase { get; init; }
    public decimal? AvgDaysToClose { get; init; }
}

/// <summary>Figures of a user in the period (SPEC 64 item 4).</summary>
public sealed record LeadReportRankingDto
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    /// <summary>Teams of the leads the user won in the period.</summary>
    public string? WonTeams { get; init; }
    /// <summary>Management role of the user's current (primary first) membership.</summary>
    public string? CurrentRole { get; init; }
    public int CreatedCount { get; init; }
    public int WonCount { get; init; }
    public int ConversionPercent { get; init; }
    public decimal WonValueBase { get; init; }
    public decimal? AvgDaysToClose { get; init; }
    public string? TopContactType { get; init; }
    public string? TopProduct { get; init; }
}

/// <summary>Accepted lines of the won leads by product (SPEC 64 item 5); value converted with <c>WonExchangeRate</c>.</summary>
public sealed record LeadReportProductDto
{
    public Guid ProductId { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int LeadsCount { get; init; }
    public decimal Quantity { get; init; }
    public decimal ValueBase { get; init; }
}

/// <summary>Created and won leads by source (SPEC 64 item 6).</summary>
public sealed record LeadReportSourceDto
{
    public Guid LeadSourceId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public int CreatedCount { get; init; }
    public int WonCount { get; init; }
}

/// <summary>Contacts of the won leads of the period by contact type (SPEC 64 item 7).</summary>
public sealed record LeadReportContactTypesDto
{
    public int WonLeadsCount { get; init; }
    public int InteractionsCount { get; init; }
    public decimal AveragePerWonLead { get; init; }
    public IReadOnlyList<LeadReportContactTypeDto> Items { get; init; } = [];
}

/// <summary>One contact type with its count.</summary>
public sealed record LeadReportContactTypeDto
{
    public string ContactTypeCode { get; init; } = string.Empty;
    public string ContactTypeName { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>A won lead of the period (SPEC 64 item 8), the base for exporting.</summary>
public sealed record LeadReportWonLeadDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string PersonName { get; init; } = string.Empty;
    public string? CustomerCode { get; init; }
    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public string SalesTeamName { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public DateTime ClosedAt { get; init; }
    public decimal WonValueBase { get; init; }
    public string? AcceptedProducts { get; init; }
}
