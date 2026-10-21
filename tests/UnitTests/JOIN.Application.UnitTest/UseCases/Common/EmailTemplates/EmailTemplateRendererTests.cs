using FluentAssertions;
using JOIN.Application.Services.Email;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;

/// <summary>
/// SPEC 71 — the closed list of variables: extraction, validation and rendering. A variable without
/// value renders as empty text and an unknown key is reported when the template is saved.
/// </summary>
public sealed class EmailTemplateRendererTests
{
    private readonly EmailTemplateRenderer _renderer = new();

    private static EmailTemplateContext Context(
        string personaNombre = "Ana",
        string personaNombreCompleto = "Ana López",
        string personaCorreo = "ana@acme.test",
        string clienteCodigo = "C-001",
        string leadCodigo = "L-007",
        string leadTema = "Renovación",
        string usuarioNombre = "Remitente Acme",
        string usuarioCorreo = "remitente@acme.test",
        string empresaNombre = "Acme",
        string fechaHoy = "20/10/2026") =>
        new(personaNombre, personaNombreCompleto, personaCorreo, clienteCodigo, leadCodigo, leadTema,
            usuarioNombre, usuarioCorreo, empresaNombre, fechaHoy);

    [Fact]
    public void ExtractVariables_ShouldReturnTheKeysWithoutBraces_AndWithoutDuplicates()
    {
        _renderer.ExtractVariables("Hola {{Persona.Nombre}}, de {{Empresa.Nombre}} (otra vez {{Persona.Nombre}})")
            .Should().Equal("Persona.Nombre", "Empresa.Nombre");

        _renderer.ExtractVariables("sin variables").Should().BeEmpty();
        _renderer.ExtractVariables(null).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldReportOnlyTheUnknownVariables()
    {
        _renderer.Validate("Hola {{Persona.Apodo}}", "<p>{{Lead.Tema}} y {{Persona.Nombre}}</p>")
            .Should().Equal("Persona.Apodo");

        _renderer.Validate("Hola {{Persona.Nombre}}", "<p>{{Empresa.Nombre}}</p>").Should().BeEmpty();
    }

    [Fact]
    public void Render_ShouldReplaceEveryKnownVariable()
    {
        var rendered = _renderer.Render("Hola {{Persona.Nombre}}", "<p>{{Lead.Tema}} / {{Fecha.Hoy}}</p>", Context());

        rendered.Subject.Should().Be("Hola Ana");
        rendered.BodyHtml.Should().Be("<p>Renovación / 20/10/2026</p>");
    }

    [Fact]
    public void Render_ShouldReplaceAVariableWithoutValueWithEmptyText()
    {
        var rendered = _renderer.Render("Hola {{Lead.Codigo}}", "<p>{{Lead.Codigo}}</p>", Context(leadCodigo: string.Empty));

        rendered.Subject.Should().Be("Hola ");
        rendered.BodyHtml.Should().Be("<p></p>");
    }

    [Fact]
    public void Render_ShouldLeaveAnUnknownKeyUntouched()
    {
        _renderer.Render("Hola {{Persona.Apodo}}", string.Empty, Context()).Subject
            .Should().Be("Hola {{Persona.Apodo}}");
    }

    [Fact]
    public void Render_ShouldEncodeTheValuesOfTheBody()
    {
        var rendered = _renderer.Render(string.Empty, "<p>{{Persona.Nombre}}</p>", Context(personaNombre: "<script>x</script>"));

        rendered.BodyHtml.Should().Be("<p>&lt;script&gt;x&lt;/script&gt;</p>");
    }

    [Fact]
    public void Variables_ShouldExposeTheClosedListWithItsDescriptions()
    {
        EmailTemplateVariables.All.Should().HaveCount(10);
        EmailTemplateVariables.All.Should().Contain(v => v.Key == "Persona.Nombre" && v.Description.Length > 0);
        EmailTemplateVariables.IsKnown("persona.nombre").Should().BeTrue();
        EmailTemplateVariables.IsKnown("Persona.Apodo").Should().BeFalse();
        EmailTemplateVariables.ValueOf("Persona.Apodo", Context()).Should().BeNull();
    }

    [Fact]
    public void Render_ShouldReplaceEveryVariableOfTheClosedList()
    {
        var body = string.Join(" | ", EmailTemplateVariables.All.Select(v => $"{{{{{v.Key}}}}}"));

        var rendered = _renderer.Render(string.Empty, body, Context());

        rendered.BodyHtml.Should().Be(
            "Ana | Ana López | ana@acme.test | C-001 | L-007 | Renovación | Remitente Acme | remitente@acme.test | Acme | 20/10/2026");
    }

    [Fact]
    public void Render_WithAnEmptyContext_RendersEveryVariableAsEmptyText()
    {
        var body = string.Join("|", EmailTemplateVariables.All.Select(v => $"{{{{{v.Key}}}}}"));

        var rendered = _renderer.Render(string.Empty, body, EmailTemplateContext.Empty);

        rendered.BodyHtml.Should().Be(new string('|', EmailTemplateVariables.All.Count - 1));
    }
}
