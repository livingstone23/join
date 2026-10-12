namespace JOIN.Domain.Calendars;

/// <summary>
/// System values of <see cref="CalendarStatus.Code"/> and <see cref="CalendarActivity.Code"/> that the
/// calendar logic relies on (SPEC 44). Statuses and activity types are always compared by <c>Code</c>,
/// never by <c>Name</c>.
/// </summary>
public static class CalendarCodes
{
    /// <summary>
    /// Codes of the global <see cref="CalendarStatus"/> catalog.
    /// </summary>
    public static class Statuses
    {
        /// <summary> Pending confirmation. Default initial status when catalogs are synced; cannot be deactivated. </summary>
        public const string Pending = "PENDING";

        /// <summary> Confirmed. Only confirmed activities are synced with Google (SPEC 46). </summary>
        public const string Confirmed = "CONFIRMED";

        /// <summary> Rescheduled. Default of <see cref="CalendarCompany.RescheduledByOthersStatusCompanyId"/>. </summary>
        public const string Rescheduled = "RESCHEDULED";

        /// <summary> Cancelled. Frees the time slot; cannot be deactivated. </summary>
        public const string Cancelled = "CANCELLED";

        /// <summary> Completed. Set manually in this stage. </summary>
        public const string Completed = "COMPLETED";
    }

    /// <summary>
    /// Codes of the global <see cref="CalendarActivity"/> catalog.
    /// </summary>
    public static class Activities
    {
        public const string Meeting = "MEETING";
        public const string Appointment = "APPOINTMENT";
        public const string Task = "TASK";
        public const string Reminder = "REMINDER";

        /// <summary> Review of a person created by the channel agent (SPEC 45). </summary>
        public const string PersonReview = "PERSON_REVIEW";
    }
}
