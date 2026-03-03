


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

        // 1. Fetch data using Dapper (blazing fast, no tracking)
        var customer = await _unitOfWork.Customers.GetAsync(request.CustomerId);

        if (customer == null)
        {
            response.IsSuccess = false;
            response.Message = "Customer not found.";
            return response;
        }

        // 2. Map Entity to DTO using Mapperly (Source Generators, zero reflection)
        var mapper = new CustomerMapper();
        response.Data = mapper.ToDto(customer);
        response.IsSuccess = true;
        response.Message = "Customer retrieved successfully.";

        return response;
    }
}
