using System.Reflection;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Application.UnitTest.Security.RoleCompanies.TestHelpers;

/// <summary>
/// Test factory for <see cref="RoleCompany"/> instances.
/// The base <c>BaseEntity.Id</c> setter is <c>protected</c>, so tests that need a deterministic Id
/// use reflection via this helper.
/// </summary>
internal static class RoleCompanyTestFactory
{
    public static RoleCompany Create(Guid id, Guid roleId, Guid companyId, int gcRecord = 0)
    {
        var entity = new RoleCompany
        {
            RoleId = roleId,
            CompanyId = companyId,
            GcRecord = gcRecord
        };
        SetId(entity, id);
        return entity;
    }

    private static void SetId(BaseEntity entity, Guid id)
    {
        // BaseEntity.Id has a protected setter; tests reach it via reflection.
        var prop = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   ?? throw new InvalidOperationException("BaseEntity.Id property not found.");
        prop.SetValue(entity, id);
    }
}
