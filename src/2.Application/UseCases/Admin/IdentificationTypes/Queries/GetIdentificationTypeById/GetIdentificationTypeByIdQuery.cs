using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Queries;

/// <summary>
/// Query used to retrieve a single identification type by its identifier.
/// </summary>
/// <param name="Id">The unique identifier of the requested identification type.</param>
public sealed record GetIdentificationTypeByIdQuery(Guid Id)
    : IRequest<Response<IdentificationTypeDto>>;