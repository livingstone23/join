using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Queries;



/// <summary>
/// Query to retrieve a paginated list of customers for the current tenant.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="PersonType">Optional customer person type filter.</param>
/// <param name="FirstName">Optional first name partial-match filter.</param>
/// <param name="MiddleName">Optional middle name partial-match filter.</param>
/// <param name="LastName">Optional last name partial-match filter.</param>
/// <param name="SecondLastName">Optional second last name partial-match filter.</param>
/// <param name="CommercialName">Optional commercial name partial-match filter.</param>
/// <param name="IdentificationTypeId">Optional identification type exact-match filter.</param>
/// <param name="IdentificationNumber">Optional identification number partial-match filter.</param>
public record GetCustomersPagedQuery(
    int PageNumber = 1,
    int PageSize = 10,
    string? PersonType = null,
    string? FirstName = null,
    string? MiddleName = null,
    string? LastName = null,
    string? SecondLastName = null,
    string? CommercialName = null,
    Guid? IdentificationTypeId = null,
    string? IdentificationNumber = null)
    : IRequest<Response<PagedResult<CustomerListItemDto>>>;