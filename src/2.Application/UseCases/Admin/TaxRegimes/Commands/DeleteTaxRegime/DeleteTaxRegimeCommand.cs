using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

public sealed record DeleteTaxRegimeCommand(Guid Id) : IRequest<Response<Guid>>;
