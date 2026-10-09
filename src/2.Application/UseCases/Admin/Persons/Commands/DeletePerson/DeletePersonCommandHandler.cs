using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Persons.Commands;



/// <summary>
/// Handles soft-delete operations for customer aggregates.
/// </summary>
/// <param name="unitOfWork">Unit of Work used for transactional persistence.</param>
/// <param name="currentUserService">Current tenant context.</param>
public class DeletePersonCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonCascadeCoordinator cascadeCoordinator) : IRequestHandler<DeletePersonCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Marks a customer as deleted by storing a date-stamp integer in GcRecord.
    /// </summary>
    /// <param name="request">The delete command payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A response containing the deleted customer identifier.</returns>
    public async Task<Response<Guid>> Handle(DeletePersonCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var company = await companyRepository.GetAsync(currentUserService.CompanyId);
        if (company is null)
        {
            return Response<Guid>.Error(
                "INVALID_COMPANY",
                ["The provided company does not exist or is inactive."]);
        }

        var customerEntity = await _unitOfWork.Persons.GetForUpdateAsync(request.Id, currentUserService.CompanyId);
        if (customerEntity is null)
        {
            return Response<Guid>.Error(
                "CUSTOMER_NOT_FOUND",
                ["Person not found for the current company."]);
        }

        // SPEC 41 (decision 2026-10-08): references with a life of their own block the delete;
        // the composition children are soft-deleted with the person, with the same stamp.
        var blockingReferences = await cascadeCoordinator.GetBlockingReferencesAsync(request.Id);
        if (blockingReferences.Count > 0)
        {
            return Response<Guid>.Error("PERSON_IN_USE", blockingReferences);
        }

        var deletedAtUtc = DateTime.UtcNow;
        customerEntity.MarkAsDeleted(deletedAtUtc);
        await cascadeCoordinator.MarkChildrenAsDeletedAsync(customerEntity, deletedAtUtc);

        await _unitOfWork.Persons.UpdateAsync(customerEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the customer."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Data = customerEntity.Id,
            Message = "Person deleted successfully."
        };
    }
}
