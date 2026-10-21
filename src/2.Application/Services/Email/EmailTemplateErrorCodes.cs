namespace JOIN.Application.Services.Email;

/// <summary>
/// Error codes of the email templates (SPEC 71). The HTTP mapping lives in
/// <c>EmailTemplateResponseResults</c> (WebApi): 404 for the <c>*_NOT_FOUND</c> codes, 409 for
/// <c>DUPLICATE_KEY</c> and <c>EMAIL_TEMPLATE_TYPE_IN_USE</c>, 403 for <c>SUPERADMIN_REQUIRED</c>,
/// 400 for the rest.
/// </summary>
public static class EmailTemplateErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string EmailTemplateNotFound = "EMAIL_TEMPLATE_NOT_FOUND";
    public const string EmailTemplateTypeNotFound = "EMAIL_TEMPLATE_TYPE_NOT_FOUND";
    public const string EmailTemplateUnknownVariable = "EMAIL_TEMPLATE_UNKNOWN_VARIABLE";
    public const string EmailTemplateTypeInUse = "EMAIL_TEMPLATE_TYPE_IN_USE";
    public const string PersonNotFound = "PERSON_NOT_FOUND";
    public const string LeadNotFound = "LEAD_NOT_FOUND";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string SuperAdminRequired = "SUPERADMIN_REQUIRED";
}
