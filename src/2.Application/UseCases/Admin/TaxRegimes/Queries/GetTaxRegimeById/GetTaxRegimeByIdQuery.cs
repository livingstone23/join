using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Queries;

public sealed record GetTaxRegimeByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<TaxRegimeDto>>;
