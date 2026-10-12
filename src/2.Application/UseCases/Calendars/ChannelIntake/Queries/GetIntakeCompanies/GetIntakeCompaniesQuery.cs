using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakeCompanies;

/// <summary>
/// Lists the companies the agent can operate in: an active <c>UserCompany</c> there, the Calendar module enabled
/// and a <c>CalendarCompany</c> configured (SPEC 45). The optional <c>Search</c> compares (contains,
/// case-insensitive) against the company's name and tax id. It does not depend on the company of the token — the
/// agent is still choosing it, and then switches to it with <c>POST /workspaces/switch-company</c> — so the endpoint
/// skips the dynamic authorization filter and the handler enforces the <c>Agent</c> / <c>SuperAdmin</c> role.
/// </summary>
public sealed record GetIntakeCompaniesQuery(string? Search)
    : IRequest<Response<IReadOnlyList<IntakeCompanyDto>>>;