namespace JOIN.Application.Services.Customers;

/// <summary>
/// Error codes of the customer module (SPEC 55).
/// </summary>
public static class CustomerErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string UserRequired = "USER_REQUIRED";
    public const string InvalidPerson = "INVALID_PERSON";
    public const string InvalidCustomerStage = "INVALID_CUSTOMER_STAGE";
    public const string CustomerNotFound = "CUSTOMER_NOT_FOUND";
    public const string CustomerStageNotFound = "CUSTOMER_STAGE_NOT_FOUND";
    public const string CustomerStageDefaultRequired = "CUSTOMER_STAGE_DEFAULT_REQUIRED";
    public const string CustomerStageDefaultInactive = "CUSTOMER_STAGE_DEFAULT_INACTIVE";
    public const string CustomerStageInUse = "CUSTOMER_STAGE_IN_USE";
    public const string DuplicateKey = "DUPLICATE_KEY";
}
