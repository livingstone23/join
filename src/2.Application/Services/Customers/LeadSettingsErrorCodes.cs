namespace JOIN.Application.Services.Customers;

/// <summary>
/// Error codes of the commercial settings (SPEC 59).
/// </summary>
public static class LeadSettingsErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string LeadStageNotFound = "LEAD_STAGE_NOT_FOUND";
    public const string LeadSourceNotFound = "LEAD_SOURCE_NOT_FOUND";
    public const string LeadReasonNotFound = "LEAD_REASON_NOT_FOUND";
    public const string RelationshipTypeNotFound = "RELATIONSHIP_TYPE_NOT_FOUND";
    public const string ContactTypeNotFound = "CONTACT_TYPE_NOT_FOUND";
    public const string LeadStageKindRequired = "LEAD_STAGE_KIND_REQUIRED";
    public const string LeadStageSystem = "LEAD_STAGE_SYSTEM";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string SuperAdminRequired = "SUPERADMIN_REQUIRED";
}
