using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.ContactTypeCompanies.Queries;

/// <summary>
/// <c>GET /ContactTypeCompanies</c> (SPEC 59): the company's contact types with the master data. Rows whose master was
/// deleted are left out.
/// </summary>
public sealed record GetContactTypeCompaniesQuery(bool? IncludeInactive = null, Guid? CompanyId = null)
    : IRequest<Response<IReadOnlyList<ContactTypeCompanyDto>>>;
