using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Handles customer creation with server-generated customer code.
/// </summary>
public sealed class CreateCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICustomerCodeGenerator customerCodeGenerator) : IRequestHandler<CreateCustomerCommand, Response<CustomerResponseDto>>
{
    public async Task<Response<CustomerResponseDto>> Handle(
        CreateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CustomerResponseDto>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var tenantId = currentUserService.CompanyId;
        var companyRepository = unitOfWork.GetRepository<Company>();
        var personRepository = unitOfWork.GetRepository<Person>();
        var userRepository = unitOfWork.GetRepository<ApplicationUser>();
        var userCompanyRepository = unitOfWork.GetRepository<UserCompany>();
        var customerRepository = unitOfWork.GetRepository<Customer>();

        var company = await companyRepository.GetAsync(tenantId);
        if (company is null)
        {
            return Response<CustomerResponseDto>.Error(
                "INVALID_COMPANY",
                ["The provided company does not exist or is inactive."]);
        }

        var person = await personRepository.GetAsync(request.PersonId);
        if (person is null || person.CompanyId != tenantId)
        {
            return Response<CustomerResponseDto>.Error(
                "INVALID_PERSON",
                ["Person not found for the current company."]);
        }

        var user = await userRepository.GetAsync(request.UserId);
        if (user is null)
        {
            return Response<CustomerResponseDto>.Error(
                "INVALID_USER",
                ["User not found."]);
        }

        var userCompanies = await userCompanyRepository.GetAllAsync();
        var userLinkedToTenant = userCompanies.Any(uc =>
            uc.UserId == request.UserId
            && uc.CompanyId == tenantId
            && uc.GcRecord == 0);

        if (!userLinkedToTenant)
        {
            return Response<CustomerResponseDto>.Error(
                "INVALID_USER",
                ["User is not linked to the current company."]);
        }

        var customers = await customerRepository.GetAllAsync();
        var linkExists = customers.Any(c =>
            c.CompanyId == tenantId
            && c.PersonId == request.PersonId
            && c.UserId == request.UserId);

        if (linkExists)
        {
            return Response<CustomerResponseDto>.Error(
                "CUSTOMER_LINK_EXISTS",
                ["A customer record already exists for this person and user in the current company."]);
        }

        var customerCode = await customerCodeGenerator.GenerateNextAsync(tenantId, cancellationToken);
        var entity = Customer.Create(
            tenantId,
            request.PersonId,
            request.UserId,
            customerCode,
            request.PersonLifecycleStage);

        await customerRepository.InsertAsync(entity);
        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<CustomerResponseDto>.Error(
                "CREATE_FAILED",
                ["No records were affected while creating the customer."]);
        }

        return new Response<CustomerResponseDto>
        {
            IsSuccess = true,
            Message = "Customer created successfully.",
            Data = MapToDto(entity, person, user.Email ?? string.Empty)
        };
    }

    private static CustomerResponseDto MapToDto(Customer entity, Person person, string userEmail) =>
        new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            PersonId = entity.PersonId,
            UserId = entity.UserId,
            CustomerCode = entity.CustomerCode,
            PersonLifecycleStage = (int)entity.PersonLifecycleStage,
            PersonLifecycleStageName = entity.PersonLifecycleStage.GetDisplayName(),
            PersonName = BuildPersonName(person),
            UserEmail = userEmail,
            IsActive = entity.IsActive,
            ActivatedAt = entity.ActivatedAt,
            DeactivatedAt = entity.DeactivatedAt,
            CreatedAt = entity.Created
        };

    private static string BuildPersonName(Person person)
    {
        if (person.PersonType == Domain.Enums.PersonType.Legal)
        {
            return person.CommercialName ?? person.FirstName;
        }

        return string.Join(
            ' ',
            new[] { person.FirstName, person.MiddleName, person.LastName, person.SecondLastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
