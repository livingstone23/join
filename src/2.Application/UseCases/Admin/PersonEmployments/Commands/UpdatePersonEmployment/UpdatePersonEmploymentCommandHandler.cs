using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonEmployments;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Handles person employment updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdatePersonEmploymentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonEmploymentCurrentCoordinator currentCoordinator) : IRequestHandler<UpdatePersonEmploymentCommand, Response<Guid>>
{
    /// <summary>
    /// Updates a person employment record for the current tenant.
    /// </summary>
    public async Task<Response<Guid>> Handle(UpdatePersonEmploymentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var employmentRepository = unitOfWork.PersonEmployments;
        var entity = await employmentRepository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException(
                nameof(PersonEmployment),
                request.Id,
                "Person employment not found for the current tenant.");
        }

        if (entity.PersonId != request.PersonId)
        {
            throw new NotFoundException(
                nameof(PersonEmployment),
                request.Id,
                "Person employment not found for the requested person.");
        }

        try
        {
            entity.Update(
                request.EmployerName,
                request.JobTitle,
                request.StartDate,
                request.EndDate);

            if (request.IsActive.HasValue)
            {
                if (request.IsActive.Value)
                {
                    entity.Reactivate();
                }
                else
                {
                    entity.Deactivate();
                }
            }

            if (request.IsCurrent == true)
            {
                await currentCoordinator.ClearOtherCurrentAsync(
                    companyId,
                    request.PersonId,
                    entity.Id,
                    cancellationToken);
                entity.SetAsCurrent();
            }
            else if (request.IsCurrent == false)
            {
                entity.RemoveCurrent();
            }
        }
        catch (ArgumentException ex)
        {
            return Response<Guid>.Error("INVALID_EMPLOYMENT_DATA", [ex.Message]);
        }
        catch (InvalidOperationException ex)
        {
            return Response<Guid>.Error("INVALID_EMPLOYMENT_CURRENT", [ex.Message]);
        }

        await employmentRepository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the person employment."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person employment updated successfully.",
            Data = entity.Id
        };
    }
}
