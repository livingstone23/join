using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

/// <summary>
/// Handles logical delete operations for tenant ticket default configurations.
/// </summary>
public sealed class DeleteTicketCompanyDefaultCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteTicketCompanyDefaultCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Marks the tenant configuration as logically deleted.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteTicketCompanyDefaultCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The X-Company-Id header is required."]);
        }

        var repository = _unitOfWork.GetRepository<TicketCompanyDefault>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null || entity.GcRecord != 0 || entity.CompanyId != currentUserService.CompanyId)
        {
            return Response<Guid>.Error("TICKET_COMPANY_DEFAULT_NOT_FOUND", ["Ticket company default configuration not found for the current tenant."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the configuration."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket company default configuration deleted successfully.",
            Data = entity.Id
        };
    }
}
