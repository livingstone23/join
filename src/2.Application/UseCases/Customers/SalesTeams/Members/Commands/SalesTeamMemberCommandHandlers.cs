using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers.Teams;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Customers.SalesTeams.Members.Commands;

/// <summary>Lookups and mapping shared by the membership commands.</summary>
public sealed class SalesTeamMemberContext(IUnitOfWork unitOfWork)
{
    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(SalesTeamErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static Response<T> MemberNotFound<T>() =>
        Response<T>.Error(SalesTeamErrorCodes.SalesTeamMemberNotFound, ["Open sales team membership not found."]);

    internal async Task<SalesTeam?> TeamAsync(Guid companyId, Guid teamId)
    {
        var team = await unitOfWork.GetRepository<SalesTeam>().GetAsync(teamId);
        return team is not null && team.CompanyId == companyId && team.GcRecord == 0 ? team : null;
    }

    internal async Task<TeamRole?> ActiveRoleAsync(Guid companyId, Guid roleId)
    {
        var role = await unitOfWork.GetRepository<TeamRole>().GetAsync(roleId);
        return role is not null && role.CompanyId == companyId && role.GcRecord == 0 && role.IsActive ? role : null;
    }

    /// <summary>Open membership <paramref name="memberId"/> of <paramref name="teamId"/> (tracked).</summary>
    internal async Task<SalesTeamMember?> OpenMemberAsync(Guid companyId, Guid teamId, Guid memberId)
    {
        var member = await unitOfWork.GetRepository<SalesTeamMember>().GetAsync(memberId);
        return member is not null && member.CompanyId == companyId && member.SalesTeamId == teamId && member.GcRecord == 0 && member.ValidTo is null
            ? member
            : null;
    }

    /// <summary>SPEC 28 membership: a live <c>UserCompany</c> row and an active user.</summary>
    internal async Task<ApplicationUser?> CompanyUserAsync(Guid companyId, Guid userId)
    {
        var isMember = (await unitOfWork.GetRepository<UserCompany>().GetAllIncludingDeletedAsync(uc =>
                uc.CompanyId == companyId && uc.UserId == userId && uc.GcRecord == 0))
            .Any();
        if (!isMember)
        {
            return null;
        }

        var user = (await unitOfWork.GetRepository<ApplicationUser>().GetAllIncludingDeletedAsync(u => u.Id == userId)).FirstOrDefault();
        return user is { IsActive: true } ? user : null;
    }

    internal static SalesTeamMemberDto ToDto(SalesTeamMember member, SalesTeam team, TeamRole role, ApplicationUser? user)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new SalesTeamMemberDto
        {
            Id = member.Id,
            SalesTeamId = team.Id,
            SalesTeamName = team.Name,
            SalesTeamIsActive = team.IsActive,
            UserId = member.UserId,
            UserName = user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim(),
            Email = user?.Email,
            TeamRoleId = role.Id,
            TeamRoleCode = role.Code,
            TeamRoleName = role.Name,
            LeadScope = (int)role.LeadScope,
            IsLeader = role.IsLeader,
            CanReassign = role.CanReassign,
            IsPrimary = member.IsPrimary,
            ValidFrom = member.ValidFrom,
            ValidTo = member.ValidTo,
            IsCurrent = member.IsCurrentOn(today)
        };
    }

    internal async Task<ApplicationUser?> UserAsync(Guid userId) =>
        (await unitOfWork.GetRepository<ApplicationUser>().GetAllIncludingDeletedAsync(u => u.Id == userId)).FirstOrDefault();
}

