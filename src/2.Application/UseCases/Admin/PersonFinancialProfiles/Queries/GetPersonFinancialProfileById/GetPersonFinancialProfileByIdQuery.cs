using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Queries;

/// <summary>
/// Query that retrieves a person financial profile by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the person financial profile record.</param>
public sealed record GetPersonFinancialProfileByIdQuery(Guid Id) : IRequest<Response<PersonFinancialProfileResponseDto>>;
