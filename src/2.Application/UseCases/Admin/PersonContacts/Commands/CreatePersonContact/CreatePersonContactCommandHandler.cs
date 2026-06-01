using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonContacts;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;

/// <summary>
/// Handles person contact creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreatePersonContactCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonContactPrimaryCoordinator primaryCoordinator) : IRequestHandler<CreatePersonContactCommand, Response<Guid>>
{
    /// <summary>
    /// Creates a person contact associated with the authenticated tenant company.
    /// </summary>
    public async Task<Response<Guid>> Handle(CreatePersonContactCommand request, CancellationToken cancellationToken)
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

        PersonContact entity;
        try
        {
            entity = PersonContact.Create(
                companyId,
                request.PersonId,
                request.ContactType,
                request.ContactValue,
                request.Comments);

            if (request.IsPrimary)
            {
                await primaryCoordinator.ClearOtherPrimariesAsync(
                    companyId,
                    request.PersonId,
                    request.ContactType,
                    null,
                    cancellationToken);
                entity.SetAsPrimary();
            }
            else
            {
                entity.RemovePrimary();
            }
        }
        catch (ArgumentException ex)
        {
            return Response<Guid>.Error("INVALID_CONTACT_DATA", [ex.Message]);
        }
        catch (InvalidOperationException ex)
        {
            return Response<Guid>.Error("INVALID_CONTACT_PRIMARY", [ex.Message]);
        }

        await unitOfWork.PersonContacts.InsertAsync(entity);

        var result = await unitOfWork.SaveAsync(cancellationToken);
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
