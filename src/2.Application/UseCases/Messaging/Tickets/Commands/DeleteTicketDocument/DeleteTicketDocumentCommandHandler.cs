using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Support;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.DeleteTicketDocument;

/// <summary>
/// Maneja el borrado lógico de un documento adjunto. No invoca
/// <c>IFileStorageService.DeleteAsync</c> — el binario permanece en
/// disco como evidencia del ciclo de vida del ticket (decisión documentada
/// en la spec, sección "Decisions taken and discarded").
/// </summary>
public sealed class DeleteTicketDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteTicketDocumentCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    public async Task<Response<Guid>> Handle(DeleteTicketDocumentCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketDocument>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null || entity.GcRecord != 0 || entity.CompanyId != tenantId || entity.TicketId != request.TicketId)
        {
            return Response<Guid>.Error("TICKET_DOCUMENT_NOT_FOUND", ["Ticket document not found for the current ticket and tenant."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the document."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket document deleted successfully.",
            Data = entity.Id
        };
    }
}