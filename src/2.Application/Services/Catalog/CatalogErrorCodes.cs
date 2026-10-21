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

    // SPEC 70 — product images and documents.
    public const string ProductNotActive = "PRODUCT_NOT_ACTIVE";
    public const string ProductAttachmentSettingsNotConfigured = "PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED";
    public const string ProductImageFormatNotAllowed = "PRODUCT_IMAGE_FORMAT_NOT_ALLOWED";
    public const string ProductImageTooLarge = "PRODUCT_IMAGE_TOO_LARGE";
    public const string ProductImageLimit = "PRODUCT_IMAGE_LIMIT";
    public const string ProductImageNotFound = "PRODUCT_IMAGE_NOT_FOUND";
    public const string InvalidProductImageOrder = "INVALID_PRODUCT_IMAGE_ORDER";
    public const string ProductDocumentFileTypeNotAllowed = "PRODUCT_DOCUMENT_FILE_TYPE_NOT_ALLOWED";
    public const string ProductDocumentTooLarge = "PRODUCT_DOCUMENT_TOO_LARGE";
    public const string ProductDocumentLimit = "PRODUCT_DOCUMENT_LIMIT";
    public const string ProductDocumentNotFound = "PRODUCT_DOCUMENT_NOT_FOUND";
    public const string ProductDocumentTypeNotFound = "PRODUCT_DOCUMENT_TYPE_NOT_FOUND";
    public const string ProductDocumentTypeInUse = "PRODUCT_DOCUMENT_TYPE_IN_USE";
    public const string ProductDocumentTypeAlreadyExists = "PRODUCT_DOCUMENT_TYPE_ALREADY_EXISTS";
    public const string ProductAttachmentSettingsAlreadyExists = "PRODUCT_ATTACHMENT_SETTINGS_ALREADY_EXISTS";
    public const string StorageProviderNotConfigured = "STORAGE_PROVIDER_NOT_CONFIGURED";
    public const string FileNotFoundInStorage = "FILE_NOT_FOUND_IN_STORAGE";
}
