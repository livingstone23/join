


using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Queries;



/// <summary>
/// Handles the GetCustomerByIdQuery using high-performance read operations.
/// </summary>
public class GetCustomerByIdQueryHandler : IRequestHandler<GetCustomerByIdQuery, Response<CustomerDto>>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetCustomerByIdQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Response<CustomerDto>> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        
        var response = new Response<CustomerDto>();

        // 1. Fetch data including identification type for enriched response payload.
        var customer = await _unitOfWork.Customers.GetByIdWithIdentificationTypeAsync(request.CustomerId);

        if (customer == null)
        {
            response.IsSuccess = false;
            response.Message = "Customer not found.";
            return response;
        }

        // 2. Map Entity to DTO using Mapperly (Source Generators, zero reflection)
        var mapper = new CustomerMapper();
        var customerDto = mapper.ToDto(customer);

        response.Data = customerDto with
        {
            IdentificationTypeName = customer.IdentificationType?.Name,
            Addresses = customerDto.Addresses is { Count: > 0 } ? customerDto.Addresses : null
        };
        response.IsSuccess = true;
        response.Message = "Customer retrieved successfully.";

        return response;
    }
}
