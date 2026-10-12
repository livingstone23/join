namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Error codes of the calendar module (SPEC 44), returned in <c>Response.Message</c>. The controllers map them to
/// HTTP status codes (<c>CalendarResponseResults</c>).
/// </summary>
public static class CalendarErrorCodes
{
    public const string CompanyRequired = "COMPANY_REQUIRED";
    public const string UserRequired = "USER_REQUIRED";
    public const string CompanyNotConfigured = "CALENDAR_COMPANY_NOT_CONFIGURED";
    public const string CompanyAlreadyConfigured = "CALENDAR_COMPANY_ALREADY_CONFIGURED";
    public const string InvalidTimeZone = "CALENDAR_INVALID_TIME_ZONE";
    public const string InvalidBusinessHours = "CALENDAR_INVALID_BUSINESS_HOURS";
    public const string UserHoursOutOfCompanyRange = "CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE";
    public const string DefaultUserNotInCompany = "CALENDAR_DEFAULT_USER_NOT_IN_COMPANY";
    public const string UserNotInCompany = "CALENDAR_USER_NOT_IN_COMPANY";
    public const string UserHasNoCalendar = "CALENDAR_USER_HAS_NO_CALENDAR";

    public const string ActivityNotFound = "CALENDAR_ACTIVITY_NOT_FOUND";
    public const string ActivityCodeDuplicate = "CALENDAR_ACTIVITY_CODE_DUPLICATE";
    public const string ActivityInUse = "CALENDAR_ACTIVITY_IN_USE";
    public const string ActivityRequiredBySystem = "CALENDAR_ACTIVITY_REQUIRED_BY_SYSTEM";
    public const string ActivityAlreadyEnabled = "CALENDAR_ACTIVITY_ALREADY_ENABLED";
    public const string ActivityNotEnabled = "CALENDAR_ACTIVITY_NOT_ENABLED";

    public const string StatusNotFound = "CALENDAR_STATUS_NOT_FOUND";
    public const string StatusCodeDuplicate = "CALENDAR_STATUS_CODE_DUPLICATE";
    public const string StatusInUse = "CALENDAR_STATUS_IN_USE";
    public const string StatusInUseAsInitial = "CALENDAR_STATUS_IN_USE_AS_INITIAL";
    public const string StatusInUseAsReschedule = "CALENDAR_STATUS_IN_USE_AS_RESCHEDULE";
    public const string StatusRequiredBySystem = "CALENDAR_STATUS_REQUIRED_BY_SYSTEM";
    public const string StatusAlreadyEnabled = "CALENDAR_STATUS_ALREADY_ENABLED";
    public const string StatusNotEnabled = "CALENDAR_STATUS_NOT_ENABLED";

    public const string NonWorkingDayNotFound = "CALENDAR_NON_WORKING_DAY_NOT_FOUND";
    public const string NonWorkingDayDuplicate = "CALENDAR_NON_WORKING_DAY_DUPLICATE";
    public const string NonWorkingPeriodNotFound = "CALENDAR_NON_WORKING_PERIOD_NOT_FOUND";
    public const string NonWorkingPeriodOverlap = "CALENDAR_NON_WORKING_PERIOD_OVERLAP";
    public const string UserConfigurationNotFound = "CALENDAR_USER_CONFIGURATION_NOT_FOUND";

    public const string CalendarNotFound = "CALENDAR_NOT_FOUND";
    public const string CalendarNameDuplicate = "CALENDAR_NAME_DUPLICATE";
    public const string CalendarIsDefault = "CALENDAR_IS_DEFAULT";
    public const string CalendarHasActiveEvents = "CALENDAR_HAS_ACTIVE_EVENTS";
    public const string CalendarInactive = "CALENDAR_INACTIVE";

    public const string EventNotFound = "CALENDAR_EVENT_NOT_FOUND";
    public const string EventOverlap = "CALENDAR_EVENT_OVERLAP";
    public const string OutsideWorkDays = "CALENDAR_OUTSIDE_WORK_DAYS";
    public const string UserNotAvailable = "CALENDAR_USER_NOT_AVAILABLE";
    public const string CompanyHoliday = "CALENDAR_COMPANY_HOLIDAY";
    public const string OutsideBusinessHours = "CALENDAR_OUTSIDE_BUSINESS_HOURS";
    public const string InvalidRecurrenceRule = "CALENDAR_INVALID_RECURRENCE_RULE";
    public const string RecurrenceScopeRequired = "CALENDAR_RECURRENCE_SCOPE_REQUIRED";
    public const string OccurrenceNotFound = "CALENDAR_OCCURRENCE_NOT_FOUND";
    public const string InvalidEventTimes = "CALENDAR_INVALID_EVENT_TIMES";
    public const string CancellationReasonRequired = "CALENDAR_CANCELLATION_REASON_REQUIRED";
    public const string InvalidLink = "CALENDAR_INVALID_LINK";
    public const string RangeTooLarge = "CALENDAR_RANGE_TOO_LARGE";
    public const string TooManyUsers = "CALENDAR_TOO_MANY_USERS";
    public const string OthersPermissionRequired = "CALENDAR_OTHERS_PERMISSION_REQUIRED";
    public const string WebChannelNotFound = "CALENDAR_WEB_CHANNEL_NOT_FOUND";

    // SPEC 45 — channel agent intake and pending persons.
    public const string IntakeInvalidChannel = "CALENDAR_INTAKE_INVALID_CHANNEL";
    public const string IntakeCompanyNotAllowed = "CALENDAR_INTAKE_COMPANY_NOT_ALLOWED";
    public const string IntakeLookupCriteriaRequired = "CALENDAR_INTAKE_LOOKUP_CRITERIA_REQUIRED";
    public const string IntakeContactBelongsToOtherPerson = "CALENDAR_INTAKE_CONTACT_BELONGS_TO_OTHER_PERSON";
    public const string IntakeTargetUserHasNoCalendar = "CALENDAR_INTAKE_TARGET_USER_HAS_NO_CALENDAR";
    public const string IntakeEventAlreadyClosed = "CALENDAR_INTAKE_EVENT_ALREADY_CLOSED";
    public const string PersonPendingNotFound = "PERSON_PENDING_NOT_FOUND";
    public const string PersonPendingAlreadyResolved = "PERSON_PENDING_ALREADY_RESOLVED";
    public const string PersonPendingInvalidCustomer = "PERSON_PENDING_INVALID_CUSTOMER";
    public const string PersonPendingInvalidStatus = "PERSON_PENDING_INVALID_STATUS";
}
