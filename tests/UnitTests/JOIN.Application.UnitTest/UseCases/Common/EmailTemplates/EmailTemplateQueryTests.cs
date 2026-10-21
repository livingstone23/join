using FluentAssertions;
using JOIN.Application.Interface;
using Moq;
using JOIN.Application.Common;
using JOIN.Application.Services.Email;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Common.EmailTemplates;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;

/// <summary>
/// SPEC 71 — the queries of the email templates (Dapper): the paged list, the detail, the catalog of
/// types, the picker of the sending modal and the closed list of variables.
/// </summary>
public sealed class EmailTemplateQueryTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly IOptions<PaginationSettings> _paging = Options.Create(new PaginationSettings());

    public EmailTemplateQueryTests()
    {
        _user.SetupGet(x => x.CompanyId).Returns(() => _companyId);
        _user.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
    }

    private static Dictionary<string, object?> TemplateRow(Guid typeId, string name = "Bienvenida") => new()
    {
        ["Id"] = Guid.NewGuid(),
        ["CompanyId"] = Guid.NewGuid(),
        ["EmailTemplateTypeId"] = typeId,
        ["EmailTemplateTypeName"] = "Presentación",
        ["EmailTemplateTypeCode"] = "INTRO",
        ["Name"] = name,
        ["Subject"] = "Hola",
        ["BodyHtml"] = "<p>Hola</p>",
        ["IsActive"] = true,
        ["CreatedAt"] = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task List_FiltersByTheTenantTheSearchAndTheType()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(TemplateRow(Guid.NewGuid())), FakeResultSet.FromScalar(1));
        var typeId = Guid.NewGuid();

        var response = await new GetEmailTemplatesQueryHandler(factory, _paging, _user.Object).Handle(
            new GetEmailTemplatesQuery(Search: "bien", EmailTemplateTypeId: typeId, IsActive: true, PageNumber: 2, PageSize: 5),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.TotalCount.Should().Be(1);
        response.Data.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(5);

        factory.CapturedParameters["CompanyId"].Should().Be(_companyId);
        factory.CapturedParameters["Search"].Should().Be("%bien%");
        factory.CapturedParameters["EmailTemplateTypeId"].Should().Be(typeId);
        factory.CapturedParameters["IsActive"].Should().Be(true);
        factory.CapturedParameters["Offset"].Should().Be(5);
        factory.CapturedParameters["PageSize"].Should().Be(5);

        factory.LastCommandText.Should().Contain("FROM admin.emailtemplates t")
            .And.Contain("t.CompanyId = @CompanyId AND t.GcRecord = 0")
            .And.Contain("(t.Name ILIKE @Search OR t.Subject ILIKE @Search)")
            .And.Contain("t.EmailTemplateTypeId = @EmailTemplateTypeId")
            .And.Contain("t.IsActive = @IsActive")
            .And.Contain("LIMIT @PageSize OFFSET @Offset")
            .And.Contain("SELECT COUNT(*)");
    }

    [Fact]
    public async Task List_FailsWhenTheCompanyOfTheTokenIsEmpty()
    {
        var factory = new FakeSqlConnectionFactory();
        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await new GetEmailTemplatesQueryHandler(factory, _paging, _user.Object).Handle(
            new GetEmailTemplatesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Detail_ReturnsTheTemplate_OfTheCompany()
    {
        var factory = new FakeSqlConnectionFactory();
        var id = Guid.NewGuid();
        factory.SetResults(FakeResultSet.FromRows(TemplateRow(Guid.NewGuid())));
        var handler = new GetEmailTemplateByIdQueryHandler(factory, _user.Object);

        var response = await handler.Handle(new GetEmailTemplateByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        factory.CapturedParameters["Id"].Should().Be(id);
        factory.CapturedParameters["CompanyId"].Should().Be(_companyId);
        factory.LastCommandText.Should().Contain("WHERE t.Id = @Id")
            .And.Contain("t.CompanyId = @CompanyId")
            .And.Contain("t.GcRecord = 0");
    }

    [Fact]
    public async Task Detail_FailsWhenTheTemplateDoesNotExist()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"));

        var response = await new GetEmailTemplateByIdQueryHandler(factory, _user.Object).Handle(
            new GetEmailTemplateByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateNotFound);
    }

    [Fact]
    public async Task Types_ListTheCatalogOfTheCompany()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["CompanyId"] = _companyId,
            ["Code"] = "INTRO",
            ["Name"] = "Presentación",
            ["SortOrder"] = 1,
            ["IsActive"] = true,
            ["CreatedAt"] = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)
        }));

        var response = await new GetEmailTemplateTypesQueryHandler(factory, _user.Object).Handle(
            new GetEmailTemplateTypesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data.Should().ContainSingle().Which.Code.Should().Be("INTRO");
        factory.CapturedParameters["CompanyId"].Should().Be(_companyId);
        factory.CapturedParameters["IncludeInactive"].Should().Be(false);
        factory.LastCommandText.Should().Contain("FROM admin.emailtemplatetypes")
            .And.Contain("GcRecord = 0")
            .And.Contain("(@IncludeInactive = TRUE OR IsActive = TRUE)");
    }

    [Fact]
    public async Task Types_IncludeTheInactiveOnes_WhenAsked()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"));

        await new GetEmailTemplateTypesQueryHandler(factory, _user.Object).Handle(
            new GetEmailTemplateTypesQuery(IncludeInactive: true), CancellationToken.None);

        factory.CapturedParameters["IncludeInactive"].Should().Be(true);
    }

    [Fact]
    public async Task Lookup_ReturnsOnlyTheActiveTemplates_AndCapsTheResult()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["Name"] = "Bienvenida",
            ["Subject"] = "Hola",
            ["EmailTemplateTypeId"] = Guid.NewGuid(),
            ["EmailTemplateTypeName"] = "Presentación",
            ["EmailTemplateTypeCode"] = "INTRO"
        }));
        var typeId = Guid.NewGuid();

        var response = await new LookupEmailTemplatesQueryHandler(factory, _user.Object).Handle(
            new LookupEmailTemplatesQuery(typeId, "bien"), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data.Should().ContainSingle().Which.Name.Should().Be("Bienvenida");
        factory.CapturedParameters["MaxResults"].Should().Be(50);
        factory.CapturedParameters["EmailTemplateTypeId"].Should().Be(typeId);
        factory.CapturedParameters["Search"].Should().Be("%bien%");
        factory.LastCommandText.Should().Contain("t.IsActive = TRUE")
            .And.Contain("LIMIT @MaxResults");
    }

    [Fact]
    public async Task Lookup_FailsWhenTheCompanyOfTheTokenIsEmpty()
    {
        var factory = new FakeSqlConnectionFactory();
        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await new LookupEmailTemplatesQueryHandler(factory, _user.Object).Handle(
            new LookupEmailTemplatesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Variables_ReturnTheClosedList()
    {
        var response = await new GetEmailTemplateVariablesQueryHandler().Handle(
            new GetEmailTemplateVariablesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data.Should().HaveCount(10);
        response.Data.Should().Contain(v => v.Key == "Persona.Nombre");
        response.Data.Should().OnlyContain(v => v.Description.Length > 0);
    }
}
