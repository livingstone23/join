namespace JOIN.Application.Interface;

/// <summary>
/// Outbound SMS adapter contract. The default deployment uses a no-op implementation that
/// only logs the message (per SPEC 26 / F8 — Twilio lands in SPEC 30).
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Sends an SMS message to the supplied E.164-formatted phone number.
    /// </summary>
    /// <param name="phoneNumber">Destination E.164 phone number.</param>
    /// <param name="message">Body text (already localised + signed).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the provider reported delivery success.</returns>
    Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken ct = default);
}
