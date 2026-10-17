namespace JOIN.Application.Services.Finance;

/// <summary>
/// Error codes of the finance module (SPEC 56).
/// </summary>
public static class FinanceErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string UserRequired = "USER_REQUIRED";
    public const string CurrencyNotFound = "CURRENCY_NOT_FOUND";
    public const string CurrencyInUse = "CURRENCY_IN_USE";
    public const string CompanyCurrencyNotFound = "COMPANY_CURRENCY_NOT_FOUND";
    public const string BaseCurrencyRequired = "BASE_CURRENCY_REQUIRED";
    public const string BaseCurrencyInUse = "BASE_CURRENCY_IN_USE";
    public const string ExchangeRateForBase = "EXCHANGE_RATE_FOR_BASE";
    public const string ExchangeRateInvalid = "EXCHANGE_RATE_INVALID";
    public const string ExchangeRateNotFound = "EXCHANGE_RATE_NOT_FOUND";
    public const string ExchangeRateRecordNotFound = "EXCHANGE_RATE_RECORD_NOT_FOUND";
    public const string InvalidCurrency = "INVALID_CURRENCY";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string SuperAdminRequired = "SUPERADMIN_REQUIRED";
}
