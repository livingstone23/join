namespace JOIN.Application.Services.Catalog;

/// <summary>
/// Error codes of the product catalog (SPEC 57).
/// </summary>
public static class CatalogErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string ProductCategoryNotFound = "PRODUCT_CATEGORY_NOT_FOUND";
    public const string ProductCategoryCycle = "PRODUCT_CATEGORY_CYCLE";
    public const string ProductCategoryInUse = "PRODUCT_CATEGORY_IN_USE";
    public const string ProductInUse = "PRODUCT_IN_USE";
    public const string BaseCurrencyRequired = "BASE_CURRENCY_REQUIRED";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string SuperAdminRequired = "SUPERADMIN_REQUIRED";
}
