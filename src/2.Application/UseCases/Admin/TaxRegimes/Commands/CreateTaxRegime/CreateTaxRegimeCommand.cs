using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

public sealed record CreateTaxRegimeCommand : ITransactionalCommand<Response<TaxRegimeDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
