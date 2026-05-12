using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerAddresses.Commands;

/// <summary>
/// Handles customer address creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreateCustomerAddressCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<CreateCustomerAddressCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a customer address associated with the authenticated tenant company.
    /// </summary>
    /// <param name="request">The create-address command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the created address identifier.</returns>
    public async Task<Response<Guid>> Handle(CreateCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var customerRepository = _unitOfWork.GetRepository<Customer>();
        var customer = await customerRepository.GetAsync(request.CustomerId);

        if (customer is null || customer.CompanyId != currentUserService.CompanyId || customer.GcRecord != 0)
        {
            return Response<Guid>.Error("CUSTOMER_NOT_FOUND", ["The requested customer does not exist in the current tenant."]);
        }

        var entity = new CustomerAddress
        {
            CustomerId = request.CustomerId,
            AddressLine1 = request.AddressLine1.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            ZipCode = request.ZipCode.Trim(),
            StreetTypeId = request.StreetTypeId,
            CountryId = request.CountryId,
            RegionId = request.RegionId,
            ProvinceId = request.ProvinceId,
            MunicipalityId = request.MunicipalityId,
            IsDefault = request.IsDefault,
            CompanyId = currentUserService.CompanyId
        };

        var addressRepository = _unitOfWork.GetRepository<CustomerAddress>();
        await addressRepository.InsertAsync(entity);

        var result = await _unitOfWork.SaveAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("ADDRESS_CREATE_FAILED", ["The customer address could not be created due to a persistence error."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Customer address created successfully.",
            Data = entity.Id
        };
    }
}
