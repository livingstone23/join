using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.LinkCustomerToPersonPendingConfirmation;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CancelIntakeEvent;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CreateIntakeEvent;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RegisterIntakePerson;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RescheduleIntakeEvent;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.Commands;

/// <summary>
/// SPEC 45 — validators of the agent commands and of the pending-persons inbox.
/// </summary>
public sealed class IntakeCommandValidatorsTests
{
    private static readonly RegisterIntakePersonCommand ValidPerson = new(
        PersonType.Physical, "Ana", "López", null, Guid.NewGuid(), "001", null, "+50588887777", null, "Cita", "WHATSAPP", "+50588887777", "wamid");

    private static readonly CreateIntakeEventCommand ValidEvent = new(
        "MEETING", "Cita", null, null, null, null, false, DateTimeOffset.UtcNow, null, null, null, null, null,
        Guid.NewGuid(), "WHATSAPP", null, "+50588887777", "wamid");

    [Fact]
    public void RegisterPerson_ShouldAcceptAPhysicalPersonWithoutGender()
    {
        new RegisterIntakePersonCommandValidator().Validate(ValidPerson).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("phone-format")]
    [InlineData("phone-missing")]
    [InlineData("email-missing")]
    [InlineData("no-contact")]
    [InlineData("reason")]
    [InlineData("legal-without-commercial-name")]
    [InlineData("legal-with-gender")]
    [InlineData("last-name")]
    [InlineData("type")]
    public void RegisterPerson_ShouldRejectInvalidData(string scenario)
    {
        var command = scenario switch
        {
            "phone-format" => ValidPerson with { Phone = "88887777" },
            "phone-missing" => ValidPerson with { Phone = null, Email = "ana@correo.com" },
            "email-missing" => ValidPerson with { OriginChannelCode = "SENDGRID", Phone = "+50588887777", Email = null },
            "no-contact" => ValidPerson with { OriginChannelCode = "TELEGRAM", Phone = null, Email = null },
            "reason" => ValidPerson with { Reason = new string('x', 501) },
            "legal-without-commercial-name" => ValidPerson with { PersonType = PersonType.Legal, LastName = null },
            "legal-with-gender" => ValidPerson with { PersonType = PersonType.Legal, CommercialName = "Acme", GenderId = Guid.NewGuid() },
            "last-name" => ValidPerson with { LastName = " " },
            _ => ValidPerson with { PersonType = (PersonType)9 }
        };

        new RegisterIntakePersonCommandValidator().Validate(command).IsValid.Should().BeFalse(scenario);
    }

    [Fact]
    public void CreateEvent_ShouldRequireTheTraceabilityFields()
    {
        var validator = new CreateIntakeEventCommandValidator();

        validator.Validate(ValidEvent).IsValid.Should().BeTrue();
        validator.Validate(ValidEvent with { OriginType = "Email" }).IsValid.Should().BeTrue();
        validator.Validate(ValidEvent with { OriginReference = "" }).IsValid.Should().BeFalse();
        validator.Validate(ValidEvent with { RequesterPersonId = Guid.Empty }).IsValid.Should().BeFalse();
        validator.Validate(ValidEvent with { OriginType = "User" }).IsValid.Should().BeFalse();
        validator.Validate(ValidEvent with { Start = null }).IsValid.Should().BeFalse();
        validator.Validate(ValidEvent with { IsAllDay = true, Start = null }).IsValid.Should().BeFalse("startDate is required");
        validator.Validate(ValidEvent with { Title = new string('x', 201) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void RescheduleAndCancel_ShouldRequireTheRequesterAndTheReason()
    {
        var reschedule = new RescheduleIntakeEventCommand(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, null, "WHATSAPP", "+505", "wamid", null);
        var cancel = new CancelIntakeEventCommand(Guid.NewGuid(), Guid.NewGuid(), "No puede", null, null, "WHATSAPP", "+505");

        new RescheduleIntakeEventCommandValidator().Validate(reschedule).IsValid.Should().BeTrue();
        new RescheduleIntakeEventCommandValidator().Validate(reschedule with { End = reschedule.Start.AddHours(-1) }).IsValid.Should().BeFalse();
        new RescheduleIntakeEventCommandValidator().Validate(reschedule with { OriginReference = " " }).IsValid.Should().BeFalse();
        new RescheduleIntakeEventCommandValidator().Validate(reschedule with { Start = default }).IsValid.Should().BeFalse();
        new CancelIntakeEventCommandValidator().Validate(cancel).IsValid.Should().BeTrue();
        new CancelIntakeEventCommandValidator().Validate(cancel with { Reason = "" }).IsValid.Should().BeFalse();
        new CancelIntakeEventCommandValidator().Validate(cancel with { RequesterPersonId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PendingInbox_ShouldRequireNotesToDiscardAndACustomerToLink()
    {
        var resolve = new ResolvePersonPendingConfirmationCommandValidator();
        var id = Guid.NewGuid();

        resolve.Validate(new ResolvePersonPendingConfirmationCommand(id, PersonPendingConfirmationStatus.Confirmed, null)).IsValid.Should().BeTrue();
        resolve.Validate(new ResolvePersonPendingConfirmationCommand(id, PersonPendingConfirmationStatus.Discarded, " ")).IsValid.Should().BeFalse();
        resolve.Validate(new ResolvePersonPendingConfirmationCommand(id, PersonPendingConfirmationStatus.Discarded, "Spam")).IsValid.Should().BeTrue();
        resolve.Validate(new ResolvePersonPendingConfirmationCommand(id, PersonPendingConfirmationStatus.LinkedToCustomer, null)).IsValid.Should().BeFalse();
        resolve.Validate(new ResolvePersonPendingConfirmationCommand(id, default, null)).IsValid.Should().BeFalse();

        new LinkCustomerToPersonPendingConfirmationCommandValidator().Validate(new LinkCustomerToPersonPendingConfirmationCommand(id, Guid.Empty)).IsValid.Should().BeFalse();
        new LinkCustomerToPersonPendingConfirmationCommandValidator().Validate(new LinkCustomerToPersonPendingConfirmationCommand(id, Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
