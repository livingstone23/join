using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

public sealed record CreateIncomeRangeCommand : IRequest<Response<IncomeRangeDto>>
{
    public string DisplayName { get; init; } = string.Empty;
    public decimal MinimumValue { get; init; }
    public decimal? MaximumValue { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
}
