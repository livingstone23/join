using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;



/// <summary>
/// Handles person contact creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreatePersonContactCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<CreatePersonContactCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a person contact associated with the authenticated tenant company.
    /// </summary>
    /// <param name="request">The create-contact command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the created contact identifier.</returns>
    public async Task<Response<Guid>> Handle(CreatePersonContactCommand request, CancellationToken cancellationToken)
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

        var entity = new PersonContact
        {
            PersonId = request.PersonId,
            ContactType = request.ContactType,
            ContactValue = request.ContactValue.Trim(),
            IsPrimary = request.IsPrimary,
            Comments = request.Comments?.Trim(),
            CompanyId = currentUserService.CompanyId
        };

        var contactRepository = _unitOfWork.GetRepository<PersonContact>();
        await contactRepository.InsertAsync(entity);

        var result = await _unitOfWork.SaveAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("CONTACT_CREATE_FAILED", ["The person contact could not be created due to a persistence error."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person contact created successfully.",
            Data = entity.Id
        };
    }
}
