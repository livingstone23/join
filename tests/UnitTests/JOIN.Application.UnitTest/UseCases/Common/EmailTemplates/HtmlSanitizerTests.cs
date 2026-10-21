using FluentAssertions;
using JOIN.Infrastructure.Email;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;

/// <summary>
/// SPEC 71 — the sanitizer of the body of a template: the dangerous blocks and the event handlers
/// are removed, the harmless markup survives.
/// </summary>
public sealed class HtmlSanitizerTests
{
    private readonly HtmlSanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_RemovesTheScriptBlockAndItsContent()
    {
        var result = _sanitizer.Sanitize("<p>Hola</p><script>alert('x')</script>");

        result.Should().NotContain("script").And.NotContain("alert");
        result.Should().Contain("Hola");
    }

    [Fact]
    public void Sanitize_RemovesTheIframeAndTheEventHandlers()
    {
        var result = _sanitizer.Sanitize("<div onclick=\"steal()\"><iframe src=\"http://x\"></iframe>Hola</div>");

        result.Should().NotContain("onclick").And.NotContain("iframe");
        result.Should().Contain("Hola");
    }

    [Fact]
    public void Sanitize_KeepsTheHarmlessMarkup()
    {
        var result = _sanitizer.Sanitize("<p><strong>Hola</strong> {{Persona.Nombre}}</p>");

        result.Should().Contain("<strong>").And.Contain("{{Persona.Nombre}}");
    }

    [Fact]
    public void Sanitize_ReturnsEmptyTextForAnEmptyBody()
    {
        _sanitizer.Sanitize(null).Should().BeEmpty();
        _sanitizer.Sanitize("   ").Should().BeEmpty();
    }
}
