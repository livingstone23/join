using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Leads.Commands;

/// <summary>
/// <c>PUT /Leads/{id}</c> (SPEC 61 C.24): editable fields of an open lead. The currency changes only while the lead
/// has no product lines (rule 8); <see cref="EstimatedValue"/> applies only without lines (rule 9).
/// </summary>
public sealed record UpdateLeadCommand : ITransactionalCommand<Response<LeadDto>>
{
    public Guid Id { get; init; }
    public string Topic { get; init; } = string.Empty;
    public Guid RelationshipTypeId { get; init; }
    public Guid LeadSourceId { get; init; }
    public Guid? ReferredByPersonId { get; init; }
    public DateOnly? EstimatedCloseDate { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? AreaId { get; init; }
    public Guid CurrencyId { get; init; }
    public decimal? EstimatedValue { get; init; }
}

/// <summary><c>PUT /Leads/{id}/stage</c> (SPEC 61 C.25): next active stage or an earlier one.</summary>
public sealed record ChangeLeadStageCommand(Guid Id, Guid LeadStageId) : ITransactionalCommand<Response<LeadDto>>;

/// <summary><c>POST /Leads/{id}/reassign</c> (SPEC 61 C.26): new owner, same team; needs <c>CanReassign</c>.</summary>
public sealed record ReassignLeadCommand : ITransactionalCommand<Response<LeadDto>>
{
    public Guid Id { get; init; }
    public Guid NewOwnerUserId { get; init; }
    public string? Notes { get; init; }
}

/// <summary><c>POST /Leads/{id}/inactivate</c> (SPEC 61 C.27): reason of use <c>Inactive</c> and scope over the lead.</summary>
public sealed record InactivateLeadCommand : ITransactionalCommand<Response<LeadDto>>
{
    public Guid Id { get; init; }
    public Guid LeadReasonId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// <c>POST /Leads/{id}/reactivate</c> (SPEC 61 C.28, rule 6): any user with <c>CanUpdate</c> on <c>Leads</c>; the
/// caller becomes the owner and the lead takes the caller's team (<see cref="SalesTeamId"/> or the primary).
/// </summary>
public sealed record ReactivateLeadCommand : ITransactionalCommand<Response<LeadDto>>
{
    public Guid Id { get; init; }
    public Guid? SalesTeamId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>Validates <see cref="UpdateLeadCommand"/>.</summary>
public sealed class UpdateLeadCommandValidator : AbstractValidator<UpdateLeadCommand>
{
    public UpdateLeadCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RelationshipTypeId).NotEmpty();
        RuleFor(x => x.LeadSourceId).NotEmpty();
        RuleFor(x => x.CurrencyId).NotEmpty();
        RuleFor(x => x.EstimatedValue).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true).When(x => x.EstimatedValue.HasValue);
    }
}

/// <summary>Validates <see cref="ChangeLeadStageCommand"/>.</summary>
public sealed class ChangeLeadStageCommandValidator : AbstractValidator<ChangeLeadStageCommand>
{
    public ChangeLeadStageCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.LeadStageId).NotEmpty();
    }
}

