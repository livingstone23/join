using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.AddTicketNote;

/// <summary>
/// Handles <see cref="AddTicketNoteCommand"/>: appends an internal/external note
/// to a ticket and returns the freshly-created <see cref="TicketLogDto"/>.
/// Visibility is derived server-side from the <see cref="LogType"/>; this handler
/// does not trust the caller to flag the note as restricted.
/// </summary>
public sealed class AddTicketNoteCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<AddTicketNoteCommand, Response<TicketLogDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    public async Task<Response<TicketLogDto>> Handle(
        AddTicketNoteCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<TicketLogDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(_currentUserService.UserId, out var actorUserId))
        {
            return Response<TicketLogDto>.Error("USER_REQUIRED", ["The authenticated user identifier is required."]);
        }

        // SPEC 35 F8 step 3 — only InternalNote/ExternalNote are user-facing; the other
        // LogType values (Creation, StatusChange, Reassignment, Finalization) are produced
        // by other handlers. Defensive duplicate of the validator rule so the business
        // code INVALID_LOG_TYPE reaches the caller even if the validator pipeline is
        // bypassed by a non-FluentValidation call site.
        if (request.LogType is not (LogType.InternalNote or LogType.ExternalNote))
        {
            return Response<TicketLogDto>.Error("INVALID_LOG_TYPE", ["LogType must be InternalNote or ExternalNote."]);
        }

        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        var entity = await ticketRepository.GetAsync(request.TicketId);
        if (entity is null || entity.CompanyId != tenantId)
        {
            return Response<TicketLogDto>.Error("TICKET_NOT_FOUND", ["Ticket not found for the current company."]);
        }

        var isInternal = request.LogType == LogType.InternalNote;
        entity.AddLog(
            actorUserId,
            request.LogType,
            request.Summary,
            isOnlyForCreatedAndAssigned: isInternal);

        await ticketRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketLogDto>.Error("ADD_NOTE_FAILED", ["No records were affected while saving the note."]);
        }

        // The log we just appended is the last entry (append-only on the aggregate).
        var savedLog = entity.TicketLogs.Last();

        var userRepository = _unitOfWork.GetRepository<ApplicationUser>();
        var registeredBy = await userRepository.GetAsync(savedLog.UserRegisterLogId);
        var currentStatus = await _unitOfWork.GetRepository<TicketStatus>().GetAsync(entity.TicketStatusId);

        var dto = new TicketLogDto
        {
            Id = savedLog.Id,
            LogType = savedLog.LogType.ToString(),
            Summary = savedLog.Summary,
            CreatedAt = savedLog.Created,
            UserRegisteredName = registeredBy is null
                ? string.Empty
                : $"{registeredBy.FirstName} {registeredBy.LastName}".Trim(),
            PreviousStatusName = null,
            NewStatusName = currentStatus?.Name ?? string.Empty,
            ConsumedTime = savedLog.ConsumedTime
        };

        return new Response<TicketLogDto>
        {
            IsSuccess = true,
            Message = "Note added successfully.",
            Data = dto
        };
    }
}
