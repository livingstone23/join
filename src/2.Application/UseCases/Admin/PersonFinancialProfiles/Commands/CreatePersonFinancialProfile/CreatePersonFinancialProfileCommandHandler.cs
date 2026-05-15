using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Handles person financial profile creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreatePersonFinancialProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<CreatePersonFinancialProfileCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a person financial profile associated with the authenticated tenant company.
    /// </summary>
    /// <param name="request">The create-financial-profile command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the created financial profile identifier.</returns>
    public async Task<Response<Guid>> Handle(CreatePersonFinancialProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;

        var personRepository = _unitOfWork.GetRepository<Person>();
        var person = await personRepository.GetAsync(request.PersonId);

        if (person is null || person.CompanyId != companyId || person.GcRecord != 0)
        {
            return Response<Guid>.Error("PERSON_NOT_FOUND", ["The requested person does not exist in the current tenant."]);
        }

        var incomeRange = await _unitOfWork.GetRepository<IncomeRange>().GetAsync(request.IncomeRangeId);
        if (incomeRange is null || incomeRange.CompanyId != companyId || incomeRange.GcRecord != 0)
        {
            return Response<Guid>.Error(
                "INVALID_REFERENCES",
                [$"IncomeRangeId '{request.IncomeRangeId}' does not exist in the current tenant."]);
        }

        var entity = new PersonFinancialProfile
        {
            PersonId = request.PersonId,
            IncomeRangeId = request.IncomeRangeId,
            SourceOfFunds = request.SourceOfFunds.Trim(),
            DeclaredDate = request.DeclaredDate,
            CompanyId = companyId
        };

        if (request.IsCurrent == false)
        {
            entity.Archive();
        }

        if (request.IsActive == false)
        {
            entity.Deactivate();
        }

        var profileRepository = _unitOfWork.GetRepository<PersonFinancialProfile>();
        await profileRepository.InsertAsync(entity);

        var result = await _unitOfWork.SaveAsync(cancellationToken);
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
