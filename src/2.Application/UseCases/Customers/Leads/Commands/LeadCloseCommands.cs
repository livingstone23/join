using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Finance;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers;
using JOIN.Domain.Customers.Leads;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Leads.Commands;

/// <summary>
/// <c>POST /Leads/{id}/win</c> (SPEC 63 A.2): closes an open lead as won from any open stage. The listed lines become
/// <c>Accepted</c> and the rest <c>Rejected</c>; the won value is fixed with the rate of the closing day. When the
/// relationship type creates customers, the person's customer is created, reactivated or confirmed (origin <c>Lead</c>).
/// </summary>
public sealed record WinLeadCommand : ITransactionalCommand<Response<LeadWinResultDto>>
{
    public Guid Id { get; init; }
    public Guid LeadReasonId { get; init; }
    public IReadOnlyList<Guid> AcceptedLeadProductIds { get; init; } = [];
    /// <summary>Completes the person's identification when it is missing (both or none).</summary>
    public Guid? IdentificationTypeId { get; init; }
    public string? IdentificationNumber { get; init; }
    public string? Notes { get; init; }
}

/// <summary><c>POST /Leads/{id}/lose</c> (SPEC 63 A.3): reason of use <c>Lost</c>, optional competitor.</summary>
public sealed record LoseLeadCommand : ITransactionalCommand<Response<LeadDto>>
{
    public Guid Id { get; init; }
    public Guid LeadReasonId { get; init; }
    public string? CompetitorName { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// <c>POST /Leads/{id}/reopen</c> (SPEC 63 A.4): a lost lead goes back to <c>Open</c> in its last open stage (the
/// initial one when that stage is no longer active). Won leads are never reopened.
/// </summary>
public sealed record ReopenLeadCommand : ITransactionalCommand<Response<LeadDto>>
{
    public Guid Id { get; init; }
    public string? Notes { get; init; }
}

/// <summary>Validates <see cref="WinLeadCommand"/>.</summary>
public sealed class WinLeadCommandValidator : AbstractValidator<WinLeadCommand>
{
    public WinLeadCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.LeadReasonId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.IdentificationNumber).MaximumLength(50);
        RuleFor(x => x)
            .Must(x => (x.IdentificationTypeId is { } typeId && typeId != Guid.Empty) == !string.IsNullOrWhiteSpace(x.IdentificationNumber))
            .WithName("Identification")
            .WithMessage("IdentificationTypeId and IdentificationNumber must be provided together.");
    }
}

/// <summary>Validates <see cref="LoseLeadCommand"/>.</summary>
public sealed class LoseLeadCommandValidator : AbstractValidator<LoseLeadCommand>
{
    public LoseLeadCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.LeadReasonId).NotEmpty();
        RuleFor(x => x.CompetitorName).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Validates <see cref="ReopenLeadCommand"/>.</summary>
