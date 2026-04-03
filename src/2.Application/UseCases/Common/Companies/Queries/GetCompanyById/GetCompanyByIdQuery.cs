using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Queries;

/// <summary>
/// Query to retrieve a company by id.
/// </summary>
/// <param name="CompanyId">The company identifier.</param>
public record GetCompanyByIdQuery(Guid CompanyId) : IRequest<Response<CompanyDto>>;
