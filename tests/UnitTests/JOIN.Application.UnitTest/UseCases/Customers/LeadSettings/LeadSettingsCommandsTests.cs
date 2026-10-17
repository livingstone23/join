using FluentAssertions;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.Customers.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypeCompanies.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Customers.LeadSettings;

/// <summary>
/// SPEC 59: commercial catalogs, the lead stage kinds, contact types (master + company) and the company rules.
/// </summary>
public sealed class LeadSettingsCommandsTests
{
    private static async Task<Dictionary<string, LeadStageDto>> SeedStagesAsync(CustomerTestContext ctx)
    {
        var result = new Dictionary<string, LeadStageDto>();
        foreach (var (code, kind, probability) in new[] { ("NEW", 1, 10), ("PROPOSAL", 2, 60), ("WON", 3, 100), ("LOST", 4, 0) })
        {
            var created = await ctx.SendAsync(new CreateLeadStageCommand { Code = code, Name = code, Kind = kind, Probability = probability });
            created.IsSuccess.Should().BeTrue(created.Message);
            result[code] = created.Data!;
        }

        return result;
    }

    [Fact]
    public async Task Catalogs_ShouldNormalizeCode_AndRejectDuplicateCodeOrName()
    {
        using var ctx = await CustomerTestContext.CreateAsync();

        var source = await ctx.SendAsync(new CreateLeadSourceCommand { Code = " wa in ", Name = " WhatsApp ", Description = "  ", SortOrder = 2 });

        source.IsSuccess.Should().BeTrue(source.Message);
        source.Data!.Code.Should().Be("WAIN");
        source.Data.Name.Should().Be("WhatsApp");
        source.Data.Description.Should().BeNull();
        (await ctx.SendAsync(new CreateLeadSourceCommand { Code = "WAIN", Name = "Other" })).Message.Should().Be(LeadSettingsErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new CreateLeadSourceCommand { Code = "X", Name = "whatsapp" })).Message.Should().Be(LeadSettingsErrorCodes.DuplicateKey);

        var reason = await ctx.SendAsync(new CreateLeadReasonCommand { Code = "NO_BUDGET", Name = "Sin presupuesto", UsedFor = 2 });
        reason.Data!.UsedFor.Should().Be(2);
        var updated = await ctx.SendAsync(new UpdateLeadReasonCommand { Id = reason.Data.Id, Code = "NO_BUDGET", Name = "Sin dinero", UsedFor = 3, IsActive = false });
        updated.Data!.Name.Should().Be("Sin dinero");
        updated.Data.UsedFor.Should().Be(3);
        updated.Data.IsActive.Should().BeFalse();