/// <summary>Validates <see cref="ReassignLeadCommand"/>.</summary>
public sealed class ReassignLeadCommandValidator : AbstractValidator<ReassignLeadCommand>
{
    public ReassignLeadCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NewOwnerUserId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Validates <see cref="InactivateLeadCommand"/>.</summary>
public sealed class InactivateLeadCommandValidator : AbstractValidator<InactivateLeadCommand>
{
    public InactivateLeadCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.LeadReasonId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Validates <see cref="ReactivateLeadCommand"/>.</summary>
public sealed class ReactivateLeadCommandValidator : AbstractValidator<ReactivateLeadCommand>
{
    public ReactivateLeadCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Handles <see cref="UpdateLeadCommand"/>.</summary>
public sealed class UpdateLeadCommandHandler(
    IUnitOfWork unitOfWork,
    LeadCommandContext context,
    LeadReferenceValidator references,
    LeadValueCalculator values)
    : IRequestHandler<UpdateLeadCommand, Response<LeadDto>>
{
    public async Task<Response<LeadDto>> Handle(UpdateLeadCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadDto>(actor, request.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (LeadCommandContext.RequireOpen<LeadDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        if (await references.SourceAsync<LeadDto>(actor.CompanyId, request.LeadSourceId) is { } sourceError && request.LeadSourceId != lead.LeadSourceId)
        {
            return sourceError;
        }

        if (await references.RelationshipTypeAsync<LeadDto>(actor.CompanyId, request.RelationshipTypeId) is { } typeError && request.RelationshipTypeId != lead.RelationshipTypeId)
        {
            return typeError;
        }

        if (request.ReferredByPersonId is { } referredId && await references.PersonAsync(actor.CompanyId, referredId, mustBeActive: false) is null)
        {
            return Response<LeadDto>.Error(LeadErrorCodes.InvalidPerson, ["The referring person does not exist in the company."]);
        }

        if (await references.ProjectAndAreaAsync<LeadDto>(actor.CompanyId, request.ProjectId, request.AreaId) is { } referenceError)
        {
            return referenceError;
        }

        if (request.CurrencyId != lead.CurrencyId)
        {
            if (await values.HasLinesAsync(lead.Id))
            {
                return Response<LeadDto>.Error(LeadErrorCodes.CurrencyLocked, ["The currency cannot change while the lead has products."]);
            }

            var active = (await unitOfWork.GetRepository<CompanyCurrency>().GetAllIncludingDeletedAsync(c =>
                c.CompanyId == actor.CompanyId && c.CurrencyId == request.CurrencyId && c.GcRecord == 0 && c.IsActive)).Any();
            if (!active)
            {
                return Response<LeadDto>.Error(LeadErrorCodes.InvalidCurrency, ["The currency is not an active currency of the company."]);
            }
        }

        var amountsChanged = request.CurrencyId != lead.CurrencyId || (request.EstimatedValue is { } v && v != lead.EstimatedValue);
        lead.Topic = request.Topic.Trim();
        lead.RelationshipTypeId = request.RelationshipTypeId;
        lead.LeadSourceId = request.LeadSourceId;
        lead.ReferredByPersonId = request.ReferredByPersonId;
        lead.EstimatedCloseDate = request.EstimatedCloseDate;
        lead.ProjectId = request.ProjectId;
        lead.AreaId = request.AreaId;
        lead.CurrencyId = request.CurrencyId;

        // The rate of the day is taken again only when the amounts change (decision: the funnel does not move with rates).
        if (amountsChanged && await values.RecalculateAsync<LeadDto>(lead, request.EstimatedValue, null, cancellationToken) is { } valueError)
        {
            return valueError;
        }

        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadDto> { IsSuccess = true, Message = "Lead updated successfully.", Data = LeadCommandContext.ToDto(lead) };
    }
}

/// <summary>Handles <see cref="ChangeLeadStageCommand"/>.</summary>
public sealed class ChangeLeadStageCommandHandler(
    IUnitOfWork unitOfWork,
    LeadCommandContext context,
    LeadReferenceValidator references,
    LeadStageAdvanceCoordinator stages)
    : IRequestHandler<ChangeLeadStageCommand, Response<LeadDto>>
{
    public async Task<Response<LeadDto>> Handle(ChangeLeadStageCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadDto>(actor, request.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (LeadCommandContext.RequireOpen<LeadDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        if (request.LeadStageId == lead.LeadStageId)
        {
            return new Response<LeadDto> { IsSuccess = true, Message = "The lead is already in that stage.", Data = LeadCommandContext.ToDto(lead) };
        }

        var all = await references.StagesAsync(actor.CompanyId);
        var current = all.First(s => s.Id == lead.LeadStageId);
        if (stages.Validate<LeadDto>(current, all.FirstOrDefault(s => s.Id == request.LeadStageId), all) is { } stageError)
        {
            return stageError;
        }

        var from = lead.LeadStageId;
        lead.LeadStageId = request.LeadStageId;
        await context.AddStageHistoryAsync(lead, from, actor.UserId, DateTime.UtcNow);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadDto> { IsSuccess = true, Message = "Lead stage changed successfully.", Data = LeadCommandContext.ToDto(lead) };
    }
}

/// <summary>Handles <see cref="ReassignLeadCommand"/>.</summary>
public sealed class ReassignLeadCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context, LeadOwnerRules owners)
    : IRequestHandler<ReassignLeadCommand, Response<LeadDto>>
{
    public async Task<Response<LeadDto>> Handle(ReassignLeadCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadDto>(actor, request.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (LeadCommandContext.RequireOpen<LeadDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        if (!load.Access.CanReassign(lead))
        {
            return LeadCommandContext.Forbidden<LeadDto>("Reassigning needs a management role that can reassign the leads of the lead's team.");
        }

        if (!await owners.IsActiveMemberAsync(actor.CompanyId, request.NewOwnerUserId))
        {
            return Response<LeadDto>.Error(LeadErrorCodes.InvalidOwner, ["The new owner is not an active member of the company."]);
        }

        if (request.NewOwnerUserId == lead.OwnerUserId)
        {
            return new Response<LeadDto> { IsSuccess = true, Message = "The user already owns the lead.", Data = LeadCommandContext.ToDto(lead) };
        }

        var from = lead.OwnerUserId;
        lead.OwnerUserId = request.NewOwnerUserId;
        await context.AddOwnershipHistoryAsync(lead, from, lead.SalesTeamId, LeadOwnershipCause.Reassigned, actor.UserId, DateTime.UtcNow, request.Notes);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadDto> { IsSuccess = true, Message = "Lead reassigned successfully.", Data = LeadCommandContext.ToDto(lead) };
    }
}

/// <summary>Handles <see cref="InactivateLeadCommand"/>.</summary>
public sealed class InactivateLeadCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context, LeadReferenceValidator references)
    : IRequestHandler<InactivateLeadCommand, Response<LeadDto>>
{
    public async Task<Response<LeadDto>> Handle(InactivateLeadCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadDto>(actor, request.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (LeadCommandContext.RequireOpen<LeadDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        if (!load.Access.Covers(lead))
        {
            return LeadCommandContext.Forbidden<LeadDto>("Inactivating needs scope over the lead.");
        }

        var reason = await references.ReasonAsync(actor.CompanyId, request.LeadReasonId, LeadReasonUsage.Inactive);
        if (reason is null)
        {
            return Response<LeadDto>.Error(LeadErrorCodes.InvalidLeadReason, ["The reason must be an active inactivation reason of the company."]);
        }

        var now = DateTime.UtcNow;
        lead.Status = LeadStatus.Inactive;
        lead.LeadReasonId = reason.Id;
        lead.StatusNotes = LeadCommandContext.Clean(request.Notes);
        lead.InactivatedAt = now;
        await context.AddStatusHistoryAsync(lead, LeadStatus.Open, actor.UserId, now, request.Notes);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadDto> { IsSuccess = true, Message = "Lead inactivated successfully.", Data = LeadCommandContext.ToDto(lead) };
    }
}

/// <summary>Handles <see cref="ReactivateLeadCommand"/>.</summary>
public sealed class ReactivateLeadCommandHandler(
    IUnitOfWork unitOfWork,
    LeadCommandContext context,
    LeadReferenceValidator references,
    LeadOwnerRules owners)
    : IRequestHandler<ReactivateLeadCommand, Response<LeadDto>>
{
    public async Task<Response<LeadDto>> Handle(ReactivateLeadCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadDto>(actor, request.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (lead.Status != LeadStatus.Inactive)
        {
            return Response<LeadDto>.Error(LeadErrorCodes.LeadNotInactive, ["Only an inactive lead can be reactivated."]);
        }

        if (await references.PersonAsync(actor.CompanyId, lead.PersonId) is null)
        {
            return Response<LeadDto>.Error(LeadErrorCodes.PersonInactive, ["The person of the lead is inactive; reactivate the person first."]);
        }

        if (!await owners.IsActiveMemberAsync(actor.CompanyId, actor.UserId))
        {
            return Response<LeadDto>.Error(LeadErrorCodes.InvalidOwner, ["The caller is not an active member of the company."]);
        }

        var (teamId, teamError) = await owners.ResolveTeamAsync<LeadDto>(actor.CompanyId, actor.UserId, request.SalesTeamId);
        if (teamError is not null)
        {
            return teamError;
        }

        var now = DateTime.UtcNow;
        var (fromUser, fromTeam) = (lead.OwnerUserId, lead.SalesTeamId);
        lead.Status = LeadStatus.Open;
        lead.OwnerUserId = actor.UserId;
        lead.SalesTeamId = teamId;
        lead.LeadReasonId = null;
        lead.StatusNotes = LeadCommandContext.Clean(request.Notes);
        lead.InactivatedAt = null;
        await context.AddOwnershipHistoryAsync(lead, fromUser, fromTeam, LeadOwnershipCause.Reactivated, actor.UserId, now, request.Notes);
        await context.AddStatusHistoryAsync(lead, LeadStatus.Inactive, actor.UserId, now, request.Notes);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadDto> { IsSuccess = true, Message = "Lead reactivated successfully.", Data = LeadCommandContext.ToDto(lead) };
    }
}
