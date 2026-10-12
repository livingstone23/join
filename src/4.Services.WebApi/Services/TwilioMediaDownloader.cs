using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JOIN.Services.WebApi.Services;

/// <summary>
/// Descarga los archivos multimedia (<c>MediaUrlN</c>) que Twilio adjunta a un
/// webhook de WhatsApp. La descarga se hace con HTTP Basic Auth usando
/// <c>Twilio:AccountSid</c>/<c>Twilio:AuthToken</c> de configuración.
/// <para>
/// Diseño: el SPEC 47 pide un singleton con <see cref="IHttpClientFactory"/>.
/// Para no tocar <c>Program.cs</c> (fuera del alcance de F5) se usa aquí un
/// <see cref="HttpClient"/> estático con <see cref="SocketsHttpHandler"/> y
/// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/> corto (2 min) para
/// evitar la huella de DNS de un singleton de larga vida — mismo efecto neto
/// que <c>IHttpClientFactory.CreateClient("Twilio")</c>.
/// </para>
/// <para>
/// Política de errores: cualquier fallo de red, timeout, o respuesta no-2xx
/// se traduce en un <c>null</c> + log de warning. <b>No</b> propaga la
/// excepción: la ingesta de un mensaje no puede tumbarse por una descarga
/// de media que falla (SPEC 47 F5 / decisión de "best-effort").
/// </para>
/// </summary>
public static class TwilioMediaDownloader
{
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    private static readonly Lazy<HttpClient> _client = new(() => new HttpClient(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = PooledConnectionLifetime,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        },
        disposeHandler: true)
    {
        Timeout = TimeSpan.FromSeconds(15)
    });

    /// <summary>
    /// Intenta descargar el binario en <paramref name="mediaUrl"/>. Devuelve
    /// <c>null</c> si Twilio no está configurado, si la URL es vacía, o si la
    /// llamada falla por cualquier motivo.
    /// </summary>
    /// <param name="mediaUrl">URL firmada de Twilio del adjunto.</param>
    /// <param name="configuration">Configuración de la app (lee <c>Twilio:AccountSid</c>/<c>AuthToken</c>).</param>
    /// <param name="logger">Logger para registrar skips y errores.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    public static async Task<byte[]?> TryDownloadAsync(
        string mediaUrl,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
        {
            return null;
        }

        var accountSid = configuration["Twilio:AccountSid"];
        var authToken = configuration["Twilio:AuthToken"];

        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken))
        {
            logger.LogWarning(
                "[TwilioMediaDownloader] Skipping media download: Twilio:AccountSid/AuthToken are not configured.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
            var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

            using var response = await _client.Value.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "[TwilioMediaDownloader] Twilio returned {StatusCode} for {Url}; skipping attachment.",
                    (int)response.StatusCode,
                    mediaUrl);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning(
                "[TwilioMediaDownloader] Timeout downloading {Url}; skipping attachment.",
                mediaUrl);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "[TwilioMediaDownloader] Network error downloading {Url}; skipping attachment.",
                mediaUrl);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[TwilioMediaDownloader] Unexpected error downloading {Url}; skipping attachment.",
                mediaUrl);
            return null;
        }
    }
}
