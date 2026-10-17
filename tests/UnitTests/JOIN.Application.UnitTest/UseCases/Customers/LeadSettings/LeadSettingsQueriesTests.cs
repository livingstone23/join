using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypeCompanies.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypeCompanies.Queries;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Queries;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Queries;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Queries;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Queries;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Queries;
using JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers.LeadSettings;

/// <summary>
/// SPEC 59 queries (tenant, soft delete and filters) and validators.
/// </summary>
public sealed class LeadSettingsQueriesTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Mock<ICurrentUserService> _user = new();

    public LeadSettingsQueriesTests()
    {
        _user.SetupGet(x => x.CompanyId).Returns(() => _companyId);
    }

    [Fact]
    public async Task CatalogLists_ShouldScopeToTheCompany_AndHideDeletedAndInactive()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id", "Code"));

        var response = await new GetLeadStagesQueryHandler(factory, _user.Object)
            .Handle(new GetLeadStagesQuery(IncludeInactive: false, CompanyId: Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeTrue(response.Message);
        factory.CapturedParameters["CompanyId"].Should().Be(_companyId);
        factory.LastCommandText.Should().Contain("customer.leadstages").And.Contain("t.GcRecord = 0").And.Contain("t.IsActive = TRUE")
            .And.Contain("t.Probability, t.Kind").And.Contain("ORDER BY t.SortOrder");
    }

    [Fact]
    public async Task LeadReasons_ShouldFilterByUsedFor()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Code"] = "POSTPONED", ["Name"] = "Pospuesto", ["UsedFor"] = 3 }));

        var response = await new GetLeadReasonsQueryHandler(factory, _user.Object).Handle(new GetLeadReasonsQuery(UsedFor: 3), CancellationToken.None);

        response.Data!.Single().UsedFor.Should().Be(3);
        factory.CapturedParameters["UsedFor"].Should().Be(3);
        factory.LastCommandText.Should().Contain("t.UsedFor = @UsedFor");
    }

    [Fact]
    public async Task OtherLists_ShouldReadTheirTables()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));

        (await new GetLeadSourcesQueryHandler(factory, _user.Object).Handle(new GetLeadSourcesQuery(), CancellationToken.None)).IsSuccess.Should().BeTrue();
        factory.LastCommandText.Should().Contain("customer.leadsources").And.Contain("t.Description");
        (await new GetRelationshipTypesQueryHandler(factory, _user.Object).Handle(new GetRelationshipTypesQuery(), CancellationToken.None)).IsSuccess.Should().BeTrue();
        factory.LastCommandText.Should().Contain("customer.relationshiptypes").And.Contain("t.OnWin");
        (await new GetLeadReasonsQueryHandler(factory, _user.Object).Handle(new GetLeadReasonsQuery(), CancellationToken.None)).IsSuccess.Should().BeTrue();
        factory.LastCommandText.Should().NotContain("@UsedFor");
    }

    [Fact]
    public async Task GetById_ShouldReturnNotFound_ForEveryCatalog()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));
        var id = Guid.NewGuid();

        (await new GetLeadStageByIdQueryHandler(factory, _user.Object).Handle(new GetLeadStageByIdQuery(id), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.LeadStageNotFound);
        (await new GetLeadSourceByIdQueryHandler(factory, _user.Object).Handle(new GetLeadSourceByIdQuery(id), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.LeadSourceNotFound);
        (await new GetLeadReasonByIdQueryHandler(factory, _user.Object).Handle(new GetLeadReasonByIdQuery(id), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.LeadReasonNotFound);
        (await new GetRelationshipTypeByIdQueryHandler(factory, _user.Object).Handle(new GetRelationshipTypeByIdQuery(id), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.RelationshipTypeNotFound);
    }

    [Fact]
    public async Task GetById_ShouldReturnTheRow()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Code"] = "WON", ["Name"] = "Ganado", ["Kind"] = 3, ["Probability"] = 100 }));

        var response = await new GetLeadStageByIdQueryHandler(factory, _user.Object).Handle(new GetLeadStageByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Data!.Kind.Should().Be(3);
        factory.LastCommandText.Should().Contain("t.Id = @Id").And.Contain("t.CompanyId = @CompanyId");
    }

    [Fact]
    public async Task Queries_WithoutCompany_ShouldFail()
    {
        _user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        var factory = new FakeSqlConnectionFactory();

        (await new GetLeadStagesQueryHandler(factory, _user.Object).Handle(new GetLeadStagesQuery(), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
        (await new GetLeadStageByIdQueryHandler(factory, _user.Object).Handle(new GetLeadStageByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
        (await new GetContactTypeCompaniesQueryHandler(factory, _user.Object).Handle(new GetContactTypeCompaniesQuery(), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
        (await new GetLeadCompanySettingsQueryHandler(factory, _user.Object).Handle(new GetLeadCompanySettingsQuery(), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task ContactTypes_ShouldRequireSuperAdmin_AndReadTheMaster()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));

        (await new GetContactTypesQueryHandler(factory, _user.Object).Handle(new GetContactTypesQuery(), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.SuperAdminRequired);
        (await new GetContactTypeByIdQueryHandler(factory, _user.Object).Handle(new GetContactTypeByIdQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadSettingsErrorCodes.SuperAdminRequired);

        _user.Setup(x => x.IsInRole("SuperAdmin")).Returns(true);
        (await new GetContactTypesQueryHandler(factory, _user.Object).Handle(new GetContactTypesQuery(IncludeInactive: false), CancellationToken.None)).IsSuccess.Should().BeTrue();
        factory.LastCommandText.Should().Contain("customer.contacttypes").And.Contain("t.GcRecord = 0").And.Contain("t.IsActive = TRUE");
        (await new GetContactTypeByIdQueryHandler(factory, _user.Object).Handle(new GetContactTypeByIdQuery(Guid.NewGuid(), IncludeDeleted: true), CancellationToken.None))
            .Message.Should().Be(LeadSettingsErrorCodes.ContactTypeNotFound);
        factory.LastCommandText.Should().NotContain("GcRecord = 0");
    }

    [Fact]
    public async Task ContactTypeCompanies_ShouldJoinTheLiveMaster()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Code"] = "CALL", ["Name"] = "Llamada", ["MasterName"] = "Llamada" }));

        var response = await new GetContactTypeCompaniesQueryHandler(factory, _user.Object).Handle(new GetContactTypeCompaniesQuery(IncludeInactive: false), CancellationToken.None);

        response.Data!.Single().Code.Should().Be("CALL");
        factory.CapturedParameters["CompanyId"].Should().Be(_companyId);
        factory.LastCommandText.Should().Contain("INNER JOIN customer.contacttypes t ON t.Id = c.ContactTypeId AND t.GcRecord = 0")
            .And.Contain("c.IsActive = TRUE AND t.IsActive = TRUE");
    }

    [Fact]
    public async Task CompanySettings_WithoutRow_ShouldReturnTheDefaults()
    {
        var factory = new FakeSqlConnectionFactory();
        factory.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(), ["CompanyId"] = _companyId, ["DuplicateOpenLeadPolicy"] = 2, ["RequireIdentificationForCustomer"] = false, ["CodePrefix"] = "OP-", ["CodeSequenceLength"] = 4
        }));
        var handler = new GetLeadCompanySettingsQueryHandler(factory, _user.Object);

        var defaults = (await handler.Handle(new GetLeadCompanySettingsQuery(), CancellationToken.None)).Data!;
        var stored = (await handler.Handle(new GetLeadCompanySettingsQuery(), CancellationToken.None)).Data!;

        defaults.IsDefault.Should().BeTrue();
        defaults.Id.Should().BeNull();
        defaults.CompanyId.Should().Be(_companyId);
        defaults.DuplicateOpenLeadPolicy.Should().Be(1);
        defaults.RequireIdentificationForCustomer.Should().BeTrue();
        defaults.CodePrefix.Should().Be("LD-");
        defaults.CodeSequenceLength.Should().Be(6);
        stored.IsDefault.Should().BeFalse();
        stored.CodePrefix.Should().Be("OP-");
    }

    [Fact]
    public void Validators_ShouldRejectInvalidInput()
    {
        var stage = new CreateLeadStageCommandValidator().TestValidate(new CreateLeadStageCommand { Code = " ", Name = "", Probability = 101, Kind = 9 });
        stage.ShouldHaveValidationErrorFor(x => x.Code);
        stage.ShouldHaveValidationErrorFor(x => x.Name);
        stage.ShouldHaveValidationErrorFor(x => x.Probability);
        stage.ShouldHaveValidationErrorFor(x => x.Kind);
        new UpdateLeadStageCommandValidator().TestValidate(new UpdateLeadStageCommand { Code = new string('A', 31), Name = "N" })
            .ShouldHaveValidationErrorFor(x => x.Code);
        new UpdateLeadStageCommandValidator().TestValidate(new UpdateLeadStageCommand { Code = "A", Name = "N" }).ShouldHaveValidationErrorFor(x => x.Id);

        new CreateLeadReasonCommandValidator().TestValidate(new CreateLeadReasonCommand { Code = "A", Name = "A", UsedFor = 0 }).ShouldHaveValidationErrorFor(x => x.UsedFor);
        new UpdateLeadReasonCommandValidator().TestValidate(new UpdateLeadReasonCommand { Id = Guid.NewGuid(), Code = "A", Name = "A", UsedFor = 3 }).ShouldNotHaveAnyValidationErrors();
        new CreateRelationshipTypeCommandValidator().TestValidate(new CreateRelationshipTypeCommand { Code = "A", Name = "A", OnWin = 5 }).ShouldHaveValidationErrorFor(x => x.OnWin);
        new UpdateRelationshipTypeCommandValidator().TestValidate(new UpdateRelationshipTypeCommand { Code = "A", Name = "A" }).ShouldHaveValidationErrorFor(x => x.Id);
        new CreateLeadSourceCommandValidator().TestValidate(new CreateLeadSourceCommand { Code = "A", Name = "A", Description = new string('d', 501) }).ShouldHaveValidationErrorFor(x => x.Description);
        new UpdateLeadSourceCommandValidator().TestValidate(new UpdateLeadSourceCommand { Id = Guid.NewGuid(), Code = "A", Name = "A" }).ShouldNotHaveAnyValidationErrors();

        var type = new CreateContactTypeCommandValidator().TestValidate(new CreateContactTypeCommand { Code = "", Name = "", Icon = "" });
        type.ShouldHaveValidationErrorFor(x => x.Code);
        type.ShouldHaveValidationErrorFor(x => x.Name);
        type.ShouldHaveValidationErrorFor(x => x.Icon);
        new UpdateContactTypeCommandValidator().TestValidate(new UpdateContactTypeCommand { Code = "A", Name = "A", Icon = "a" }).ShouldHaveValidationErrorFor(x => x.Id);
        new UpdateContactTypeCompanyCommandValidator().TestValidate(new UpdateContactTypeCompanyCommand { Name = "" }).ShouldHaveValidationErrorFor(x => x.Name);

        var settings = new SaveLeadCompanySettingsCommandValidator().TestValidate(new SaveLeadCompanySettingsCommand { DuplicateOpenLeadPolicy = 0, CodePrefix = " ", CodeSequenceLength = 2 });
        settings.ShouldHaveValidationErrorFor(x => x.DuplicateOpenLeadPolicy);
        settings.ShouldHaveValidationErrorFor(x => x.CodePrefix);
        settings.ShouldHaveValidationErrorFor(x => x.CodeSequenceLength);
        new SaveLeadCompanySettingsCommandValidator().TestValidate(new SaveLeadCompanySettingsCommand { CodePrefix = new string('P', 11), CodeSequenceLength = 11 })
            .ShouldHaveValidationErrorFor(x => x.CodePrefix);
        new SaveLeadCompanySettingsCommandValidator().TestValidate(new SaveLeadCompanySettingsCommand()).ShouldNotHaveAnyValidationErrors();
    }
}
