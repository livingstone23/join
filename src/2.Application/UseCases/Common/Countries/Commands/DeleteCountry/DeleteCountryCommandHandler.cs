using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Handles soft delete operations for countries.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class DeleteCountryCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCountryCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking GcRecord.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted country id.</returns>
    public async Task<Response<Guid>> Handle(DeleteCountryCommand request, CancellationToken cancellationToken)
    {
        var countryRepository = _unitOfWork.GetRepository<Country>();
        var countryEntity = await countryRepository.GetAsync(request.Id);

        if (countryEntity is null)
        {
            return Response<Guid>.Error("COUNTRY_NOT_FOUND", ["Country not found."]);
        }

        countryEntity.GcRecord = 1;

        await countryRepository.UpdateAsync(countryEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the country."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Country deleted successfully.",
            Data = countryEntity.Id
        };
    }
}
