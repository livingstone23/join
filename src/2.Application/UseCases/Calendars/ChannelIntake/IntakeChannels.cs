using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Channels a channel agent can write from (SPEC 45). Channels are compared by <c>Code</c> (SPEC 99): the phone
/// channels identify the requester by an E.164 number, the e-mail channel by an address.
/// </summary>
public static class IntakeChannels
{
    private static readonly HashSet<string> PhoneCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        CommunicationChannelCodes.WhatsApp,
        CommunicationChannelCodes.Twilio,
        CommunicationChannelCodes.Telegram
    };

    private static readonly HashSet<string> EmailCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        CommunicationChannelCodes.SendGrid
    };

    /// <summary> <c>true</c> for the channels whose identifier is a phone number. </summary>
    public static bool IsPhone(string? code) => code is not null && PhoneCodes.Contains(code.Trim());

    /// <summary> <c>true</c> for the channels whose identifier is an e-mail address. </summary>
    public static bool IsEmail(string? code) => code is not null && EmailCodes.Contains(code.Trim());

    /// <summary> <c>true</c> for any channel the agent may use. </summary>
    public static bool IsSupported(string? code) => IsPhone(code) || IsEmail(code);

    /// <summary> Origin type of an activity created from <paramref name="code"/>. </summary>
    public static CalendarEventOriginType OriginTypeOf(string? code) =>
        IsEmail(code) ? CalendarEventOriginType.Email : CalendarEventOriginType.Phone;
}

/// <summary>
/// Reads the active <see cref="CommunicationChannel"/> of an agent request (<c>CALENDAR_INTAKE_INVALID_CHANNEL</c>
/// when it returns <c>null</c>).
/// </summary>
public sealed class IntakeChannelReader(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Returns the active channel with <paramref name="code"/> when it is one of <see cref="IntakeChannels"/>, or
    /// <c>null</c>.
    /// </summary>
    public async Task<CommunicationChannel?> FindActiveAsync(string? code)
    {
        if (!IntakeChannels.IsSupported(code))
        {
            return null;
        }

        var normalized = code!.Trim().ToUpperInvariant();
        return (await unitOfWork.GetRepository<CommunicationChannel>()
                .GetAllIncludingDeletedAsync(x => x.Code == normalized && x.GcRecord == 0 && x.IsActive))
            .FirstOrDefault();
    }
}
