using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Queries;

/// <summary>
/// Query to retrieve a paginated list of companies with optional search.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested page size.</param>
/// <param name="SearchTerm">Optional search term for company name or tax id.</param>
public record GetCompaniesPagedQuery(int PageNumber = 1, int PageSize = 10, string? SearchTerm = null)
    : IRequest<Response<PagedResult<CompanyListItemDto>>>;
