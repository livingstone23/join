using System.Net.Http.Headers;
using System.Text;

namespace JOIN.Services.WebApi.Services;

/// <summary>
/// Downloads the media of a WhatsApp message received through Twilio (SPEC 47 F5). Twilio protects
/// <c>MediaUrlN</c> with Basic Auth (<c>Twilio:AccountSid</c> / <c>Twilio:AuthToken</c>).
/// <para>
/// Every failure (missing credentials, URL outside Twilio, 4xx/5xx, timeout, network) is returned as a
/// <see cref="TwilioMediaDownloadResult"/> with the reason — never thrown — so the ticket is still created and the
/// skipped attachment is noted on it. The URL comes from the webhook payload, so the credentials are only sent over
/// HTTPS to <see cref="AllowedHost"/>: otherwise anyone holding a webhook token could make the API send them to a
/// host of their choice.
/// </para>
/// </summary>
public class TwilioMediaDownloader(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TwilioMediaDownloader> logger)
{
    /// <summary> Name of the <see cref="HttpClient"/> registered for the downloads. </summary>
    public const string HttpClientName = "TwilioMedia";

    /// <summary> The only host the Twilio credentials are sent to. </summary>
    public const string AllowedHost = "api.twilio.com";

    /// <summary> Downloads <paramref name="mediaUrl"/>; never throws except on caller cancellation. </summary>
    public virtual async Task<TwilioMediaDownloadResult> DownloadAsync(string mediaUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("[TwilioMediaDownloader] Rejected media URL outside https://{AllowedHost}: {Url}", AllowedHost, mediaUrl);
            return TwilioMediaDownloadResult.Failed("la URL no pertenece a Twilio");
        }

        var accountSid = configuration["Twilio:AccountSid"];
        var authToken = configuration["Twilio:AuthToken"];
        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken))
        {
            logger.LogWarning("[TwilioMediaDownloader] Twilio:AccountSid/AuthToken are not configured; media skipped.");
            return TwilioMediaDownloadResult.Failed("credenciales de Twilio no configuradas");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}")));

            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[TwilioMediaDownloader] Twilio returned {StatusCode} for {Url}.", (int)response.StatusCode, mediaUrl);
                return TwilioMediaDownloadResult.Failed($"Twilio respondió {(int)response.StatusCode}");
            }

            return TwilioMediaDownloadResult.Ok(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[TwilioMediaDownloader] Timeout downloading {Url}.", mediaUrl);
            return TwilioMediaDownloadResult.Failed("tiempo de descarga agotado");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "[TwilioMediaDownloader] Network error downloading {Url}.", mediaUrl);
            return TwilioMediaDownloadResult.Failed("error de red al descargar");
        }
    }
}

/// <summary> Outcome of <see cref="TwilioMediaDownloader.DownloadAsync"/>. </summary>
/// <param name="Content">Downloaded bytes, or <c>null</c> when it failed.</param>
/// <param name="FailureReason">Why it failed, in the language of the ticket note.</param>
public sealed record TwilioMediaDownloadResult(byte[]? Content, string? FailureReason)
{
    /// <summary> Successful download. </summary>
    public static TwilioMediaDownloadResult Ok(byte[] content) => new(content, null);

    /// <summary> Failed download. </summary>
    public static TwilioMediaDownloadResult Failed(string reason) => new(null, reason);
}
