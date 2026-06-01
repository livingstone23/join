using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonEmployments;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Handles person employment creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreatePersonEmploymentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonEmploymentCurrentCoordinator currentCoordinator) : IRequestHandler<CreatePersonEmploymentCommand, Response<Guid>>
{
    /// <summary>
    /// Creates a person employment record associated with the authenticated tenant company.
    /// </summary>
    public async Task<Response<Guid>> Handle(CreatePersonEmploymentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var person = await unitOfWork.GetRepository<Person>().GetAsync(request.PersonId);

        if (person is null || person.CompanyId != companyId || person.GcRecord != 0)
        {
            return Response<Guid>.Error("PERSON_NOT_FOUND", ["The requested person does not exist in the current tenant."]);
        }

        PersonEmployment entity;
        try
        {
            entity = PersonEmployment.Create(
                companyId,
                request.PersonId,
                request.EmployerName,
                request.JobTitle,
                request.StartDate);

            if (request.EndDate.HasValue)
            {
                entity.MarkAsEnded(request.EndDate.Value);
            }
            else if (request.IsCurrent == true)
            {
                await currentCoordinator.ClearOtherCurrentAsync(
                    companyId,
                    request.PersonId,
                    null,
                    cancellationToken);
                entity.SetAsCurrent();
            }
            else
            {
                entity.RemoveCurrent();
            }

            if (request.IsActive == false)
            {
                entity.Deactivate();
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

        await unitOfWork.PersonEmployments.InsertAsync(entity);

        var result = await unitOfWork.SaveAsync(cancellationToken);
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
