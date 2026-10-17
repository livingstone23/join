using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.SalesTeams;

namespace JOIN.Application.UseCases.Customers.SalesTeams.Teams.Commands;

/// <summary><c>POST /SalesTeams</c> (SPEC 60).</summary>
public sealed record CreateSalesTeamCommand : ITransactionalCommand<Response<SalesTeamDto>>
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary><c>PUT /SalesTeams/{id}</c> (SPEC 60): an inactive team is not chosen for new leads.</summary>
public sealed record UpdateSalesTeamCommand : ITransactionalCommand<Response<SalesTeamDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary><c>DELETE /SalesTeams/{id}</c>: not while it has current memberships (<c>SALES_TEAM_IN_USE</c>).</summary>
public sealed record DeleteSalesTeamCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /SalesTeams/{id}/restore</c> (SPEC 41-post, SuperAdmin).</summary>
public sealed record RestoreSalesTeamCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;

/// <summary>Validates <see cref="CreateSalesTeamCommand"/>.</summary>
public sealed class CreateSalesTeamCommandValidator : AbstractValidator<CreateSalesTeamCommand>
{
    public CreateSalesTeamCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

/// <summary>Validates <see cref="UpdateSalesTeamCommand"/>.</summary>
public sealed class UpdateSalesTeamCommandValidator : AbstractValidator<UpdateSalesTeamCommand>
{
    public UpdateSalesTeamCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
