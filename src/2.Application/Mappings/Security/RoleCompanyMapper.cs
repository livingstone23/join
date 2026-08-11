// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.DTO.Security.RoleCompany;
using Riok.Mapperly.Abstractions;

namespace JOIN.Application.Mappings.Security;

/// <summary>
/// Mapperly contract for converting <see cref="Domain.Security.RoleCompany"/> entities to their DTO projections.
/// Read paths return Dapper-backed DTOs directly; the FromEntity/ToListItem methods are kept for
/// cases where a tracked entity is mapped after EF writes (RoleName/IsSystemDefault are
/// intentionally not mapped from the entity — they live on the Role navigation and must be
/// supplied by the caller after re-reading).
/// </summary>
public interface IRoleCompanyMapper
{
    [MapperIgnoreSource(nameof(Domain.Security.RoleCompany.Role))]
    [MapperIgnoreSource(nameof(Domain.Security.RoleCompany.Company))]
    [MapperIgnoreTarget(nameof(RoleCompanyDto.RoleName))]
    [MapperIgnoreTarget(nameof(RoleCompanyDto.IsSystemDefault))]
    RoleCompanyDto FromEntity(Domain.Security.RoleCompany entity);

    [MapperIgnoreSource(nameof(Domain.Security.RoleCompany.Role))]
    [MapperIgnoreSource(nameof(Domain.Security.RoleCompany.Company))]
    [MapperIgnoreTarget(nameof(RoleCompanyListItemDto.RoleName))]
    [MapperIgnoreTarget(nameof(RoleCompanyListItemDto.IsSystemDefault))]
    RoleCompanyListItemDto ToListItem(Domain.Security.RoleCompany entity);
}

/// <summary>
/// Mapperly source-generated implementation for <see cref="IRoleCompanyMapper"/>.
/// </summary>
[Mapper]
public partial class RoleCompanyMapper : IRoleCompanyMapper
{
    public partial RoleCompanyDto FromEntity(Domain.Security.RoleCompany entity);

    public partial RoleCompanyListItemDto ToListItem(Domain.Security.RoleCompany entity);
}