/// <summary>Handles <see cref="AddSalesTeamMemberCommand"/>.</summary>
public sealed class AddSalesTeamMemberCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SalesTeamMemberContext context,
    SalesTeamMemberPrimaryCoordinator primary,
    SalesTeamLeaderCoordinator leader)
    : IRequestHandler<AddSalesTeamMemberCommand, Response<SalesTeamMemberDto>>
{
    public async Task<Response<SalesTeamMemberDto>> Handle(AddSalesTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return SalesTeamMemberContext.CompanyRequired<SalesTeamMemberDto>();
        }

        var team = await context.TeamAsync(companyId, request.SalesTeamId);
        if (team is null)
        {
            return Response<SalesTeamMemberDto>.Error(SalesTeamErrorCodes.SalesTeamNotFound, ["Sales team not found."]);
        }

        var role = await context.ActiveRoleAsync(companyId, request.TeamRoleId);
        if (role is null)
        {
            return Response<SalesTeamMemberDto>.Error(SalesTeamErrorCodes.TeamRoleNotFound, ["The team role does not exist in the company or is inactive."]);
        }

        var user = await context.CompanyUserAsync(companyId, request.UserId);
        if (user is null)
        {
            return Response<SalesTeamMemberDto>.Error(SalesTeamErrorCodes.UserNotInCompany, ["The user is not an active member of the company."]);
        }

        var repository = unitOfWork.GetRepository<SalesTeamMember>();
        var alreadyOpen = (await repository.GetAllIncludingDeletedAsync(m =>
                m.SalesTeamId == team.Id && m.UserId == user.Id && m.GcRecord == 0 && m.ValidTo == null))
            .Any();
        if (alreadyOpen)
        {
            return Response<SalesTeamMemberDto>.Error(SalesTeamErrorCodes.DuplicateKey, ["The user already has an open membership in this team."]);
        }

        var member = new SalesTeamMember
        {
            CompanyId = companyId,
            SalesTeamId = team.Id,
            UserId = user.Id,
            TeamRoleId = role.Id,
            ValidFrom = request.ValidFrom ?? DateOnly.FromDateTime(DateTime.UtcNow)
        };

        if (await leader.ApplyAsync<SalesTeamMemberDto>(member, role) is { } leaderError)
        {
            return leaderError;
        }

        await primary.PrepareNewAsync(member, request.IsPrimary, cancellationToken);
        await repository.InsertAsync(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<SalesTeamMemberDto>
        {
            IsSuccess = true,
            Message = "Sales team member added successfully.",
            Data = SalesTeamMemberContext.ToDto(member, team, role, user)
        };
    }
}

/// <summary>Handles <see cref="UpdateSalesTeamMemberCommand"/>.</summary>
public sealed class UpdateSalesTeamMemberCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SalesTeamMemberContext context,
    SalesTeamMemberPrimaryCoordinator primary,
    SalesTeamLeaderCoordinator leader)
    : IRequestHandler<UpdateSalesTeamMemberCommand, Response<SalesTeamMemberDto>>
{
    public async Task<Response<SalesTeamMemberDto>> Handle(UpdateSalesTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return SalesTeamMemberContext.CompanyRequired<SalesTeamMemberDto>();
        }

        var team = await context.TeamAsync(companyId, request.SalesTeamId);
        var member = team is null ? null : await context.OpenMemberAsync(companyId, team.Id, request.MemberId);
        if (team is null || member is null)
        {
            return SalesTeamMemberContext.MemberNotFound<SalesTeamMemberDto>();
        }

        var role = await context.ActiveRoleAsync(companyId, request.TeamRoleId);
        if (role is null)
        {
            return Response<SalesTeamMemberDto>.Error(SalesTeamErrorCodes.TeamRoleNotFound, ["The team role does not exist in the company or is inactive."]);
        }

        if (await leader.ApplyAsync<SalesTeamMemberDto>(member, role) is { } leaderError)
        {
            return leaderError;
        }

        member.TeamRoleId = role.Id;
        if (request.IsPrimary)
        {
            await primary.MakePrimaryAsync(member, cancellationToken);
        }

        await unitOfWork.GetRepository<SalesTeamMember>().UpdateAsync(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<SalesTeamMemberDto>
        {
            IsSuccess = true,
            Message = "Sales team member updated successfully.",
            Data = SalesTeamMemberContext.ToDto(member, team, role, await context.UserAsync(member.UserId))
        };
    }
}

/// <summary>Handles <see cref="CloseSalesTeamMemberCommand"/>.</summary>
public sealed class CloseSalesTeamMemberCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SalesTeamMemberContext context,
    SalesTeamMemberPrimaryCoordinator primary)
    : IRequestHandler<CloseSalesTeamMemberCommand, Response<SalesTeamMemberDto>>
{
    public async Task<Response<SalesTeamMemberDto>> Handle(CloseSalesTeamMemberCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return SalesTeamMemberContext.CompanyRequired<SalesTeamMemberDto>();
        }

        var team = await context.TeamAsync(companyId, request.SalesTeamId);
        var member = team is null ? null : await context.OpenMemberAsync(companyId, team.Id, request.MemberId);
        if (team is null || member is null)
        {
            return SalesTeamMemberContext.MemberNotFound<SalesTeamMemberDto>();
        }

        var validTo = request.ValidTo ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (validTo < member.ValidFrom)
        {
            return Response<SalesTeamMemberDto>.Error(SalesTeamErrorCodes.MemberDatesInvalid, ["ValidTo cannot be earlier than ValidFrom."]);
        }

        member.ValidTo = validTo;
        await primary.OnClosedAsync(member, cancellationToken);
        await unitOfWork.GetRepository<SalesTeamMember>().UpdateAsync(member);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var role = (await unitOfWork.GetRepository<TeamRole>().GetAllIncludingDeletedAsync(r => r.Id == member.TeamRoleId)).First();
        return new Response<SalesTeamMemberDto>
        {
            IsSuccess = true,
            Message = "Sales team membership closed successfully.",
            Data = SalesTeamMemberContext.ToDto(member, team, role, await context.UserAsync(member.UserId))
        };
    }
}
