using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Handles ticket status creation commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateTicketStatusCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTicketStatusCommand, Response<TicketStatusDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a ticket status catalog item.
    /// </summary>
    public async Task<Response<TicketStatusDto>> Handle(CreateTicketStatusCommand request, CancellationToken cancellationToken)
    {
        var ticketStatusRepository = _unitOfWork.GetRepository<TicketStatus>();

        var normalizedName = request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        var existingStatuses = await ticketStatusRepository.GetAllAsync();

        var nameInUse = existingStatuses.Any(status =>
            status.GcRecord == 0
            && string.Equals(status.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<TicketStatusDto>.Error("TICKET_STATUS_NAME_IN_USE", ["Another active ticket status already uses the same name."]);
        }

        var codeInUse = existingStatuses.Any(status =>
            status.GcRecord == 0
            && status.Code == request.Code);

        if (codeInUse)
        {
            return Response<TicketStatusDto>.Error("TICKET_STATUS_CODE_IN_USE", ["Another active ticket status already uses the same code."]);
        }

        var entity = new TicketStatus
        {
            Name = normalizedName,
            Description = normalizedDescription,
            Code = request.Code,
            IsActive = request.IsActive
        };

        await ticketStatusRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketStatusDto>.Error("CREATE_FAILED", ["No records were affected while creating the ticket status."]);
        }

        return new Response<TicketStatusDto>
        {
            IsSuccess = true,
            Message = "Ticket status created successfully.",
            Data = new TicketStatusDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Code = entity.Code,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}
