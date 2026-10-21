namespace JOIN.Application.Services.Email;

/// <summary>
/// Error codes of the sending of emails (SPEC 72). The HTTP mapping lives in
/// <c>PersonEmailResponseResults</c> (WebApi): 404 for the <c>*_NOT_FOUND</c> codes, 409 for
/// <c>PERSON_NOT_ACTIVE</c> and <c>LEAD_NOT_OPEN</c>, 429 for <c>EMAIL_DAILY_LIMIT_REACHED</c>,
/// 502 for <c>EMAIL_SEND_FAILED</c> and 400 for the rest.
/// </summary>
public static class PersonEmailErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string UserRequired = "USER_REQUIRED";
    public const string PersonNotFound = "PERSON_NOT_FOUND";
    public const string CustomerNotFound = "CUSTOMER_NOT_FOUND";
    public const string LeadNotFound = "LEAD_NOT_FOUND";
    public const string PersonNotActive = "PERSON_NOT_ACTIVE";
    public const string PersonEmailNotAvailable = "PERSON_EMAIL_NOT_AVAILABLE";
    public const string InvalidEmailAddress = "INVALID_EMAIL_ADDRESS";
    public const string LeadNotOpen = "LEAD_NOT_OPEN";
    public const string InvalidLeadPerson = "INVALID_LEAD_PERSON";
    public const string EmailContactTypeNotConfigured = "EMAIL_CONTACT_TYPE_NOT_CONFIGURED";
    public const string EmailTemplateNotFound = "EMAIL_TEMPLATE_NOT_FOUND";
    public const string EmailTemplateUnknownVariable = "EMAIL_TEMPLATE_UNKNOWN_VARIABLE";
    public const string EmailAttachmentNotFound = "EMAIL_ATTACHMENT_NOT_FOUND";
    public const string EmailAttachmentsTooLarge = "EMAIL_ATTACHMENTS_TOO_LARGE";
    public const string EmailDailyLimitReached = "EMAIL_DAILY_LIMIT_REACHED";
    public const string EmailSettingsNotConfigured = "EMAIL_SETTINGS_NOT_CONFIGURED";
    public const string EmailSettingsAlreadyExists = "EMAIL_SETTINGS_ALREADY_EXISTS";
    public const string SuperAdminRequired = "SUPERADMIN_REQUIRED";
    public const string EmailSendFailed = "EMAIL_SEND_FAILED";
    public const string PersonEmailNotFound = "PERSON_EMAIL_NOT_FOUND";
    public const string StorageProviderNotConfigured = "STORAGE_PROVIDER_NOT_CONFIGURED";
    public const string FileNotFoundInStorage = "FILE_NOT_FOUND_IN_STORAGE";
}
