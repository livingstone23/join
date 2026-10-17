namespace JOIN.Application.Services.Customers;

/// <summary>
/// Error codes of the leads (SPEC 61) and of the in-use blocks it adds to SPEC 57 and 59.
/// </summary>
public static class LeadErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string UserRequired = "USER_REQUIRED";
    public const string LeadNotFound = "LEAD_NOT_FOUND";
    public const string LeadPersonRequired = "LEAD_PERSON_REQUIRED";
    public const string InvalidPerson = "INVALID_PERSON";
    public const string LeadDuplicateOpen = "LEAD_DUPLICATE_OPEN";
    public const string InvalidLeadStage = "INVALID_LEAD_STAGE";
    public const string InvalidLeadSource = "INVALID_LEAD_SOURCE";
    public const string InvalidRelationshipType = "INVALID_RELATIONSHIP_TYPE";
    public const string InvalidLeadReason = "INVALID_LEAD_REASON";
    public const string InvalidContactType = "INVALID_CONTACT_TYPE";
    public const string StageSkipNotAllowed = "LEAD_STAGE_SKIP_NOT_ALLOWED";
    public const string LeadNotOpen = "LEAD_NOT_OPEN";
    public const string LeadNotInactive = "LEAD_NOT_INACTIVE";
    public const string ActionForbidden = "LEAD_ACTION_FORBIDDEN";
    public const string InvalidOwner = "INVALID_OWNER";
    public const string InvalidSalesTeam = "INVALID_SALES_TEAM";
    public const string CurrencyLocked = "LEAD_CURRENCY_LOCKED";
    public const string InvalidCurrency = "INVALID_CURRENCY";
    public const string ExchangeRateNotFound = "EXCHANGE_RATE_NOT_FOUND";
    public const string InvalidProduct = "INVALID_PRODUCT";
    public const string PersonInactive = "LEAD_PERSON_INACTIVE";
    public const string InvalidReference = "INVALID_REFERENCE";
    public const string InteractionDurationRequired = "LEAD_INTERACTION_DURATION_REQUIRED";
    public const string InteractionNotFound = "LEAD_INTERACTION_NOT_FOUND";
    public const string CommentNotFound = "LEAD_COMMENT_NOT_FOUND";
    public const string ProductLineNotFound = "LEAD_PRODUCT_NOT_FOUND";

    public const string LeadStageInUse = "LEAD_STAGE_IN_USE";
    public const string LeadSourceInUse = "LEAD_SOURCE_IN_USE";
    public const string LeadReasonInUse = "LEAD_REASON_IN_USE";
    public const string RelationshipTypeInUse = "RELATIONSHIP_TYPE_IN_USE";
    public const string ContactTypeInUse = "CONTACT_TYPE_IN_USE";
}
