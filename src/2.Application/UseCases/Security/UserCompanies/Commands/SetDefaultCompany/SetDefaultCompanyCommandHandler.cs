using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.SetDefaultCompany;

/// <summary>
/// Handles the update of the default company for a specific user.
/// </summary>
/// <param name="unitOfWork">Unit of work used to persist the operation atomically.</param>
public sealed class SetDefaultCompanyCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<SetDefaultCompanyCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Reassigns the default company for the requested user.
    /// </summary>
    /// <param name="request">The user/company pair that should become the new default context.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A standardized response containing the new default company identifier.</returns>
    public async Task<Response<Guid>> Handle(SetDefaultCompanyCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<UserCompany>();
        var userCompanies = (await repository.GetAllAsync())
            .Where(link => link.UserId == request.UserId)
            .ToList();

        if (userCompanies.Count == 0)
        {
            return Response<Guid>.Error(
                "USER_COMPANY_NOT_FOUND",
                ["The user does not have any active company assignments."]);
        }

        var targetLink = userCompanies.FirstOrDefault(link => link.CompanyId == request.CompanyId);
        if (targetLink is null)
        {
            return Response<Guid>.Error(
                "TARGET_COMPANY_NOT_ASSIGNED",
                ["The selected company is not assigned to the user."]);
        }

        var hasChanges = false;

        foreach (var currentDefault in userCompanies.Where(link => link.IsDefault && link.Id != targetLink.Id))
        {
            currentDefault.IsDefault = false;
            await repository.UpdateAsync(currentDefault);
            hasChanges = true;
        }

        if (!targetLink.IsDefault)
        {
            targetLink.IsDefault = true;
            await repository.UpdateAsync(targetLink);
            hasChanges = true;
        }

        // A single SaveChangesAsync call is committed by EF Core in one atomic database transaction.
        if (hasChanges)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Default company updated successfully.",
            Data = targetLink.CompanyId
        };
    }
}
