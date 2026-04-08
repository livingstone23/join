using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Handles project update commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class UpdateProjectCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateProjectCommand, Response<ProjectDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates an existing tenant-scoped project.
    /// </summary>
    /// <param name="request">The update payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the update operation.</returns>
    public async Task<Response<ProjectDto>> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<ProjectDto>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var projectRepository = _unitOfWork.GetRepository<Project>();
        var statusRepository = _unitOfWork.GetRepository<EntityStatus>();

        var company = await companyRepository.GetAsync(request.CompanyId);
        if (company is null)
        {
            return Response<ProjectDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var entity = await projectRepository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != request.CompanyId || entity.GcRecord != 0)
        {
            return Response<ProjectDto>.Error("PROJECT_NOT_FOUND", ["Project not found."]);
        }

        var status = await statusRepository.GetAsync(request.EntityStatusId);
        if (status is null)
        {
            return Response<ProjectDto>.Error("PROJECT_STATUS_NOT_FOUND", ["The specified EntityStatusId does not exist."]);
        }

        var normalizedName = request.Name.Trim();
        var projects = await projectRepository.GetAllAsync();
        var nameInUse = projects.Any(project =>
            project.Id != request.Id
            && project.CompanyId == request.CompanyId
            && project.GcRecord == 0
            && string.Equals(project.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<ProjectDto>.Error("PROJECT_NAME_IN_USE", ["Another active project already uses the same name in this company."]);
        }

        entity.Name = normalizedName;
        entity.EntityStatusId = request.EntityStatusId;

        await projectRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<ProjectDto>.Error("UPDATE_FAILED", ["No records were affected while updating the project."]);
        }

        return new Response<ProjectDto>
        {
            IsSuccess = true,
            Message = "Project updated successfully.",
            Data = new ProjectDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company.Name,
                Name = entity.Name,
                EntityStatusId = entity.EntityStatusId,
                EntityStatusName = status.Name,
                CreatedAt = entity.Created
            }
        };
    }
}