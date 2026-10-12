using System;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmations;

/// <summary>
/// Paged inbox of pending confirmations. The default status filter is <c>Pending = 1</c>, so the call
/// without a <c>Status</c> value is the "open tray" used by the supervisor screen (SPEC 45 F5).
/// </summary>
public sealed record GetPersonPendingConfirmationsQuery(
    Guid CompanyId,
    int? Status = null,
    Guid? ChannelId = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Response<PagedResult<PersonPendingConfirmationDto>>>;