        var relationship = await ctx.SendAsync(new CreateRelationshipTypeCommand { Code = "DISTRIBUTOR", Name = "Distribuidor", OnWin = 1 });
        relationship.Data!.OnWin.Should().Be(1);
        (await ctx.SendAsync(new UpdateRelationshipTypeCommand { Id = Guid.NewGuid(), Code = "A", Name = "A" })).Message.Should().Be(LeadSettingsErrorCodes.RelationshipTypeNotFound);
        (await ctx.SendAsync(new UpdateLeadSourceCommand { Id = source.Data.Id, Code = "WEB", Name = "Web", Description = " Sitio " })).Data!.Description.Should().Be("Sitio");
    }

    [Fact]
    public async Task Catalogs_ShouldBeIsolatedPerCompany_AndRequireACompany()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var mine = (await ctx.SendAsync(new CreateLeadSourceCommand { Code = "WEB", Name = "Web" })).Data!;

        ctx.ActingCompanyId = Guid.NewGuid();
        (await ctx.SendAsync(new CreateLeadSourceCommand { Code = "WEB", Name = "Web" })).IsSuccess.Should().BeTrue("another company");
        (await ctx.SendAsync(new UpdateLeadSourceCommand { Id = mine.Id, Code = "WEB", Name = "Hack" })).Message.Should().Be(LeadSettingsErrorCodes.LeadSourceNotFound);
        (await ctx.SendAsync(new DeleteLeadSourceCommand(mine.Id))).Message.Should().Be(LeadSettingsErrorCodes.LeadSourceNotFound);

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new CreateLeadSourceCommand { Code = "A", Name = "A" })).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateLeadSourceCommand { Id = mine.Id, Code = "A", Name = "A" })).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteLeadSourceCommand(mine.Id))).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new RestoreLeadSourceCommand(mine.Id))).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task DeleteAndRestore_ShouldBeLogical_AndRestoreNeedsSuperAdminAndAFreeKey()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var reason = (await ctx.SendAsync(new CreateLeadReasonCommand { Code = "POSTPONED", Name = "Pospuesto", UsedFor = 3 })).Data!;
        var type = (await ctx.SendAsync(new CreateRelationshipTypeCommand { Code = "PARTNER", Name = "Socio" })).Data!;

        (await ctx.SendAsync(new DeleteLeadReasonCommand(reason.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteRelationshipTypeCommand(type.Id))).IsSuccess.Should().BeTrue();
        (await ctx.Db.LeadReasons.IgnoreQueryFilters().SingleAsync(r => r.Id == reason.Id)).GcRecord.Should().NotBe(0);

        (await ctx.SendAsync(new RestoreLeadReasonCommand(reason.Id))).Message.Should().Be(LeadSettingsErrorCodes.SuperAdminRequired);
        ctx.IsSuperAdmin = true;
        (await ctx.SendAsync(new CreateLeadReasonCommand { Code = "POSTPONED", Name = "Otro", UsedFor = 3 })).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new RestoreLeadReasonCommand(reason.Id))).Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        (await ctx.SendAsync(new RestoreRelationshipTypeCommand(type.Id))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LeadStages_ShouldKeepOneInitialWonAndLost()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var stages = await SeedStagesAsync(ctx);

        (await ctx.SendAsync(new CreateLeadStageCommand { Code = "NEW2", Name = "Nuevo 2", Kind = 1 })).Message.Should().Be(LeadSettingsErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new CreateLeadStageCommand { Code = "WON2", Name = "Ganado 2", Kind = 3 })).Message.Should().Be(LeadSettingsErrorCodes.DuplicateKey);
        (await ctx.SendAsync(new UpdateLeadStageCommand { Id = stages["PROPOSAL"].Id, Code = "PROPOSAL", Name = "P", Kind = 4 })).Message.Should().Be(LeadSettingsErrorCodes.DuplicateKey);

        var won = stages["WON"];
        var renamed = await ctx.SendAsync(new UpdateLeadStageCommand { Id = won.Id, Code = "won", Name = "Cerrado ganado", Kind = 3, Probability = 100, SortOrder = 9 });
        renamed.IsSuccess.Should().BeTrue(renamed.Message);
        renamed.Data!.Name.Should().Be("Cerrado ganado");

        (await ctx.SendAsync(new UpdateLeadStageCommand { Id = won.Id, Code = "WIN", Name = "W", Kind = 3 })).Message.Should().Be(LeadSettingsErrorCodes.LeadStageSystem);
        (await ctx.SendAsync(new UpdateLeadStageCommand { Id = won.Id, Code = "WON", Name = "W", Kind = 3, IsActive = false })).Message.Should().Be(LeadSettingsErrorCodes.LeadStageSystem);
        (await ctx.SendAsync(new UpdateLeadStageCommand { Id = won.Id, Code = "WON", Name = "W", Kind = 2 })).Message.Should().Be(LeadSettingsErrorCodes.LeadStageKindRequired);
        (await ctx.SendAsync(new DeleteLeadStageCommand(won.Id))).Message.Should().Be(LeadSettingsErrorCodes.LeadStageSystem);
        (await ctx.SendAsync(new DeleteLeadStageCommand(stages["LOST"].Id))).Message.Should().Be(LeadSettingsErrorCodes.LeadStageSystem);

        var initial = stages["NEW"];
        (await ctx.SendAsync(new UpdateLeadStageCommand { Id = initial.Id, Code = "NEW", Name = "N", Kind = 1, IsActive = false })).Message.Should().Be(LeadSettingsErrorCodes.LeadStageKindRequired);
        (await ctx.SendAsync(new UpdateLeadStageCommand { Id = initial.Id, Code = "NEW", Name = "N", Kind = 2 })).Message.Should().Be(LeadSettingsErrorCodes.LeadStageKindRequired);
        (await ctx.SendAsync(new DeleteLeadStageCommand(initial.Id))).Message.Should().Be(LeadSettingsErrorCodes.LeadStageKindRequired);
        (await ctx.SendAsync(new UpdateLeadStageCommand { Id = initial.Id, Code = "START", Name = "Inicio", Kind = 1 })).IsSuccess.Should().BeTrue("the Initial code can change");

        var open = stages["PROPOSAL"];
        (await ctx.SendAsync(new UpdateLeadStageCommand { Id = open.Id, Code = "PROPOSAL", Name = "Propuesta", Kind = 2, IsActive = false })).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteLeadStageCommand(open.Id))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LeadStages_Restore_ShouldNotBringBackASecondUniqueKind()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var stages = await SeedStagesAsync(ctx);
        var deletedInitial = new LeadStage { CompanyId = ctx.CompanyId, Code = "OLD_NEW", Name = "Antiguo", Kind = LeadStageKind.Initial };
        deletedInitial.MarkAsDeleted();
        ctx.Db.LeadStages.Add(deletedInitial);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        await ctx.SendAsync(new DeleteLeadStageCommand(stages["PROPOSAL"].Id));
        ctx.IsSuperAdmin = true;

        (await ctx.SendAsync(new RestoreLeadStageCommand(deletedInitial.Id))).Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        (await ctx.SendAsync(new RestoreLeadStageCommand(stages["PROPOSAL"].Id))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ContactTypes_AreSuperAdminOnly_AndANewMasterReachesEveryCompanyInactive()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var other = new Company { Name = "Other", TaxId = "OTH-1", IsActive = true };
        ctx.Db.Companies.Add(other);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        var command = new CreateContactTypeCommand { Code = " call ", Name = "Llamada", Icon = "phone", CommunicationChannelCode = " whatsapp ", AsksDuration = true, SortOrder = 1 };

        (await ctx.SendAsync(command)).Message.Should().Be(LeadSettingsErrorCodes.SuperAdminRequired);
        ctx.IsSuperAdmin = true;
        var created = await ctx.SendAsync(command);

        created.IsSuccess.Should().BeTrue(created.Message);
        created.Data!.Code.Should().Be("CALL");
        created.Data.CommunicationChannelCode.Should().Be("WHATSAPP");
        (await ctx.SendAsync(command)).Message.Should().Be(LeadSettingsErrorCodes.DuplicateKey);
        var copies = await ctx.Db.ContactTypeCompanies.IgnoreQueryFilters().Where(c => c.ContactTypeId == created.Data.Id).ToListAsync();
        copies.Select(c => c.CompanyId).Should().BeEquivalentTo(new[] { ctx.CompanyId, other.Id });
        copies.Should().OnlyContain(c => !c.IsActive && c.Name == "Llamada");

        var mine = copies.Single(c => c.CompanyId == ctx.CompanyId);
        ctx.IsSuperAdmin = false;
        var activated = await ctx.SendAsync(new UpdateContactTypeCompanyCommand { Id = mine.Id, Name = " Llamada telefónica ", SortOrder = 4, IsActive = true });
        activated.IsSuccess.Should().BeTrue(activated.Message);
        activated.Data!.Name.Should().Be("Llamada telefónica");
        activated.Data.MasterName.Should().Be("Llamada");
        activated.Data.Code.Should().Be("CALL");
        (await ctx.Db.ContactTypeCompanies.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.CompanyId == other.Id)).IsActive.Should().BeFalse();

        var otherCopy = copies.Single(c => c.CompanyId == other.Id);
        (await ctx.SendAsync(new UpdateContactTypeCompanyCommand { Id = otherCopy.Id, Name = "X" })).Message.Should().Be(LeadSettingsErrorCodes.ContactTypeNotFound);

        ctx.IsSuperAdmin = true;
        var update = await ctx.SendAsync(new UpdateContactTypeCommand { Id = created.Data.Id, Code = "CALL", Name = "Llamada", Icon = "phone_in_talk", SchedulesCalendarEvent = true });
        update.Data!.Icon.Should().Be("phone_in_talk");
        update.Data.CommunicationChannelCode.Should().BeNull();
        (await ctx.SendAsync(new UpdateContactTypeCommand { Id = Guid.NewGuid(), Code = "A", Name = "A", Icon = "a" })).Message.Should().Be(LeadSettingsErrorCodes.ContactTypeNotFound);
        (await ctx.SendAsync(new DeleteContactTypeCommand(created.Data.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteContactTypeCommand(created.Data.Id))).Message.Should().Be(LeadSettingsErrorCodes.ContactTypeNotFound);
        (await ctx.SendAsync(new UpdateContactTypeCompanyCommand { Id = mine.Id, Name = "X" })).Message.Should().Be(LeadSettingsErrorCodes.ContactTypeNotFound, "the master was deleted");

        ctx.IsSuperAdmin = false;
        (await ctx.SendAsync(new UpdateContactTypeCommand { Id = created.Data.Id, Code = "A", Name = "A", Icon = "a" })).Message.Should().Be(LeadSettingsErrorCodes.SuperAdminRequired);
        (await ctx.SendAsync(new DeleteContactTypeCommand(created.Data.Id))).Message.Should().Be(LeadSettingsErrorCodes.SuperAdminRequired);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new UpdateContactTypeCompanyCommand { Id = mine.Id, Name = "X" })).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task CompanySettings_ShouldDefault_ThenUpsert()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var provider = ctx.Get<ILeadCompanySettingsProvider>();

        var defaults = await provider.GetAsync(ctx.CompanyId);
        defaults.DuplicateOpenLeadPolicy.Should().Be(DuplicateOpenLeadPolicy.Warn);
        defaults.RequireIdentificationForCustomer.Should().BeTrue();
        defaults.CodePrefix.Should().Be("LD-");
        defaults.CodeSequenceLength.Should().Be(6);

        var created = await ctx.SendAsync(new SaveLeadCompanySettingsCommand { DuplicateOpenLeadPolicy = 2, RequireIdentificationForCustomer = false, CodePrefix = " OP- ", CodeSequenceLength = 4 });
        created.IsSuccess.Should().BeTrue(created.Message);
        created.Data!.IsDefault.Should().BeFalse();
        created.Data.CodePrefix.Should().Be("OP-");
        var updated = await ctx.SendAsync(new SaveLeadCompanySettingsCommand { DuplicateOpenLeadPolicy = 3, CodePrefix = "LD-", CodeSequenceLength = 8 });
        updated.Data!.Id.Should().Be(created.Data.Id);
        (await ctx.Db.LeadCompanySettings.IgnoreQueryFilters().CountAsync(s => s.CompanyId == ctx.CompanyId)).Should().Be(1);
        (await provider.GetAsync(ctx.CompanyId)).DuplicateOpenLeadPolicy.Should().Be(DuplicateOpenLeadPolicy.Allow);

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new SaveLeadCompanySettingsCommand())).Message.Should().Be(LeadSettingsErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task ACustomerWithoutIdentification_IsAllowedWhenTheCompanyTurnsTheRuleOff()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var person = new Person { CompanyId = ctx.CompanyId, PersonType = PersonType.Physical, FirstName = "Lead", LastName = "Sin Documento" };
        ctx.Db.Persons.Add(person);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        (await ctx.SendAsync(new CreateCustomerCommand(person.Id, null))).Message.Should().Be(CustomerErrorCodes.PersonIdentificationRequired);

        await ctx.SendAsync(new SaveLeadCompanySettingsCommand { RequireIdentificationForCustomer = false });
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new CreateCustomerCommand(person.Id, null))).IsSuccess.Should().BeTrue();
    }
}
