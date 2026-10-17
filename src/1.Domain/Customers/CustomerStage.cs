using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers;

/// <summary>
/// A stage of the company's commercial funnel (SPEC 55, part F). Each company manages its own catalog and has
/// exactly one active default stage, used when a customer is created without an explicit stage.
/// </summary>
public class CustomerStage : BaseTenantEntity
{
    /// <summary>Post-sale stages every company starts with (SPEC 63: the funnel lives in the leads).</summary>
    /// <summary>Funnel stages retired by SPEC 63; only the data migration still reads them.</summary>
    public static class LegacyCodes
    {
        public const string Lead = "LEAD";
        public const string Prospect = "PROSPECT";
    }

    public static class Codes
    {
        public const string Customer = "CUSTOMER";
        public const string Recurring = "RECURRING";
        public const string Former = "FORMER";
    }

    /// <summary>Upper case, no spaces; unique per company among live rows.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static CustomerStage Create(
        Guid companyId,
        string code,
        string name,
        string? description,
        int sortOrder,
        bool isDefault,
        bool isActive = true)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId is required.", nameof(companyId));

        var stage = new CustomerStage { CompanyId = companyId, GcRecord = ActiveGcRecord };
        stage.Update(code, name, description, sortOrder);
        stage.IsDefault = isDefault;
        stage.IsActive = isActive;
        return stage;
    }

    public void Update(string code, string name, string? description, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Code = NormalizeCode(code);
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SortOrder = sortOrder;
    }

    public void MarkAsDefault() => IsDefault = true;

    public void UnmarkAsDefault() => IsDefault = false;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Upper case without spaces, as stored in <see cref="Code"/>.</summary>
    public static string NormalizeCode(string code) =>
        string.Concat(code.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
