using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakeCompanies;

/// <summary>
/// Lists the companies the agent can operate in: an active <c>UserCompany</c> there, the Calendar module enabled
/// and a <c>CalendarCompany</c> configured (SPEC 45). The optional <c>Search</c> compares (contains,
/// case-insensitive) against the company's name and tax id. Bypasses the tenant query filter — runs before
/// <c>X-Company-Id</c> is set — so it must enforce the role check itself.
/// </summary>
public sealed record GetIntakeCompaniesQuery(string? Search)
    : IRequest<Response<IReadOnlyList<IntakeCompanyDto>>>;