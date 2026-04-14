using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands;

/// <summary>
/// Handles soft-delete operations for tickets.
/// </summary>
public sealed class DeleteTicketCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteTicketCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Marks a ticket as logically deleted for the current tenant.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteTicketCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        if (await companyRepository.GetAsync(currentUserService.CompanyId) is null)
        {
            return Response<Guid>.Error("INVALID_COMPANY", ["The provided company does not exist or is inactive."]);
        }

        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        var ticket = await ticketRepository.GetAsync(request.Id);

        if (ticket is null || ticket.CompanyId != currentUserService.CompanyId)
        {
            return Response<Guid>.Error("TICKET_NOT_FOUND", ["Ticket not found for the current company."]);
        }

        ticket.MarkAsDeleted();

        await ticketRepository.UpdateAsync(ticket);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the ticket."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket deleted successfully.",
            Data = ticket.Id
        };
    }
}
