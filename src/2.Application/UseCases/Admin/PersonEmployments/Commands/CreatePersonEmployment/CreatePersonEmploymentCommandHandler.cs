using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Handles person employment creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreatePersonEmploymentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<CreatePersonEmploymentCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a person employment record associated with the authenticated tenant company.
    /// </summary>
    /// <param name="request">The create-employment command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the created employment identifier.</returns>
    public async Task<Response<Guid>> Handle(CreatePersonEmploymentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var personRepository = _unitOfWork.GetRepository<Person>();
        var person = await personRepository.GetAsync(request.PersonId);

        if (person is null || person.CompanyId != currentUserService.CompanyId || person.GcRecord != 0)
        {
            return Response<Guid>.Error("PERSON_NOT_FOUND", ["The requested person does not exist in the current tenant."]);
        }

        var entity = new PersonEmployment
        {
            PersonId = request.PersonId,
            EmployerName = request.EmployerName.Trim(),
            JobTitle = request.JobTitle.Trim(),
            StartDate = request.StartDate.Date,
            CompanyId = currentUserService.CompanyId
        };

        if (request.EndDate.HasValue || request.IsCurrent == false)
        {
            entity.MarkAsEnded(request.EndDate!.Value.Date);
        }

        if (request.IsActive == false)
        {
            entity.Deactivate();
        }

        var employmentRepository = _unitOfWork.GetRepository<PersonEmployment>();
        await employmentRepository.InsertAsync(entity);

        var result = await _unitOfWork.SaveAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("EMPLOYMENT_CREATE_FAILED", ["The person employment could not be created due to a persistence error."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person employment created successfully.",
            Data = entity.Id
        };
    }
}
