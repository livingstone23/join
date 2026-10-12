using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Free slots of a user for an activity type (SPEC 44, <c>GET /CalendarPanel/availability</c>), seen by someone who
/// is not the owner: R5 (work days, non-working periods, holidays, business hours) when the type requires business
/// hours, and R4 (busy occurrences) when it does not allow overlap. Blocks of the type duration, from the start of the
/// effective hours (or midnight), never in the past.
/// </summary>
public static class CalendarAvailabilityCalculator
{
    public static IReadOnlyList<CalendarSlotDto> Compute(
        DateTime fromUtc,
        DateTime toUtc,
        DateTime nowUtc,
        TimeZoneInfo zone,
        int durationMinutes,
        bool requiresBusinessHours,
        bool allowsOverlap,
        CalendarWorkDays workDays,
        CalendarBusinessHours hours,
        IReadOnlyCollection<(DateOnly Start, DateOnly End)> unavailablePeriods,
        IReadOnlyCollection<DateOnly> holidays,
        IReadOnlyCollection<(DateTime StartUtc, DateTime EndUtc)> busy)
    {
        var slots = new List<CalendarSlotDto>();
        if (durationMinutes <= 0)
        {
            return slots;
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);
        foreach (var date in CalendarTime.LocalDates(fromUtc, toUtc, zone))
        {
            if (requiresBusinessHours
                && (!CalendarBusinessHours.IsWorkDay(workDays, date)
                    || unavailablePeriods.Any(p => p.Start <= date && p.End >= date)
                    || holidays.Contains(date)))
            {
                continue;
            }

            var dayHours = requiresBusinessHours ? hours : CalendarBusinessHours.WholeDay;
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            var localStart = dayStart + dayHours.Start.ToTimeSpan();
            var localLimit = dayHours.End is { } end ? dayStart + end.ToTimeSpan() : dayStart.AddDays(1);

            for (var slotStart = localStart; slotStart + duration <= localLimit; slotStart += duration)
            {
                var startUtc = CalendarTime.ToUtc(slotStart, zone);
                var endUtc = CalendarTime.ToUtc(slotStart + duration, zone);
                if (startUtc < fromUtc || endUtc > toUtc || startUtc < nowUtc)
                {
                    continue;
                }

                if (!allowsOverlap && busy.Any(b => b.StartUtc < endUtc && startUtc < b.EndUtc))
                {
                    continue;
                }

                slots.Add(new CalendarSlotDto(startUtc, endUtc));
            }
        }

        return slots;
    }
}
