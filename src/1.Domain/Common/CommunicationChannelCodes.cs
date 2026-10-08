namespace JOIN.Domain.Common;

/// <summary>
/// Values of <see cref="CommunicationChannel.Code"/> for the channels seeded by the system (SPEC 99).
/// Channels are always compared by <c>Code</c>, never by <c>Name</c>.
/// </summary>
public static class CommunicationChannelCodes
{
    /// <summary>
    /// The JOIN web application (management console): requests born inside the application itself.
    /// </summary>
    public const string Web = "WEB";

    /// <summary>
    /// A first-party application (mobile app or portal with login). Seeded for completeness; nothing uses it yet.
    /// </summary>
    public const string App = "APP";

    /// <summary>
    /// WhatsApp messaging.
    /// </summary>
    public const string WhatsApp = "WHATSAPP";

    /// <summary>
    /// E-mail through SendGrid.
    /// </summary>
    public const string SendGrid = "SENDGRID";

    /// <summary>
    /// Telegram messaging.
    /// </summary>
    public const string Telegram = "TELEGRAM";

    /// <summary>
    /// SMS / voice through Twilio.
    /// </summary>
    public const string Twilio = "TWILIO";
}
