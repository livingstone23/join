using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Queries;

/// <summary>
/// Query that retrieves a person employment record by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the person employment record.</param>
public sealed record GetPersonEmploymentByIdQuery(Guid Id) : IRequest<Response<PersonEmploymentResponseDto>>;
