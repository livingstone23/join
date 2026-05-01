using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Handles tenant-scoped retrieval of a single RoleSystemOption rule.
/// </summary>
public sealed class GetRoleSystemOptionByIdQueryHandler(
    IRoleSystemOptionsRepository repository,
    ICurrentUserService currentUserService,
    IRoleSystemOptionMapper mapper)
    : IRequestHandler<GetRoleSystemOptionByIdQuery, Response<RoleSystemOptionDto>>
{
    public async Task<Response<RoleSystemOptionDto>> Handle(GetRoleSystemOptionByIdQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RoleSystemOptionDto>.Error("INVALID_COMPANY_ID", ["A valid company context is required."]);
        }

        var readModel = await repository.GetWithNamesAsync(request.Id, companyId);
        if (readModel is null)
        {
            return Response<RoleSystemOptionDto>.Error("ROLE_SYSTEM_OPTION_NOT_FOUND", ["Role system option not found."]);
        }

        return new Response<RoleSystemOptionDto>
        {
            IsSuccess = true,
            Message = "Role system option retrieved successfully.",
            Data = mapper.ToDto(readModel)
        };
    }
}
