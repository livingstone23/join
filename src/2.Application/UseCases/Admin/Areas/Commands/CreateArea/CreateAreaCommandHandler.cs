using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Handles area creation commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateAreaCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateAreaCommand, Response<AreaDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a new area for the specified tenant.
    /// </summary>
    /// <param name="request">The creation payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the create operation.</returns>
    public async Task<Response<AreaDto>> Handle(CreateAreaCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<AreaDto>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var statusRepository = _unitOfWork.GetRepository<EntityStatus>();
        var areaRepository = _unitOfWork.GetRepository<Area>();

        var company = await companyRepository.GetAsync(request.CompanyId);
        if (company is null)
        {
            return Response<AreaDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var status = await statusRepository.GetAsync(request.EntityStatusId);
        if (status is null)
        {
            return Response<AreaDto>.Error("AREA_STATUS_NOT_FOUND", ["The specified EntityStatusId does not exist."]);
        }

        var normalizedName = request.Name.Trim();
        var areas = await areaRepository.GetAllAsync();
        var nameInUse = areas.Any(area =>
            area.CompanyId == request.CompanyId
            && area.GcRecord == 0
            && string.Equals(area.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<AreaDto>.Error("AREA_NAME_IN_USE", ["Another active area already uses the same name in this company."]);
        }

        var entity = new Area
        {
            CompanyId = request.CompanyId,
            Name = normalizedName,
            EntityStatusId = request.EntityStatusId
        };

        await areaRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<AreaDto>.Error("CREATE_FAILED", ["No records were affected while creating the area."]);
        }

        return new Response<AreaDto>
        {
            IsSuccess = true,
            Message = "Area created successfully.",
            Data = new AreaDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company.Name,
                Name = entity.Name,
                EntityStatusId = entity.EntityStatusId,
                EntityStatusName = status.Name,
                Created = entity.Created
            }
        };
    }
}
