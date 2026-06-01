using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Handles person financial profile creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreatePersonFinancialProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonFinancialProfileCurrentCoordinator currentCoordinator) : IRequestHandler<CreatePersonFinancialProfileCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(CreatePersonFinancialProfileCommand request, CancellationToken cancellationToken)
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

        var incomeRange = await unitOfWork.GetRepository<IncomeRange>().GetAsync(request.IncomeRangeId);
        if (incomeRange is null || incomeRange.CompanyId != companyId || incomeRange.GcRecord != 0)
        {
            return Response<Guid>.Error(
                "INVALID_REFERENCES",
                [$"IncomeRangeId '{request.IncomeRangeId}' does not exist in the current tenant."]);
        }

        PersonFinancialProfile entity;
        try
        {
            entity = PersonFinancialProfile.Create(
                companyId,
                request.PersonId,
                request.IncomeRangeId,
                request.SourceOfFunds,
                request.DeclaredDate);

            if (request.IsCurrent == false)
            {
                entity.Archive();
            }
            else
            {
                await currentCoordinator.ArchiveOtherCurrentAsync(
                    companyId,
                    request.PersonId,
                    null,
                    cancellationToken);
                entity.SetAsCurrent();
            }

            if (request.IsActive == false)
            {
                entity.Deactivate();
            }
        }
        catch (ArgumentException ex)
        {
            return Response<Guid>.Error("INVALID_FINANCIAL_PROFILE_DATA", [ex.Message]);
        }
        catch (InvalidOperationException ex)
        {
            return Response<Guid>.Error("INVALID_FINANCIAL_PROFILE_CURRENT", [ex.Message]);
        }

        await unitOfWork.PersonFinancialProfiles.InsertAsync(entity);

        var result = await unitOfWork.SaveAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("FINANCIAL_PROFILE_CREATE_FAILED", ["The person financial profile could not be created due to a persistence error."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person financial profile created successfully.",
            Data = entity.Id
        };
    }
}
