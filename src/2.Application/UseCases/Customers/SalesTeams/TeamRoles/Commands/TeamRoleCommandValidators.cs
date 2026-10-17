using FluentValidation;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Teams;

namespace JOIN.Application.UseCases.Customers.SalesTeams.TeamRoles.Commands;

internal static class TeamRoleInputRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : ITeamRoleInput
    {
        CommercialCatalogInputRules.Apply(validator);
        validator.RuleFor(x => x.LeadScope)
            .Must(v => Enum.IsDefined(typeof(LeadScope), v))
            .WithMessage("LeadScope must be 1 (Own), 2 (Team) or 3 (Company).");
    }
}

/// <summary>Validates <see cref="CreateTeamRoleCommand"/>.</summary>
public sealed class CreateTeamRoleCommandValidator : AbstractValidator<CreateTeamRoleCommand>
{
    public CreateTeamRoleCommandValidator() => TeamRoleInputRules.Apply(this);
}

/// <summary>Validates <see cref="UpdateTeamRoleCommand"/>.</summary>
public sealed class UpdateTeamRoleCommandValidator : AbstractValidator<UpdateTeamRoleCommand>
{
    public UpdateTeamRoleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        TeamRoleInputRules.Apply(this);
    }
}
