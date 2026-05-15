using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Genders.Queries;

/// <summary>
/// Query used to retrieve a single tenant-scoped gender by its identifier.
/// </summary>
/// <param name="Id">The unique identifier of the requested gender.</param>
public sealed record GetGenderByIdQuery(Guid Id)
    : IRequest<Response<GenderDto>>;
