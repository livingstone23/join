using System.Reflection;
using JOIN.Domain.Audit;

namespace JOIN.Application.UnitTest.Common.TestDoubles;

/// <summary>
/// Reflection helper to set the protected <see cref="BaseEntity.Id"/> on entities instantiated in tests.
/// The setter on <see cref="BaseEntity"/> is <c>protected</c> to keep it inside the entity, but tests need
/// a known id to wire mocks and assertions; reflection bypasses the accessibility without changing
/// production code.
/// </summary>
public static class EntityIdSetter
{
    /// <summary>
    /// Sets the <c>Id</c> of <paramref name="entity"/> to <paramref name="id"/>.
    /// </summary>
    public static void SetId(object entity, Guid id)
    {
        typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.Id), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(entity, id);
    }
}
