using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>Current tenant and user of a lead command.</summary>
public readonly record struct LeadActor(Guid CompanyId, Guid UserId);

/// <summary>A tracked lead the current user can see, with the resolved access.</summary>
public sealed record LeadLoad(Lead Lead, LeadAccess Access);

/// <summary>
/// Shared steps of the lead commands (SPEC 61): tenant and user from the token, loading a lead the user can see
/// (else <c>LEAD_NOT_FOUND</c>), the state checks, the history rows and the mapping.
/// </summary>
public sealed class LeadCommandContext(IUnitOfWork unitOfWork, ICurrentUserService currentUserService, LeadAccessGuard guard)
{
    public Response<T>? Actor<T>(out LeadActor actor) => ActorOf<T>(currentUserService, out actor);

    /// <summary>Tenant and user of the token; <c>COMPANY_REQUIRED</c> / <c>USER_REQUIRED</c> otherwise.</summary>
    public static Response<T>? ActorOf<T>(ICurrentUserService currentUserService, out LeadActor actor)
    {
        actor = default;
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<T>.Error(LeadErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId))
        {
            return Response<T>.Error(LeadErrorCodes.UserRequired, ["The authenticated token must contain a valid user id."]);
        }

        actor = new LeadActor(currentUserService.CompanyId, userId);
        return null;
    }

    public Task<LeadAccess> AccessAsync(LeadActor actor, CancellationToken cancellationToken) =>
        guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);

    /// <summary>The tracked lead when it exists in the company and the user can see it.</summary>
    public async Task<(LeadLoad? Load, Response<T>? Error)> LoadAsync<T>(LeadActor actor, Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await unitOfWork.GetRepository<Lead>().GetAsync(leadId);
        if (lead is null || lead.CompanyId != actor.CompanyId || lead.GcRecord != 0)
        {
            return (null, NotFound<T>());
        }

        var access = await AccessAsync(actor, cancellationToken);
        return access.CanSee(lead) ? (new LeadLoad(lead, access), null) : (null, NotFound<T>());
    }

    public static Response<T> NotFound<T>() => Response<T>.Error(LeadErrorCodes.LeadNotFound, ["Lead not found."]);

    public static Response<T>? RequireOpen<T>(Lead lead) =>
        lead.Status == LeadStatus.Open ? null : Response<T>.Error(LeadErrorCodes.LeadNotOpen, ["The action needs an open lead."]);

    public static Response<T> Forbidden<T>(string message) => Response<T>.Error(LeadErrorCodes.ActionForbidden, [message]);

    public Task AddStageHistoryAsync(Lead lead, Guid? fromStageId, Guid userId, DateTime at) =>
        unitOfWork.GetRepository<LeadStageHistory>().InsertAsync(new LeadStageHistory
        {
            CompanyId = lead.CompanyId, LeadId = lead.Id, FromStageId = fromStageId, ToStageId = lead.LeadStageId, UserId = userId, ChangedAt = at
        });

    public Task AddOwnershipHistoryAsync(Lead lead, Guid? fromUserId, Guid? fromTeamId, LeadOwnershipCause cause, Guid userId, DateTime at, string? notes) =>
        unitOfWork.GetRepository<LeadOwnershipHistory>().InsertAsync(new LeadOwnershipHistory
        {
            CompanyId = lead.CompanyId, LeadId = lead.Id, FromUserId = fromUserId, ToUserId = lead.OwnerUserId, FromTeamId = fromTeamId,
            ToTeamId = lead.SalesTeamId, Cause = cause, UserId = userId, At = at, Notes = Clean(notes)
        });

    public Task AddStatusHistoryAsync(Lead lead, LeadStatus? fromStatus, Guid userId, DateTime at, string? notes) =>
        unitOfWork.GetRepository<LeadStatusHistory>().InsertAsync(new LeadStatusHistory
        {
            CompanyId = lead.CompanyId, LeadId = lead.Id, FromStatus = fromStatus, ToStatus = lead.Status, LeadReasonId = lead.LeadReasonId,
            UserId = userId, At = at, Notes = Clean(notes)
        });

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static LeadDto ToDto(Lead lead) => new()
    {
        Id = lead.Id,
        CompanyId = lead.CompanyId,
        Code = lead.Code,
        PersonId = lead.PersonId,
        Topic = lead.Topic,
        Status = (int)lead.Status,
        LeadStageId = lead.LeadStageId,
        OwnerUserId = lead.OwnerUserId,
        SalesTeamId = lead.SalesTeamId,
        LeadSourceId = lead.LeadSourceId,
        RelationshipTypeId = lead.RelationshipTypeId,
        ReferredByPersonId = lead.ReferredByPersonId,
        LeadReasonId = lead.LeadReasonId,
        StatusNotes = lead.StatusNotes,
        CurrencyId = lead.CurrencyId,
        ExchangeRate = lead.ExchangeRate,
        ExchangeRateDate = lead.ExchangeRateDate,
        EstimatedValue = lead.EstimatedValue,
        EstimatedValueBase = lead.EstimatedValueBase,
        EstimatedCloseDate = lead.EstimatedCloseDate,
        ProjectId = lead.ProjectId,
        AreaId = lead.AreaId,
        LastInteractionAt = lead.LastInteractionAt,
        InactivatedAt = lead.InactivatedAt,
        ClosedAt = lead.ClosedAt,
        CustomerId = lead.CustomerId,
        WonValue = lead.WonValue,
        WonExchangeRate = lead.WonExchangeRate,
        WonValueBase = lead.WonValueBase,
        CompetitorName = lead.CompetitorName,
        CreatedAt = lead.Created
    };
}
