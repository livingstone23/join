using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;

namespace JOIN.Application.UseCases.Customers.SalesTeams.Members.Commands;

/// <summary>
/// <c>POST /SalesTeams/{id}/members</c> (SPEC 60): adds a user of the company to the team with a management role.
/// <c>ValidFrom</c> defaults to today; the user's first open membership becomes primary.
/// </summary>
public sealed record AddSalesTeamMemberCommand : ITransactionalCommand<Response<SalesTeamMemberDto>>
{
    public Guid SalesTeamId { get; init; }
    public Guid UserId { get; init; }
    public Guid TeamRoleId { get; init; }
    public DateOnly? ValidFrom { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>
/// <c>PUT /SalesTeams/{id}/members/{memberId}</c> (SPEC 60): role and primary flag of an open membership.
/// <c>IsPrimary = false</c> on the primary membership is ignored: the flag moves by marking another membership.
/// </summary>
public sealed record UpdateSalesTeamMemberCommand : ITransactionalCommand<Response<SalesTeamMemberDto>>
{
    public Guid SalesTeamId { get; init; }
    public Guid MemberId { get; init; }
    public Guid TeamRoleId { get; init; }
    public bool IsPrimary { get; init; }
}

/// <summary>
/// <c>POST /SalesTeams/{id}/members/{memberId}/close</c> (SPEC 60): ends an open membership on <c>ValidTo</c> (default
/// today). The row is never deleted.
/// </summary>
public sealed record CloseSalesTeamMemberCommand : ITransactionalCommand<Response<SalesTeamMemberDto>>
{
    public Guid SalesTeamId { get; init; }
    public Guid MemberId { get; init; }
    public DateOnly? ValidTo { get; init; }
}

/// <summary>Validates <see cref="AddSalesTeamMemberCommand"/>.</summary>
public sealed class AddSalesTeamMemberCommandValidator : AbstractValidator<AddSalesTeamMemberCommand>
{
    public AddSalesTeamMemberCommandValidator()
    {
        RuleFor(x => x.SalesTeamId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.TeamRoleId).NotEmpty();
    }
}

/// <summary>Validates <see cref="UpdateSalesTeamMemberCommand"/>.</summary>
public sealed class UpdateSalesTeamMemberCommandValidator : AbstractValidator<UpdateSalesTeamMemberCommand>
{
    public UpdateSalesTeamMemberCommandValidator()
    {
        RuleFor(x => x.SalesTeamId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.TeamRoleId).NotEmpty();
    }
}

/// <summary>Validates <see cref="CloseSalesTeamMemberCommand"/>.</summary>
public sealed class CloseSalesTeamMemberCommandValidator : AbstractValidator<CloseSalesTeamMemberCommand>
{
    public CloseSalesTeamMemberCommandValidator()
    {
        RuleFor(x => x.SalesTeamId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
    }
}
