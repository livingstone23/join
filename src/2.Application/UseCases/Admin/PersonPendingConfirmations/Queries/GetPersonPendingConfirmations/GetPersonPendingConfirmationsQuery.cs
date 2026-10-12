using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmations;

/// <summary>
/// <c>GET /PersonPendingConfirmations</c> (SPEC 45): paged inbox, most recent first. <see cref="Status"/> defaults to
/// <c>Pending</c>; <see cref="Search"/> matches the person's name, identification or active contacts.
/// </summary>
public sealed record GetPersonPendingConfirmationsQuery(
    int? Status = null,
    Guid? ChannelId = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Response<PagedResult<PersonPendingConfirmationDto>>>;
