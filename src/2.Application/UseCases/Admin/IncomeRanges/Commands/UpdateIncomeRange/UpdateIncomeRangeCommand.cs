using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

public sealed record UpdateIncomeRangeCommand : ITransactionalCommand<Response<IncomeRangeDto>>
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public decimal MinimumValue { get; init; }
    public decimal? MaximumValue { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public bool? IsActive { get; init; }
}
