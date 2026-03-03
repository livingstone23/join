


using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Handles the creation of a new Customer, ensuring transactional integrity.
/// </summary>
public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateCustomerCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Response<Guid>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var response = new Response<Guid>();
        var mapper = new CustomerMapper();

        // 1. Map DTO to Entity (Mapperly ignores the Id automatically to let DB/Constructor handle it)
        var customerEntity = mapper.ToEntity(request.CustomerDto);

        // 2. Insert into Context (EF Core Change Tracker picks this up)
        await _unitOfWork.Customers.InsertAsync(customerEntity);

        // 3. Commit Transaction (This triggers the AuditableEntitySaveChangesInterceptor)
        var result = await _unitOfWork.SaveAsync(cancellationToken);

        if (result > 0)
        {
            response.Data = customerEntity.Id;
            response.IsSuccess = true;
            response.Message = "Customer created successfully.";
        }
        else
        {
            response.IsSuccess = false;
            response.Message = "Failed to create the customer due to a database error.";
        }

        return response;
        
    }
}