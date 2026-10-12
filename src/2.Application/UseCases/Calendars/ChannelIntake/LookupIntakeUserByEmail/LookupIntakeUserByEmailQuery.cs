using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.LookupIntakeUserByEmail;

/// <summary>
/// Exact (case-insensitive) match on the e-mail of a user that belongs to the current company with an active
/// <c>CalendarUserConfiguration</c> (SPEC 45). A miss is NOT an error — the agent falls through to the default
/// user of the company; we still return the same shape so the caller can react.
/// </summary>
public sealed record LookupIntakeUserByEmailQuery(string Email)
    : IRequest<Response<IntakeUserLookupDto>>;