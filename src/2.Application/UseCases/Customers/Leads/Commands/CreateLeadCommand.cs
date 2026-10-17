using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Admin.Persons.Commands;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Finance.CompanyCurrencies;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using JOIN.Domain.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Leads.Commands;

/// <summary>A person created together with the lead (quick create, identification optional, SPEC 58).</summary>
public sealed record LeadNewPersonInput
{
    /// <summary>1 Physical, 2 Legal.</summary>
    public int PersonType { get; init; } = 1;
    /// <summary>Required for physical persons (same rule as <c>POST /Persons</c>).</summary>
    public Guid? GenderId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string? MiddleName { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string? SecondLastName { get; init; }
    public string? CommercialName { get; init; }
    public Guid? IdentificationTypeId { get; init; }
    public string? IdentificationNumber { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
}

/// <summary>
/// <c>POST /Leads</c> (SPEC 61 B.9): exactly one of <see cref="PersonId"/> or <see cref="NewPerson"/>. The lead starts
/// in the company's initial stage, <c>Open</c>, owned by <see cref="OwnerUserId"/> (default: the caller) with the team
/// of rule 4; the currency defaults to the base one.
/// </summary>
public sealed record CreateLeadCommand : ITransactionalCommand<Response<LeadCreatedDto>>
{
    public Guid? PersonId { get; init; }
    public LeadNewPersonInput? NewPerson { get; init; }
    public string Topic { get; init; } = string.Empty;
    public Guid RelationshipTypeId { get; init; }
    public Guid LeadSourceId { get; init; }
    public Guid? ReferredByPersonId { get; init; }
    public Guid? OwnerUserId { get; init; }
    public Guid? SalesTeamId { get; init; }
    public Guid? CurrencyId { get; init; }
    /// <summary>Manual value; ignored when product lines come.</summary>
    public decimal? EstimatedValue { get; init; }
    public DateOnly? EstimatedCloseDate { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? AreaId { get; init; }
    public IReadOnlyList<LeadProductInput>? ProductLines { get; init; }
    public LeadInteractionInput? FirstInteraction { get; init; }
}

/// <summary>Validates <see cref="CreateLeadCommand"/>.</summary>
public sealed class CreateLeadCommandValidator : AbstractValidator<CreateLeadCommand>
{
    public CreateLeadCommandValidator()
    {
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RelationshipTypeId).NotEmpty();
        RuleFor(x => x.LeadSourceId).NotEmpty();
        RuleFor(x => x.EstimatedValue).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true).When(x => x.EstimatedValue.HasValue);
        RuleForEach(x => x.ProductLines!).SetValidator(new LeadProductInputValidator()).When(x => x.ProductLines is not null);
        RuleFor(x => x.FirstInteraction!).SetValidator(new LeadInteractionInputValidator()).When(x => x.FirstInteraction is not null);
        RuleFor(x => x.ProductLines).Must(lines => lines is null || lines.All(l => l.OfferedInInteractionId is null))
            .WithMessage("Lines created with the lead are linked to the first contact automatically.");
    }
}

/// <summary>Handles <see cref="CreateLeadCommand"/>.</summary>
public sealed class CreateLeadCommandHandler(
    IUnitOfWork unitOfWork,
    LeadCommandContext context,
    LeadReferenceValidator references,
    LeadOwnerRules owners,
    LeadWorkRules work,
    LeadValueCalculator values,
    LeadCodeGenerator codes,
    ILeadCompanySettingsProvider settingsProvider,
    CompanyBaseCurrencyCoordinator baseCurrency,
    IValidator<CreatePersonCommand> personValidator,
    IRequestHandler<CreatePersonCommand, Response<Guid>> createPerson)
    : IRequestHandler<CreateLeadCommand, Response<LeadCreatedDto>>
{
    public async Task<Response<LeadCreatedDto>> Handle(CreateLeadCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadCreatedDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        if ((request.PersonId is null) == (request.NewPerson is null))
        {
            return Response<LeadCreatedDto>.Error(LeadErrorCodes.LeadPersonRequired, ["Send exactly one of PersonId or NewPerson."]);
        }

        var companyId = actor.CompanyId;
        if (await references.SourceAsync<LeadCreatedDto>(companyId, request.LeadSourceId) is { } sourceError)
        {
            return sourceError;
        }

        if (await references.RelationshipTypeAsync<LeadCreatedDto>(companyId, request.RelationshipTypeId) is { } typeError)
        {
            return typeError;
        }

        if (request.ReferredByPersonId is { } referredId && await references.PersonAsync(companyId, referredId, mustBeActive: false) is null)
        {
            return Response<LeadCreatedDto>.Error(LeadErrorCodes.InvalidPerson, ["The referring person does not exist in the company."]);
        }

        if (await references.ProjectAndAreaAsync<LeadCreatedDto>(companyId, request.ProjectId, request.AreaId) is { } referenceError)
        {
            return referenceError;
        }

        var ownerId = request.OwnerUserId ?? actor.UserId;
        if (!await owners.IsActiveMemberAsync(companyId, ownerId))
        {
            return Response<LeadCreatedDto>.Error(LeadErrorCodes.InvalidOwner, ["The owner is not an active member of the company."]);
        }

        var (teamId, teamError) = await owners.ResolveTeamAsync<LeadCreatedDto>(companyId, ownerId, request.SalesTeamId);
        if (teamError is not null)
        {
            return teamError;
        }

        var currencyId = request.CurrencyId ?? (await baseCurrency.GetBaseAsync(companyId))?.CurrencyId;
        var currencyActive = currencyId is { } cid
            && (await unitOfWork.GetRepository<CompanyCurrency>().GetAllIncludingDeletedAsync(c =>
                c.CompanyId == companyId && c.CurrencyId == cid && c.GcRecord == 0 && c.IsActive)).Any();
        if (!currencyActive)
        {
            return Response<LeadCreatedDto>.Error(LeadErrorCodes.InvalidCurrency, ["The currency is not an active currency of the company."]);
        }

        var initial = (await references.StagesAsync(companyId)).FirstOrDefault(s => s.Kind == LeadStageKind.Initial && s.IsActive);
        if (initial is null)
        {
            return Response<LeadCreatedDto>.Error(LeadErrorCodes.InvalidLeadStage, ["The company has no active initial stage."]);
        }

        var (personId, personError) = await ResolvePersonAsync(companyId, request, cancellationToken);
        if (personError is not null)
        {
            return personError;
        }

        var settings = await settingsProvider.GetAsync(companyId);
        var duplicates = settings.DuplicateOpenLeadPolicy == DuplicateOpenLeadPolicy.Allow
            ? []
            : (await unitOfWork.GetRepository<Lead>().GetAllIncludingDeletedAsync(l =>
                l.CompanyId == companyId && l.PersonId == personId && l.GcRecord == 0 && l.Status == LeadStatus.Open)).ToList();
        if (duplicates.Count > 0 && settings.DuplicateOpenLeadPolicy == DuplicateOpenLeadPolicy.Block)
        {
            return Response<LeadCreatedDto>.Error(LeadErrorCodes.LeadDuplicateOpen, duplicates.Select(d => d.Code).ToList());
        }

        var now = DateTime.UtcNow;
        var lead = new Lead
        {
            CompanyId = companyId,
            Code = await codes.GenerateNextAsync(companyId, cancellationToken),
            PersonId = personId,
            Topic = request.Topic.Trim(),
            OwnerUserId = ownerId,
            SalesTeamId = teamId,
            LeadStageId = initial.Id,
            LeadSourceId = request.LeadSourceId,
            RelationshipTypeId = request.RelationshipTypeId,
            ReferredByPersonId = request.ReferredByPersonId,
            CurrencyId = currencyId!.Value,
            EstimatedCloseDate = request.EstimatedCloseDate,
            ProjectId = request.ProjectId,
            AreaId = request.AreaId
        };

        var (rate, rateError) = await values.RateAsync<LeadCreatedDto>(companyId, lead.CurrencyId, cancellationToken);
        if (rateError is not null)
        {
            return rateError;
        }

        lead.ExchangeRate = rate!.Rate;

        LeadInteraction? interaction = null;
        if (request.FirstInteraction is { } first)
        {
            (interaction, var interactionError) = await work.BuildInteractionAsync<LeadCreatedDto>(lead, first, actor.UserId);
            if (interactionError is not null)
            {
                return interactionError;
            }

            LeadWorkRules.Touch(lead, interaction!.OccurredAt);
        }

        var lines = new List<LeadProduct>();
        foreach (var input in request.ProductLines ?? [])
        {
            var (line, lineError) = await work.BuildLineAsync<LeadCreatedDto>(lead, input with { OfferedInInteractionId = interaction?.Id });
            if (lineError is not null)
            {
                return lineError;
            }

            lines.Add(line!);
        }

        if (await values.RecalculateAsync<LeadCreatedDto>(lead, request.EstimatedValue ?? 0m, lines, cancellationToken) is { } valueError)
        {
            return valueError;
        }

        await unitOfWork.GetRepository<Lead>().InsertAsync(lead);
        if (interaction is not null)
        {
            await unitOfWork.GetRepository<LeadInteraction>().InsertAsync(interaction);
        }

        foreach (var line in lines)
        {
            await unitOfWork.GetRepository<LeadProduct>().InsertAsync(line);
        }

        await context.AddStageHistoryAsync(lead, null, actor.UserId, now);
        await context.AddOwnershipHistoryAsync(lead, null, null, LeadOwnershipCause.Created, actor.UserId, now, null);
        await context.AddStatusHistoryAsync(lead, null, actor.UserId, now, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<LeadCreatedDto>
        {
            IsSuccess = true,
            Message = duplicates.Count > 0 ? "Lead created; the person already has open leads." : "Lead created successfully.",
            Data = new LeadCreatedDto
            {
                Lead = LeadCommandContext.ToDto(lead),
                DuplicateOpenLeads = duplicates.Select(d => new LeadDuplicateDto
                {
                    Id = d.Id, Code = d.Code, Topic = d.Topic, OwnerUserId = d.OwnerUserId, CreatedAt = d.Created
                }).ToList()
            }
        };
    }

    private async Task<(Guid PersonId, Response<LeadCreatedDto>? Error)> ResolvePersonAsync(Guid companyId, CreateLeadCommand request, CancellationToken cancellationToken)
    {
        if (request.PersonId is { } existingId)
        {
            return await references.PersonAsync(companyId, existingId) is null
                ? (Guid.Empty, Response<LeadCreatedDto>.Error(LeadErrorCodes.InvalidPerson, ["The person does not exist, is inactive or deleted in the company."]))
                : (existingId, null);
        }

        var input = request.NewPerson!;
        var contacts = new List<CreatePersonCommand.CreatePersonContactDto>();
        if (!string.IsNullOrWhiteSpace(input.Email))
        {
            contacts.Add(new() { ContactType = nameof(JOIN.Domain.Enums.ContactType.PrimaryEmail), ContactValue = input.Email.Trim(), IsPrimary = true });
        }

        if (!string.IsNullOrWhiteSpace(input.Phone))
        {
            contacts.Add(new() { ContactType = nameof(JOIN.Domain.Enums.ContactType.MobilePhone), ContactValue = input.Phone.Trim(), IsPrimary = contacts.Count == 0 });
        }

        var command = new CreatePersonCommand
        {
            PersonType = (PersonType)input.PersonType,
            GenderId = input.GenderId,
            FirstName = input.FirstName,
            MiddleName = input.MiddleName,
            LastName = input.LastName,
            SecondLastName = input.SecondLastName,
            CommercialName = input.CommercialName,
            IdentificationTypeId = input.IdentificationTypeId,
            IdentificationNumber = input.IdentificationNumber,
            Contacts = contacts
        };

        var validation = await personValidator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return (Guid.Empty, Response<LeadCreatedDto>.Error(LeadErrorCodes.InvalidPerson, validation.Errors.Select(e => e.ErrorMessage).ToList()));
        }

        var created = await createPerson.Handle(command, cancellationToken);
        return created.IsSuccess
            ? (created.Data, null)
            : (Guid.Empty, Response<LeadCreatedDto>.Error(LeadErrorCodes.InvalidPerson, [created.Message, .. created.Errors ?? []]));
    }
}
