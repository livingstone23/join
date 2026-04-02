using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Queries;



/// <summary>
/// Query to retrieve a paginated list of customers for the current tenant.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
public record GetCustomersPagedQuery(int PageNumber = 1, int PageSize = 10)
    : IRequest<Response<PagedResult<CustomerListItemDto>>>;