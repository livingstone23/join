using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Handles ticket status update commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class UpdateTicketStatusCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateTicketStatusCommand, Response<TicketStatusDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// Updates a ticket status catalog item.
    /// </summary>
    public async Task<Response<TicketStatusDto>> Handle(UpdateTicketStatusCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketStatusDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var ticketStatusRepository = _unitOfWork.GetRepository<TicketStatus>();
        var companyRepository = _unitOfWork.GetRepository<Company>();

        var entity = await ticketStatusRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<TicketStatusDto>.Error("TICKET_STATUS_NOT_FOUND", ["Ticket status not found."]);
        }

        var normalizedName = request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        var existingStatuses = await ticketStatusRepository.GetAllAsync();

        var nameInUse = existingStatuses.Any(status =>
            status.Id != request.Id
            && status.GcRecord == 0
            && string.Equals(status.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<TicketStatusDto>.Error("TICKET_STATUS_NAME_IN_USE", ["Another active ticket status already uses the same name."]);
        }

        var codeInUse = existingStatuses.Any(status =>
            status.Id != request.Id
            && status.GcRecord == 0
            && status.Code == request.Code);

        if (codeInUse)
        {
            return Response<TicketStatusDto>.Error("TICKET_STATUS_CODE_IN_USE", ["Another active ticket status already uses the same code."]);
        }

        entity.Name = normalizedName;
        entity.Description = normalizedDescription;
        entity.Code = request.Code;
        entity.IsActive = request.IsActive;
        entity.IsInitial = request.IsInitial;
        entity.IsPaused = request.IsPaused;
        entity.IsFinal = request.IsFinal;

        await ticketStatusRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketStatusDto>.Error("UPDATE_FAILED", ["No records were affected while updating the ticket status."]);
        }

        var company = await companyRepository.GetAsync(entity.CompanyId);

        return new Response<TicketStatusDto>
        {
            IsSuccess = true,
            Message = "Ticket status updated successfully.",
            Data = new TicketStatusDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company?.Name,
                Name = entity.Name,
                Description = entity.Description,
                Code = entity.Code,
                IsActive = entity.IsActive,
                IsInitial = entity.IsInitial,
                IsPaused = entity.IsPaused,
                IsFinal = entity.IsFinal,
                CreatedAt = entity.Created
            }
        };
    }
}
