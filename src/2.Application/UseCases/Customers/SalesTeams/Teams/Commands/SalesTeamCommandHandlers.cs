using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers.Teams;
using MediatR;

namespace JOIN.Application.UseCases.Customers.SalesTeams.Teams.Commands;

/// <summary>Rules shared by the sales team commands.</summary>
internal static class SalesTeamRules
{
    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(SalesTeamErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static Response<T> NotFound<T>() => Response<T>.Error(SalesTeamErrorCodes.SalesTeamNotFound, ["Sales team not found."]);

    internal static async Task<bool> NameTakenAsync(IUnitOfWork unitOfWork, Guid companyId, string name, Guid? exceptId)
    {
        var lowered = name.ToLower();
        return (await unitOfWork.GetRepository<SalesTeam>().GetAllIncludingDeletedAsync(t =>
                t.CompanyId == companyId && t.GcRecord == 0 && t.Id != exceptId && t.Name.ToLower() == lowered))
            .Any();
    }

    internal static Response<SalesTeamDto> Duplicate() =>
        Response<SalesTeamDto>.Error(SalesTeamErrorCodes.DuplicateKey, ["Another sales team of the company already uses the same name."]);

    internal static SalesTeamDto ToDto(SalesTeam entity) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Name = entity.Name,
        Description = entity.Description,
        IsActive = entity.IsActive,
        CreatedAt = entity.Created
    };

    internal static void Apply(SalesTeam entity, string name, string? description, bool isActive)
    {
        entity.Name = name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        entity.IsActive = isActive;
    }
}

/// <summary>Handles <see cref="CreateSalesTeamCommand"/>.</summary>
public sealed class CreateSalesTeamCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<CreateSalesTeamCommand, Response<SalesTeamDto>>
{
    public async Task<Response<SalesTeamDto>> Handle(CreateSalesTeamCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return SalesTeamRules.CompanyRequired<SalesTeamDto>();
        }

        if (await SalesTeamRules.NameTakenAsync(unitOfWork, currentUserService.CompanyId, request.Name.Trim(), null))
        {
            return SalesTeamRules.Duplicate();
        }

        var entity = new SalesTeam { CompanyId = currentUserService.CompanyId };
        SalesTeamRules.Apply(entity, request.Name, request.Description, request.IsActive);
        await unitOfWork.GetRepository<SalesTeam>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<SalesTeamDto> { IsSuccess = true, Message = "Sales team created successfully.", Data = SalesTeamRules.ToDto(entity) };
    }
}

/// <summary>Handles <see cref="UpdateSalesTeamCommand"/>.</summary>
public sealed class UpdateSalesTeamCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<UpdateSalesTeamCommand, Response<SalesTeamDto>>
{
    public async Task<Response<SalesTeamDto>> Handle(UpdateSalesTeamCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return SalesTeamRules.CompanyRequired<SalesTeamDto>();
        }

        var repository = unitOfWork.GetRepository<SalesTeam>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0)
        {
            return SalesTeamRules.NotFound<SalesTeamDto>();
        }

        if (await SalesTeamRules.NameTakenAsync(unitOfWork, entity.CompanyId, request.Name.Trim(), entity.Id))
        {
            return SalesTeamRules.Duplicate();
        }

        SalesTeamRules.Apply(entity, request.Name, request.Description, request.IsActive);
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<SalesTeamDto> { IsSuccess = true, Message = "Sales team updated successfully.", Data = SalesTeamRules.ToDto(entity) };
    }
}

/// <summary>Handles <see cref="DeleteSalesTeamCommand"/>: a team with current memberships is deactivated, not deleted.</summary>
public sealed class DeleteSalesTeamCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<DeleteSalesTeamCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteSalesTeamCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return SalesTeamRules.CompanyRequired<Guid>();
        }

        var repository = unitOfWork.GetRepository<SalesTeam>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0)
        {
            return SalesTeamRules.NotFound<Guid>();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = (await unitOfWork.GetRepository<SalesTeamMember>().GetAllIncludingDeletedAsync(m =>
                m.SalesTeamId == entity.Id && m.GcRecord == 0 && (m.ValidTo == null || m.ValidTo >= today)))
            .Count();
        if (current > 0)
        {
            return Response<Guid>.Error(SalesTeamErrorCodes.SalesTeamInUse, [$"Active SalesTeamMembers: {current}"]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<Guid> { IsSuccess = true, Message = "Sales team deleted successfully.", Data = entity.Id };
    }
}

/// <summary>Handles <see cref="RestoreSalesTeamCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).</summary>
public sealed class RestoreSalesTeamCommandHandler(ICurrentUserService currentUserService, SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreSalesTeamCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreSalesTeamCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return SalesTeamRules.CompanyRequired<Guid>();
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(SalesTeamErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<SalesTeam>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<SalesTeam>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.Name == entity.Name),
            cancellationToken: cancellationToken);
    }
}
