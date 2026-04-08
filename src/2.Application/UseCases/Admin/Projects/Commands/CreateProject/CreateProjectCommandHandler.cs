using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Handles project creation commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateProjectCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateProjectCommand, Response<ProjectDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a new project for the specified tenant.
    /// </summary>
    /// <param name="request">The creation payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the create operation.</returns>
    public async Task<Response<ProjectDto>> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<ProjectDto>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var statusRepository = _unitOfWork.GetRepository<EntityStatus>();
        var projectRepository = _unitOfWork.GetRepository<Project>();

        var company = await companyRepository.GetAsync(request.CompanyId);
        if (company is null)
        {
            return Response<ProjectDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var status = await statusRepository.GetAsync(request.EntityStatusId);
        if (status is null)
        {
            return Response<ProjectDto>.Error("PROJECT_STATUS_NOT_FOUND", ["The specified EntityStatusId does not exist."]);
        }

        var normalizedName = request.Name.Trim();
        var projects = await projectRepository.GetAllAsync();
        var nameInUse = projects.Any(project =>
            project.CompanyId == request.CompanyId
            && project.GcRecord == 0
            && string.Equals(project.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<ProjectDto>.Error("PROJECT_NAME_IN_USE", ["Another active project already uses the same name in this company."]);
        }

        var entity = new Project
        {
            CompanyId = request.CompanyId,
            Name = normalizedName,
            EntityStatusId = request.EntityStatusId
        };

        await projectRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<ProjectDto>.Error("CREATE_FAILED", ["No records were affected while creating the project."]);
        }

        return new Response<ProjectDto>
        {
            IsSuccess = true,
            Message = "Project created successfully.",
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