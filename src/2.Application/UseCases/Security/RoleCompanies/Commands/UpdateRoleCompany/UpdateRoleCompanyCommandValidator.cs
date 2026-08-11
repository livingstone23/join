// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.UpdateRoleCompany;

/// <summary>
/// FluentValidation rules for <see cref="UpdateRoleCompanyCommand"/>.
/// Existence and tenant checks live in the handler.
/// </summary>
public sealed class UpdateRoleCompanyCommandValidator : AbstractValidator<UpdateRoleCompanyCommand>
{
    public UpdateRoleCompanyCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty)
            .WithMessage("Id es requerido.");

        RuleFor(x => x.RoleId)
            .NotEqual(Guid.Empty)
            .WithMessage("RoleId es requerido.");
    }
}
