using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Handles person employment updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdatePersonEmploymentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<UpdatePersonEmploymentCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a person employment record for the current tenant.
    /// </summary>
    /// <param name="request">The update-employment command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the updated employment identifier.</returns>
    public async Task<Response<Guid>> Handle(UpdatePersonEmploymentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var employmentRepository = _unitOfWork.GetRepository<PersonEmployment>();
        var employments = await employmentRepository.GetAllAsync();

        var entity = employments.FirstOrDefault(employment =>
            employment.Id == request.Id &&
            employment.CompanyId == currentUserService.CompanyId);

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

        entity.EmployerName = request.EmployerName.Trim();
        entity.JobTitle = request.JobTitle.Trim();
        entity.StartDate = request.StartDate.Date;

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

        if (request.EndDate.HasValue)
        {
            entity.MarkAsEnded(request.EndDate.Value.Date);
        }

        await employmentRepository.UpdateAsync(entity);

        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

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
