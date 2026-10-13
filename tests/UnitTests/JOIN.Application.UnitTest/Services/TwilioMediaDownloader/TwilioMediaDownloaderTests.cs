using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Downloader = JOIN.Services.WebApi.Services.TwilioMediaDownloader;

namespace JOIN.Application.UnitTest.Services.TwilioMediaDownloader;

/// <summary>
/// SPEC 47 F9 — <see cref="Downloader"/>: every failure is a reason, never an exception, and the Twilio
/// credentials only travel to https://api.twilio.com.
/// </summary>
public sealed class TwilioMediaDownloaderTests
{
    private const string MediaUrl = "https://api.twilio.com/2010-04-01/Accounts/AC1/Messages/MM1/Media/ME1";

    private static Downloader Create(Func<HttpRequestMessage, HttpResponseMessage> respond, bool withCredentials = true, List<HttpRequestMessage>? captured = null)
    {
        var settings = withCredentials
            ? new Dictionary<string, string?> { ["Twilio:AccountSid"] = "AC1", ["Twilio:AuthToken"] = "secret" }
            : new Dictionary<string, string?>();
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(x => x.CreateClient(Downloader.HttpClientName))
            .Returns(() => new HttpClient(new StubHandler(request =>
            {
                captured?.Add(request);
                return respond(request);
            })));

        return new Downloader(factory.Object, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger<Downloader>.Instance);
    }

    [Fact]
    public async Task DownloadAsync_WhenTwilioAnswers200_ShouldReturnTheBytesUsingBasicAuth()
    {
        var captured = new List<HttpRequestMessage>();
        var downloader = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }, captured: captured);

        var result = await downloader.DownloadAsync(MediaUrl, CancellationToken.None);

        result.Content.Should().Equal(1, 2, 3);
        result.FailureReason.Should().BeNull();
        captured.Single().Headers.Authorization!.Scheme.Should().Be("Basic");
    }

    [Fact]
    public async Task DownloadAsync_WhenTwilioAnswers404_ShouldReturnAReasonWithoutThrowing()
    {
        var result = await Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).DownloadAsync(MediaUrl, CancellationToken.None);

        result.Content.Should().BeNull();
        result.FailureReason.Should().Contain("404");
    }

    [Fact]
    public async Task DownloadAsync_WhenTheRequestTimesOut_ShouldReturnAReasonWithoutThrowing()
    {
        var result = await Create(_ => throw new TaskCanceledException("timeout")).DownloadAsync(MediaUrl, CancellationToken.None);

        result.Content.Should().BeNull();
        result.FailureReason.Should().Contain("tiempo");
    }

    [Fact]
    public async Task DownloadAsync_WhenTheNetworkFails_ShouldReturnAReasonWithoutThrowing()
    {
        var result = await Create(_ => throw new HttpRequestException("dns")).DownloadAsync(MediaUrl, CancellationToken.None);

        result.Content.Should().BeNull();
        result.FailureReason.Should().Contain("red");
    }

    [Theory]
    [InlineData("https://attacker.example/steal")]
    [InlineData("http://api.twilio.com/2010-04-01/Media/ME1")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task DownloadAsync_WhenTheUrlIsNotHttpsTwilio_ShouldNotSendTheCredentials(string url)
    {
        var captured = new List<HttpRequestMessage>();

        var result = await Create(_ => new HttpResponseMessage(HttpStatusCode.OK), captured: captured).DownloadAsync(url, CancellationToken.None);

        result.Content.Should().BeNull();
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAsync_WhenCredentialsAreMissing_ShouldReturnAReasonWithoutCallingTwilio()
    {
        var captured = new List<HttpRequestMessage>();

        var result = await Create(_ => new HttpResponseMessage(HttpStatusCode.OK), withCredentials: false, captured: captured).DownloadAsync(MediaUrl, CancellationToken.None);

        result.FailureReason.Should().Contain("credenciales");
        captured.Should().BeEmpty();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
