using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Queries.ResolveTicketInboundChannel;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;
using JOIN.Domain.Common;
using JOIN.Services.WebApi.Controllers.Messaging;
using JOIN.Services.WebApi.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;

namespace JOIN.Application.UnitTest.Controllers.Messaging;

/// <summary>
/// SPEC 47 F9 — <see cref="InboundWhatsAppController"/> and <see cref="InboundEmailController"/>: bare 404 for an
/// unusable token, payload mapping, tenant exposed to the request and the minimal <c>{ ticketCode }</c> answer.
/// </summary>
public sealed class InboundWebhookControllerTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();
    private readonly Mock<ISender> _sender = new();
    private readonly Mock<TwilioMediaDownloader> _downloader = new(
        Mock.Of<IHttpClientFactory>(),
        new ConfigurationBuilder().Build(),
        NullLogger<TwilioMediaDownloader>.Instance);
    private RegisterInboundTicketCommand? _sent;

    public InboundWebhookControllerTests()
    {
        _sender
            .Setup(x => x.Send(It.IsAny<RegisterInboundTicketCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Response<TicketDto>>, CancellationToken>((command, _) => _sent = (RegisterInboundTicketCommand)command)
            .ReturnsAsync(new Response<TicketDto> { IsSuccess = true, Data = new TicketDto { Id = Guid.NewGuid(), Code = "TICK-202610-0001" } });
    }

    private void TokenResolvesTo(string channelCode) =>
        _sender
            .Setup(x => x.Send(It.Is<ResolveTicketInboundChannelQuery>(q => q.ExpectedChannelCode == channelCode), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Response<ResolvedTicketInboundChannelDto>
            {
                IsSuccess = true,
                Data = new ResolvedTicketInboundChannelDto(Guid.NewGuid(), _companyId, _channelId, channelCode)
            });

    private void TokenDoesNotResolve() =>
        _sender
            .Setup(x => x.Send(It.IsAny<ResolveTicketInboundChannelQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response<ResolvedTicketInboundChannelDto>.Error(TicketInboundErrorCodes.InboundChannelNotFound));

    private T WithContext<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private InboundWhatsAppController WhatsApp() =>
        WithContext(new InboundWhatsAppController(_sender.Object, _downloader.Object, NullLogger<InboundWhatsAppController>.Instance));

    private InboundEmailController Email() =>
        WithContext(new InboundEmailController(_sender.Object, NullLogger<InboundEmailController>.Instance));

    private static FormCollection Form(Dictionary<string, StringValues> fields, FormFileCollection? files = null) => new(fields, files);

    [Fact]
    public async Task WhatsApp_WhenTokenDoesNotResolve_ShouldAnswerABare404()
    {
        TokenDoesNotResolve();

        var result = await WhatsApp().Receive("unknown", Form(new() { ["MessageSid"] = "SM1", ["From"] = "whatsapp:+1" }));

        ShouldBeBareNotFound(result);
        _sender.Verify(x => x.Send(It.IsAny<RegisterInboundTicketCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhatsApp_ShouldLookTheTokenUpOnlyAmongWhatsAppChannels()
    {
        TokenDoesNotResolve();

        await WhatsApp().Receive("token", Form(new()));

        _sender.Verify(x => x.Send(
            It.Is<ResolveTicketInboundChannelQuery>(q => q.WebhookToken == "token" && q.ExpectedChannelCode == CommunicationChannelCodes.WhatsApp),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task WhatsApp_WhenMessageSidIsMissing_ShouldAnswerPayloadInvalid()
    {
        TokenResolvesTo(CommunicationChannelCodes.WhatsApp);

        var result = await WhatsApp().Receive("token", Form(new() { ["From"] = "whatsapp:+50588887777", ["Body"] = "hola" }));

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<Response<object>>()
            .Which.Message.Should().Be(TicketInboundErrorCodes.PayloadInvalid);
    }

    [Fact]
    public async Task WhatsApp_ValidPayload_ShouldMapTwilioFieldsExposeTheTenantAndAnswerOnlyTheCode()
    {
        TokenResolvesTo(CommunicationChannelCodes.WhatsApp);
        _downloader
            .Setup(x => x.DownloadAsync("https://api.twilio.com/m0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwilioMediaDownloadResult.Ok([1, 2]));
        _downloader
            .Setup(x => x.DownloadAsync("https://api.twilio.com/m1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwilioMediaDownloadResult.Failed("Twilio respondió 404"));
        var controller = WhatsApp();

        var result = await controller.Receive("token", Form(new()
        {
            ["MessageSid"] = "SM123",
            ["From"] = "whatsapp:+50588887777",
            ["Body"] = "Mi pedido no llegó",
            ["NumMedia"] = "2",
            ["MediaUrl0"] = "https://api.twilio.com/m0",
            ["MediaContentType0"] = "image/jpeg",
            ["MediaUrl1"] = "https://api.twilio.com/m1",
            ["MediaContentType1"] = "application/pdf"
        }));

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(new InboundTicketResponse("TICK-202610-0001"));
        controller.HttpContext.Items[CurrentUserService.InboundCompanyIdItemKey].Should().Be(_companyId);
        _sent!.CompanyId.Should().Be(_companyId);
        _sent.ChannelId.Should().Be(_channelId);
        _sent.SenderIdentifier.Should().Be("+50588887777");
        _sent.MessageSubject.Should().BeEmpty();
        _sent.MessageBody.Should().Be("Mi pedido no llegó");
        _sent.ExternalMessageId.Should().Be("SM123");
        _sent.Attachments.Should().ContainSingle().Which.FileName.Should().Be("whatsapp-SM123-0.jpg");
        _sent.SkippedAttachments.Should().ContainSingle().Which.Should().Be("whatsapp-SM123-1.pdf: Twilio respondió 404");
    }

    [Fact]
    public async Task WhatsApp_WhenTheChannelWasRotatedMeanwhile_ShouldAnswerABare404()
    {
        TokenResolvesTo(CommunicationChannelCodes.WhatsApp);
        _sender
            .Setup(x => x.Send(It.IsAny<RegisterInboundTicketCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response<TicketDto>.Error(TicketInboundErrorCodes.InboundChannelNotFound));

        var result = await WhatsApp().Receive("token", Form(new() { ["MessageSid"] = "SM1", ["From"] = "whatsapp:+50588887777" }));

        ShouldBeBareNotFound(result);
    }

    [Fact]
    public async Task WhatsApp_WhenTheCompanyIsNotConfigured_ShouldAnswer400WithTheCode()
    {
        TokenResolvesTo(CommunicationChannelCodes.WhatsApp);
        _sender
            .Setup(x => x.Send(It.IsAny<RegisterInboundTicketCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response<TicketDto>.Error(TicketInboundErrorCodes.NoTicketAdmin, ["no admin"]));

        var result = await WhatsApp().Receive("token", Form(new() { ["MessageSid"] = "SM1", ["From"] = "whatsapp:+50588887777" }));

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<Response<object>>()
            .Which.Message.Should().Be(TicketInboundErrorCodes.NoTicketAdmin);
    }

    private static void ShouldBeBareNotFound(IActionResult result) => result.Should().BeOfType<EmptyResult>();

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("application/pdf; charset=binary", ".pdf")]
    [InlineData("audio/ogg", ".bin")]
    public void SyntheticFileName_ShouldUseTheExtensionOfTheContentType(string contentType, string extension) =>
        InboundWhatsAppController.SyntheticFileName("SM9", 3, contentType).Should().Be($"whatsapp-SM9-3{extension}");

    [Theory]
    [InlineData("whatsapp:+50588887777", "+50588887777")]
    [InlineData("+50588887777", "+50588887777")]
    [InlineData(null, "")]
    public void StripWhatsAppPrefix_ShouldReturnTheBareNumber(string? from, string expected) =>
        InboundWhatsAppController.StripWhatsAppPrefix(from).Should().Be(expected);

    [Fact]
    public async Task Email_WhenTokenDoesNotResolve_ShouldAnswerABare404()
    {
        TokenDoesNotResolve();

        var result = await Email().Receive("token", Form(new() { ["from"] = "a@b.com", ["text"] = "hola" }));

        ShouldBeBareNotFound(result);
        _sender.Verify(x => x.Send(
            It.Is<ResolveTicketInboundChannelQuery>(q => q.ExpectedChannelCode == CommunicationChannelCodes.SendGrid),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Email_WhenBodyIsMissing_ShouldAnswerPayloadInvalid()
    {
        TokenResolvesTo(CommunicationChannelCodes.SendGrid);

        var result = await Email().Receive("token", Form(new() { ["from"] = "a@b.com", ["subject"] = "s" }));

        result.Should().BeOfType<BadRequestObjectResult>();
        _sender.Verify(x => x.Send(It.IsAny<RegisterInboundTicketCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Email_ValidPayload_ShouldMapSendGridFieldsAndFiles()
    {
        TokenResolvesTo(CommunicationChannelCodes.SendGrid);
        var bytes = new byte[] { 37, 80, 68, 70 };
        var files = new FormFileCollection
        {
            new FormFile(new MemoryStream(bytes), 0, bytes.Length, "attachment1", "factura.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            }
        };

        var result = await Email().Receive("token", Form(new()
        {
            ["from"] = "\"Ana Pérez\" <ana@cliente.com>",
            ["subject"] = "Factura duplicada",
            ["text"] = "Me cobraron dos veces",
            ["headers"] = "Received: x\nMessage-ID: <abc@mail.cliente.com>\nSubject: Factura duplicada"
        }, files));

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(new InboundTicketResponse("TICK-202610-0001"));
        _sent!.SenderIdentifier.Should().Be("ana@cliente.com");
        _sent.SenderDisplayName.Should().Be("Ana Pérez");
        _sent.MessageSubject.Should().Be("Factura duplicada");
        _sent.MessageBody.Should().Be("Me cobraron dos veces");
        _sent.ExternalMessageId.Should().Be("abc@mail.cliente.com");
        var attachment = _sent.Attachments.Should().ContainSingle().Subject;
        attachment.FileName.Should().Be("factura.pdf");
        attachment.ContentType.Should().Be("application/pdf");
        attachment.Length.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task Email_WithoutMessageIdAndText_ShouldUseHtmlAndADeterministicHash()
    {
        TokenResolvesTo(CommunicationChannelCodes.SendGrid);

        await Email().Receive("token", Form(new() { ["from"] = "ana@cliente.com", ["subject"] = "s", ["html"] = "<p>hola</p>" }));

        _sent!.MessageBody.Should().Be("<p>hola</p>");
        _sent.ExternalMessageId.Should().Be(InboundEmailController.ComputePayloadHash("ana@cliente.com", "s", "<p>hola</p>"));
        _sent.ExternalMessageId.Should().HaveLength(64);
    }

    [Theory]
    [InlineData("ana@cliente.com", "ana@cliente.com", null)]
    [InlineData("Ana <ana@cliente.com>", "ana@cliente.com", "Ana")]
    [InlineData("<ana@cliente.com>", "ana@cliente.com", null)]
    [InlineData(null, "", null)]
    public void ParseFrom_ShouldSplitAddressAndDisplayName(string? raw, string address, string? displayName)
    {
        InboundEmailController.ParseAddress(raw).Should().Be(address);
        InboundEmailController.ParseDisplayName(raw).Should().Be(displayName);
    }

    [Theory]
    [InlineData("Message-ID: <id-1@x>", "id-1@x")]
    [InlineData("message-id:id-2@x\r\n", "id-2@x")]
    [InlineData("Subject: hola", null)]
    [InlineData("Message-ID: <>", null)]
    public void ExtractMessageId_ShouldReadTheHeaderWithoutBrackets(string headers, string? expected) =>
        InboundEmailController.ExtractMessageId(headers).Should().Be(expected);
}