public sealed class ReopenLeadCommandValidator : AbstractValidator<ReopenLeadCommand>
{
    public ReopenLeadCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Handles <see cref="WinLeadCommand"/>.</summary>
public sealed class WinLeadCommandHandler(
    IUnitOfWork unitOfWork,
    LeadCommandContext context,
    LeadReferenceValidator references,
    LeadValueCalculator values,
    IMoneyConverter converter,
    ILeadCompanySettingsProvider settingsProvider,
    ICustomerActivationService activation)
    : IRequestHandler<WinLeadCommand, Response<LeadWinResultDto>>
{
    public async Task<Response<LeadWinResultDto>> Handle(WinLeadCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadWinResultDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadWinResultDto>(actor, request.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (LeadCommandContext.RequireOpen<LeadWinResultDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        var reason = await references.ReasonAsync(actor.CompanyId, request.LeadReasonId, LeadReasonUsage.Won);
        if (reason is null)
        {
            return Response<LeadWinResultDto>.Error(LeadErrorCodes.InvalidLeadReason, ["The reason must be an active win reason of the company."]);
        }

        var relationship = (await unitOfWork.GetRepository<RelationshipType>().GetAllIncludingDeletedAsync(r => r.Id == lead.RelationshipTypeId)).First();
        var createsCustomer = relationship.OnWin == RelationshipOnWin.Customer;
        if (createsCustomer && await CompleteIdentificationAsync(actor.CompanyId, lead.PersonId, request, cancellationToken) is { } identificationError)
        {
            return identificationError;
        }

        var lines = (await unitOfWork.GetRepository<LeadProduct>().GetAllIncludingDeletedAsync(l => l.LeadId == lead.Id && l.GcRecord == 0)).ToList();
        var accepted = request.AcceptedLeadProductIds.Distinct().ToHashSet();
        if (accepted.Any(id => lines.All(l => l.Id != id)))
        {
            return Response<LeadWinResultDto>.Error(LeadErrorCodes.InvalidLeadProduct, ["Every accepted line must be a live product line of the lead."]);
        }

        var (rate, rateError) = await values.RateAsync<LeadWinResultDto>(actor.CompanyId, lead.CurrencyId, cancellationToken);
        if (rateError is not null)
        {
            return rateError;
        }

        var wonStage = (await references.StagesAsync(actor.CompanyId)).First(s => s.Kind == LeadStageKind.Won);
        var lineRepository = unitOfWork.GetRepository<LeadProduct>();
        foreach (var line in lines)
        {
            var tracked = await lineRepository.GetAsync(line.Id);
            tracked!.Status = accepted.Contains(line.Id) ? LeadProductStatus.Accepted : LeadProductStatus.Rejected;
            await lineRepository.UpdateAsync(tracked);
        }

        var now = DateTime.UtcNow;
        var fromStage = lead.LeadStageId;
        lead.WonValue = lines.Count > 0 ? lines.Where(l => accepted.Contains(l.Id)).Sum(l => l.LineTotal) : lead.EstimatedValue;
        lead.WonExchangeRate = rate!.Rate;
        lead.WonValueBase = converter.ToBase(lead.WonValue.Value, rate.Rate);
        lead.LeadStageId = wonStage.Id;
        lead.Status = LeadStatus.Won;
        lead.LeadReasonId = reason.Id;
        lead.StatusNotes = LeadCommandContext.Clean(request.Notes);
        lead.ClosedAt = now;
        await context.AddStageHistoryAsync(lead, fromStage, actor.UserId, now);
        await context.AddStatusHistoryAsync(lead, LeadStatus.Open, actor.UserId, now, request.Notes);

        CustomerActivationResult? customer = null;
        if (createsCustomer)
        {
            var activated = await activation.EnsureActiveAsync(
                actor.CompanyId, lead.PersonId, CustomerStatusSource.Lead, actor.UserId, sourceReferenceId: lead.Id, notes: LeadCommandContext.Clean(request.Notes),
                cancellationToken: cancellationToken);
            if (!activated.IsSuccess)
            {
                return Response<LeadWinResultDto>.Error(activated.Message, activated.Errors?.ToList() ?? []);
            }

            customer = activated.Data;
            lead.CustomerId = customer!.Customer.Id;
        }

        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<LeadWinResultDto>
        {
            IsSuccess = true,
            Message = customer is null ? "Lead won." : $"Lead won; customer {customer.Action?.ToString() ?? "Unchanged"}.",
            Data = new LeadWinResultDto
            {
                Lead = LeadCommandContext.ToDto(lead),
                CustomerId = customer?.Customer.Id,
                CustomerCode = customer?.Customer.CustomerCode,
                CustomerAction = customer?.Action?.ToString() ?? (customer is null ? null : nameof(CustomerStatusAction.Confirmed))
            }
        };
    }

    /// <summary>
    /// Rule 2: when the company requires it and the person has none, the identification must come with the command; it
    /// is saved on the person (unique among live persons, SPEC 58) before the customer is activated.
    /// </summary>
    private async Task<Response<LeadWinResultDto>?> CompleteIdentificationAsync(Guid companyId, Guid personId, WinLeadCommand request, CancellationToken cancellationToken)
    {
        var persons = unitOfWork.GetRepository<Person>();
        // Tracked EF load (the persons repository's GetAsync reads with Dapper).
        var person = await unitOfWork.Persons.GetForUpdateAsync(personId, companyId);
        if (person is null || person.GcRecord != 0 || !person.IsActive)
        {
            return Response<LeadWinResultDto>.Error(LeadErrorCodes.InvalidPerson, ["The person of the lead is inactive or deleted."]);
        }

        if (person.HasIdentification)
        {
            return null;
        }

        if (request.IdentificationTypeId is not { } typeId)
        {
            return (await settingsProvider.GetAsync(companyId)).RequireIdentificationForCustomer
                ? Response<LeadWinResultDto>.Error(LeadErrorCodes.PersonIdentificationRequired, ["The person needs an identification type and number to become a customer."])
                : null;
        }

        if (await unitOfWork.GetRepository<IdentificationType>().GetAsync(typeId) is null)
        {
            return Response<LeadWinResultDto>.Error(LeadErrorCodes.InvalidReference, ["The identification type does not exist."]);
        }

        var number = request.IdentificationNumber!.Trim();
        var taken = (await persons.GetAllIncludingDeletedAsync(p =>
                p.CompanyId == companyId && p.GcRecord == 0 && p.Id != personId && p.IdentificationTypeId == typeId && p.IdentificationNumber == number))
            .Any();
        if (taken)
        {
            return Response<LeadWinResultDto>.Error(LeadErrorCodes.DuplicateKey, ["Another person of the company already has this identification."]);
        }

        person.IdentificationTypeId = typeId;
        person.IdentificationNumber = number;
        await persons.UpdateAsync(person);
        // EnsureActiveAsync reads the person again: the identification must be saved first (same transaction).
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return null;
    }
}

/// <summary>Handles <see cref="LoseLeadCommand"/>.</summary>
public sealed class LoseLeadCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context, LeadReferenceValidator references)
    : IRequestHandler<LoseLeadCommand, Response<LeadDto>>
{
    public async Task<Response<LeadDto>> Handle(LoseLeadCommand request, CancellationToken cancellationToken)
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

        var reason = await references.ReasonAsync(actor.CompanyId, request.LeadReasonId, LeadReasonUsage.Lost);
        if (reason is null)
        {
            return Response<LeadDto>.Error(LeadErrorCodes.InvalidLeadReason, ["The reason must be an active loss reason of the company."]);
        }

        var lostStage = (await references.StagesAsync(actor.CompanyId)).First(s => s.Kind == LeadStageKind.Lost);
        var now = DateTime.UtcNow;
        var fromStage = lead.LeadStageId;
        lead.LeadStageId = lostStage.Id;
        lead.Status = LeadStatus.Lost;
        lead.LeadReasonId = reason.Id;
        lead.CompetitorName = LeadCommandContext.Clean(request.CompetitorName);
        lead.StatusNotes = LeadCommandContext.Clean(request.Notes);
        lead.ClosedAt = now;
        await context.AddStageHistoryAsync(lead, fromStage, actor.UserId, now);
        await context.AddStatusHistoryAsync(lead, LeadStatus.Open, actor.UserId, now, request.Notes);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadDto> { IsSuccess = true, Message = "Lead lost.", Data = LeadCommandContext.ToDto(lead) };
    }
}

/// <summary>Handles <see cref="ReopenLeadCommand"/>.</summary>
public sealed class ReopenLeadCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context, LeadReferenceValidator references)
    : IRequestHandler<ReopenLeadCommand, Response<LeadDto>>
{
    public async Task<Response<LeadDto>> Handle(ReopenLeadCommand request, CancellationToken cancellationToken)
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
        if (lead.Status != LeadStatus.Lost)
        {
            return Response<LeadDto>.Error(LeadErrorCodes.LeadNotLost, ["Only a lost lead can be reopened."]);
        }

        var stages = await references.StagesAsync(actor.CompanyId);
        var openStages = stages.Where(s => s.IsActive && s.Kind is LeadStageKind.Initial or LeadStageKind.Open).ToDictionary(s => s.Id);
        var history = (await unitOfWork.GetRepository<LeadStageHistory>().GetAllIncludingDeletedAsync(h => h.LeadId == lead.Id && h.GcRecord == 0))
            .OrderByDescending(h => h.ChangedAt)
            .ToList();
        var target = history.Select(h => h.ToStageId).FirstOrDefault(openStages.ContainsKey);
        var stage = target != Guid.Empty ? openStages[target] : stages.First(s => s.Kind == LeadStageKind.Initial);

        var now = DateTime.UtcNow;
        var fromStage = lead.LeadStageId;
        lead.LeadStageId = stage.Id;
        lead.Status = LeadStatus.Open;
        lead.LeadReasonId = null;
        lead.CompetitorName = null;
        lead.StatusNotes = LeadCommandContext.Clean(request.Notes);
        lead.ClosedAt = null;
        await context.AddStageHistoryAsync(lead, fromStage, actor.UserId, now);
        await context.AddStatusHistoryAsync(lead, LeadStatus.Lost, actor.UserId, now, request.Notes);
        await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadDto> { IsSuccess = true, Message = "Lead reopened.", Data = LeadCommandContext.ToDto(lead) };
    }
}
