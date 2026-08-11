// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.CreateRoleCompany;

/// <summary>
/// FluentValidation rules for <see cref="CreateRoleCompanyCommand"/>.
/// Existence and active-state validation live in the handler (queries the role by id).
/// </summary>
public sealed class CreateRoleCompanyCommandValidator : AbstractValidator<CreateRoleCompanyCommand>
{
    public CreateRoleCompanyCommandValidator()
    {
        RuleFor(x => x.RoleId)
            .NotEqual(Guid.Empty)
            .WithMessage("RoleId es requerido.");
    }
}
