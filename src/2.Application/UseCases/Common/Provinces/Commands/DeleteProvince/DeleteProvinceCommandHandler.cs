using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Provinces.Commands;

/// <summary>
/// Handles soft delete operations for provinces.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteProvinceCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteProvinceCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the province as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted province id.</returns>
    public async Task<Response<Guid>> Handle(DeleteProvinceCommand request, CancellationToken cancellationToken)
    {
        var provinceRepository = _unitOfWork.GetRepository<Province>();
        var municipalityRepository = _unitOfWork.GetRepository<Municipality>();
        var customerAddressRepository = _unitOfWork.GetRepository<CustomerAddress>();

        var provinceEntity = await provinceRepository.GetAsync(request.Id);
        if (provinceEntity is null)
        {
            return Response<Guid>.Error("PROVINCE_NOT_FOUND", ["Province not found."]);
        }

        var municipalities = await municipalityRepository.GetAllAsync();
        var customerAddresses = await customerAddressRepository.GetAllAsync();
        var isInUse = municipalities.Any(m => m.GcRecord == 0 && m.ProvinceId == request.Id)
                    || customerAddresses.Any(address => address.GcRecord == 0 && address.ProvinceId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error("PROVINCE_IN_USE", ["The province is currently linked to municipalities or customer addresses and cannot be deleted."]);
        }

        provinceEntity.MarkAsDeleted();

        await provinceRepository.UpdateAsync(provinceEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the province."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Province deleted successfully.",
            Data = provinceEntity.Id
        };
    }
}