using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Handles soft delete operations for companies.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class DeleteCompanyCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCompanyCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking GcRecord.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteCompanyCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<Company>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null)
        {
            return Response<Guid>.Error("COMPANY_NOT_FOUND", ["Company not found."]);
        }

        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the company."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Company deleted successfully.",
            Data = entity.Id
        };
    }
}
