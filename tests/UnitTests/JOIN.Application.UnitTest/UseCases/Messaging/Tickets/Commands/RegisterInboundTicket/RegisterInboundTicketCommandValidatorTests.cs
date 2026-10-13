using FluentAssertions;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;

/// <summary> SPEC 47 F9 — <see cref="RegisterInboundTicketCommandValidator"/>. </summary>
public sealed class RegisterInboundTicketCommandValidatorTests
{
    private readonly RegisterInboundTicketCommandValidator _validator = new();

    private static RegisterInboundTicketCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "+50588887777", null, string.Empty, "Hola", "SM-1", []);

    [Fact]
    public void Validate_ValidCommandWithLongSubjectAndBody_ShouldPass() =>
        _validator.Validate(Valid() with { MessageSubject = new string('s', 1000), MessageBody = new string('b', 20_000) })
            .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_WhenIdsOrKeysAreMissing_ShouldFail()
    {
        _validator.Validate(Valid() with { CompanyId = Guid.Empty }).IsValid.Should().BeFalse();
        _validator.Validate(Valid() with { ChannelId = Guid.Empty }).IsValid.Should().BeFalse();
        _validator.Validate(Valid() with { ExternalMessageId = "" }).IsValid.Should().BeFalse();
        _validator.Validate(Valid() with { ExternalMessageId = new string('x', 201) }).IsValid.Should().BeFalse();
        _validator.Validate(Valid() with { SenderIdentifier = " " }).IsValid.Should().BeFalse();
    }
}
