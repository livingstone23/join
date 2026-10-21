using FluentAssertions;
using FluentValidation;
using JOIN.Application.UseCases.Common.EmailTemplates.Commands;
using JOIN.Application.UseCases.Common.EmailTemplates.Queries;
using JOIN.Application.UseCases.Common.EmailTemplateTypes.Commands;
using JOIN.Application.UseCases.Common.EmailTemplateTypes.Queries;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates.Commands.SaveEmailTemplate;

/// <summary>
/// SPEC 71 — the validators of the commands: the identifiers, the names and the lengths of the fields.
/// </summary>
public sealed class EmailTemplateValidatorTests
{
    [Fact]
    public void CreateType_RejectsAnEmptyCodeAndName()
    {
        var result = new CreateEmailTemplateTypeCommandValidator().Validate(new CreateEmailTemplateTypeCommand
        {
            Code = string.Empty,
            Name = string.Empty
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEmailTemplateTypeCommand.Code));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEmailTemplateTypeCommand.Name));
    }

    [Fact]
    public void CreateType_RejectsANegativeSortOrder()
    {
        var result = new CreateEmailTemplateTypeCommandValidator().Validate(new CreateEmailTemplateTypeCommand
        {
            Code = "INTRO",
            Name = "Presentación",
            SortOrder = -1
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEmailTemplateTypeCommand.SortOrder));
    }

    [Fact]
    public void UpdateType_RejectsAnEmptyId()
    {
        var result = new UpdateEmailTemplateTypeCommandValidator().Validate(new UpdateEmailTemplateTypeCommand
        {
            Code = "INTRO",
            Name = "Presentación"
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateEmailTemplateTypeCommand.Id));
    }

    [Fact]
    public void CreateTemplate_RejectsAnEmptyTypeAndAnEmptyBody()
    {
        var result = new CreateEmailTemplateCommandValidator().Validate(new CreateEmailTemplateCommand
        {
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = string.Empty
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEmailTemplateCommand.EmailTemplateTypeId));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEmailTemplateCommand.BodyHtml));
    }

    [Fact]
    public void CreateTemplate_RejectsALongBody()
    {
        var result = new CreateEmailTemplateCommandValidator().Validate(new CreateEmailTemplateCommand
        {
            EmailTemplateTypeId = Guid.NewGuid(),
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = new string('a', 50_001)
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEmailTemplateCommand.BodyHtml));
    }

    [Fact]
    public void UpdateTemplate_RejectsAnEmptyIdAndAnEmptySubject()
    {
        var result = new UpdateEmailTemplateCommandValidator().Validate(new UpdateEmailTemplateCommand
        {
            EmailTemplateTypeId = Guid.NewGuid(),
            Name = "Bienvenida",
            Subject = string.Empty,
            BodyHtml = "<p>Hola</p>"
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateEmailTemplateCommand.Id));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateEmailTemplateCommand.Subject));
    }
}
