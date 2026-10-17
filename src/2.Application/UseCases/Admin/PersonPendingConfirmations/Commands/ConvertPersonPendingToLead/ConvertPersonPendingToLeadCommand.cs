using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Customers.Leads.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ConvertPersonPendingToLead;

/// <summary>
/// <c>POST /PersonPendingConfirmations/{id}/convert-to-lead</c> (SPEC 65 B.6): a <c>Pending</c> person becomes an open
/// lead with origin <c>PENDING</c>, owned by the caller (SPEC 61 rules, duplicates included); the pending is resolved as
/// <c>ConvertedToLead</c> with the lead. No customer is created or activated (SPEC 55 rule 9).
/// </summary>
public sealed record ConvertPersonPendingToLeadCommand : ITransactionalCommand<Response<ConvertPersonPendingToLeadDto>>
{
    public Guid PendingConfirmationId { get; init; }
    public string Topic { get; init; } = string.Empty;
    public Guid RelationshipTypeId { get; init; }
    public Guid? SalesTeamId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>Result of the conversion: the resolved pending and the new lead.</summary>
public sealed record ConvertPersonPendingToLeadDto(Guid Id, Guid LeadId, string LeadCode, IReadOnlyList<LeadDuplicateDto> DuplicateOpenLeads);

/// <summary>Validates <see cref="ConvertPersonPendingToLeadCommand"/>.</summary>
public sealed class ConvertPersonPendingToLeadCommandValidator : AbstractValidator<ConvertPersonPendingToLeadCommand>
{
    public ConvertPersonPendingToLeadCommandValidator()
    {
        RuleFor(x => x.PendingConfirmationId).NotEmpty();
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RelationshipTypeId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Handles <see cref="ConvertPersonPendingToLeadCommand"/>.</summary>
public sealed class ConvertPersonPendingToLeadCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonPendingConfirmationReader reader,
    PersonPendingReviewCloser reviewCloser,
    IRequestHandler<CreateLeadCommand, Response<LeadCreatedDto>> createLead)
    : IRequestHandler<ConvertPersonPendingToLeadCommand, Response<ConvertPersonPendingToLeadDto>>
{
    /// <summary>Code of the lead source used for converted pending persons (SPEC 59 seed).</summary>
    public const string PendingSourceCode = "PENDING";
    public const string NotPending = "PERSON_PENDING_NOT_PENDING";

    public async Task<Response<ConvertPersonPendingToLeadDto>> Handle(ConvertPersonPendingToLeadCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<ConvertPersonPendingToLeadDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId) || userId == Guid.Empty)
        {
            return Response<ConvertPersonPendingToLeadDto>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        var opened = await reader.OpenPendingAsync(companyId, request.PendingConfirmationId);
        if (!opened.IsSuccess)
        {
            return opened.Message == CalendarErrorCodes.PersonPendingAlreadyResolved
                ? Response<ConvertPersonPendingToLeadDto>.Error(NotPending, ["Only a Pending person can be converted into a lead."])
                : Response<ConvertPersonPendingToLeadDto>.Error(opened.Message, opened.Errors);
        }

        var pending = opened.Data!;
        var source = (await unitOfWork.GetRepository<LeadSource>().GetAllIncludingDeletedAsync(s =>
                s.CompanyId == companyId && s.Code == PendingSourceCode && s.GcRecord == 0 && s.IsActive))
            .FirstOrDefault();
        if (source is null)
        {
            return Response<ConvertPersonPendingToLeadDto>.Error(LeadErrorCodes.InvalidLeadSource, ["The company has no active PENDING lead source."]);
        }

        var created = await createLead.Handle(new CreateLeadCommand
        {
            PersonId = pending.PersonId,
            Topic = request.Topic,
            RelationshipTypeId = request.RelationshipTypeId,
            LeadSourceId = source.Id,
            SalesTeamId = request.SalesTeamId
        }, cancellationToken);
        if (!created.IsSuccess)
        {
            return Response<ConvertPersonPendingToLeadDto>.Error(created.Message, created.Errors?.ToList() ?? []);
        }

        var lead = created.Data!.Lead;
        pending.Status = PersonPendingConfirmationStatus.ConvertedToLead;
        pending.LeadId = lead.Id;
        pending.ResolvedByUserId = userId;
        pending.ResolvedUtc = DateTime.UtcNow;
        pending.ResolutionNotes = string.IsNullOrWhiteSpace(request.Notes) ? $"Convertida en el lead {lead.Code}" : request.Notes.Trim();
        await unitOfWork.GetRepository<PersonPendingConfirmation>().UpdateAsync(pending);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var closed = await reviewCloser.CloseAsync(pending, cancellationToken);
        if (!closed.IsSuccess)
        {
            return Response<ConvertPersonPendingToLeadDto>.Error(closed.Message, closed.Errors);
        }

        return new Response<ConvertPersonPendingToLeadDto>
        {
            IsSuccess = true,
            Message = "Person pending confirmation converted into a lead successfully.",
            Data = new ConvertPersonPendingToLeadDto(pending.Id, lead.Id, lead.Code, created.Data.DuplicateOpenLeads)
        };
    }
}
