using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Industries.Queries;

/// <summary>
/// Query used to retrieve a single tenant-scoped industry by its identifier.
/// </summary>
/// <param name="Id">The unique identifier of the requested industry.</param>
public sealed record GetIndustryByIdQuery(Guid Id)
    : IRequest<Response<IndustryDto>>;
