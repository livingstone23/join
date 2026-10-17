namespace JOIN.Application.Services.Customers;

/// <summary>
/// Error codes of the sales teams and management roles (SPEC 60).
/// </summary>
public static class SalesTeamErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string UserRequired = "USER_REQUIRED";
    public const string SalesTeamNotFound = "SALES_TEAM_NOT_FOUND";
    public const string TeamRoleNotFound = "TEAM_ROLE_NOT_FOUND";
    public const string SalesTeamMemberNotFound = "SALES_TEAM_MEMBER_NOT_FOUND";
    public const string UserNotInCompany = "SALES_TEAM_USER_NOT_IN_COMPANY";
    public const string MemberDatesInvalid = "SALES_TEAM_MEMBER_DATES_INVALID";
    public const string SalesTeamInUse = "SALES_TEAM_IN_USE";
    public const string TeamRoleInUse = "TEAM_ROLE_IN_USE";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string SuperAdminRequired = "SUPERADMIN_REQUIRED";
}
