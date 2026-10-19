using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Enums;
using Moq;
using CreateHandler = JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands.CreateCalendarAttachmentSettingsCommandHandler;
using CreateCmd = JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands.CreateCalendarAttachmentSettingsCommand;
using UpdateHandler = JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands.UpdateCalendarAttachmentSettingsCommandHandler;
using UpdateCmd = JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands.UpdateCalendarAttachmentSettingsCommand;
using GetHandler = JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Queries.GetCalendarAttachmentSettingsQueryHandler;
using GetCmd = JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Queries.GetCalendarAttachmentSettingsQuery;
using SettingsEntity = JOIN.Domain.Calendars.CalendarAttachmentSettings;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — coverage of the per-tenant calendar attachment settings CRUD: a single live row per company,
/// created with the seed defaults (rule 7) and replaced by <c>PUT</c>. The handlers fail-closed when the
/// token has no tenant and route every not-configured probe to the same code so the UI can switch between
/// <c>POST</c> and <c>PUT</c> blindly.
/// </summary>
public sealed class Spec69CalendarAttachmentSettingsHandlerTests
{
    private static readonly Fixture _fixture = new();

    /// <summary> Missing <c>CompanyId</c> → <c>COMPANY_REQUIRED</c> on every command. </summary>
    [Fact]
    public async Task Get_WhenCompanyIdIsEmpty_RespondsCompanyRequired()
    {
        var context = new SettingsTestContext(companyId: Guid.Empty);
        var response = await context.CreateGetHandler().Handle(new GetCmd(), CancellationToken.None);
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    /// <summary> A freshly seeded company has the row created by <c>SeedCalendarAttachmentSettingsForCompanyAsync</c> (rule 7). </summary>
    [Fact]
    public async Task Get_WhenRowExists_ReturnsDefaults()
    {
        var row = new SettingsEntity
        {
            CompanyId = Guid.NewGuid(),
            AllowedDocumentTypes = SettingsEntity.DefaultAllowedTypes,
            MaxFileSizeBytes = SettingsEntity.DefaultMaxFileSizeBytes,
            MaxFilesPerComment = SettingsEntity.DefaultMaxFilesPerComment,
            MaxFilesPerDay = SettingsEntity.DefaultMaxFilesPerDay
        };
        var context = new SettingsTestContext(row.CompanyId);
        context.SettingsRepositoryMock.SetupRows(new[] { row });

        var response = await context.CreateGetHandler().Handle(new GetCmd(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.MaxFileSizeBytes.Should().Be(SettingsEntity.DefaultMaxFileSizeBytes);
        response.Data.MaxFilesPerComment.Should().Be(SettingsEntity.DefaultMaxFilesPerComment);
    }

    /// <summary> Without the row, the GET answers the not-configured code so the UI can show the configuration tab. </summary>
    [Fact]
    public async Task Get_WhenRowMissing_RespondsAttachmentSettingsNotConfigured()
    {
        var context = new SettingsTestContext(Guid.NewGuid());
        context.SettingsRepositoryMock.SetupRows(Array.Empty<SettingsEntity>());

        var response = await context.CreateGetHandler().Handle(new GetCmd(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.AttachmentSettingsNotConfigured);
    }

    /// <summary> <c>POST</c> on a row that already exists → <c>CALENDAR_ATTACHMENT_SETTINGS_ALREADY_EXISTS</c>, the UI pivots to PUT. </summary>
    [Fact]
    public async Task Post_WhenRowAlreadyExists_RespondsAlreadyExists()
    {
        var companyId = Guid.NewGuid();
        var existing = new SettingsEntity { CompanyId = companyId };
        var context = new SettingsTestContext(companyId);
        context.SettingsRepositoryMock.SetupRows(new[] { existing });

        var response = await context.CreateCreateHandler().Handle(
            new CreateCmd { AllowedDocumentTypes = DocumentType.Pdf, MaxFileSizeBytes = 1024, MaxFilesPerComment = 1, MaxFilesPerDay = 1 },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CALENDAR_ATTACHMENT_SETTINGS_ALREADY_EXISTS");
    }

    /// <summary> First-time <c>POST</c> inserts the row with the supplied limits. </summary>
    [Fact]
    public async Task Post_WhenRowMissing_InsertsAndReturns()
    {
        var companyId = Guid.NewGuid();
        var context = new SettingsTestContext(companyId);
        context.SettingsRepositoryMock.SetupRows(Array.Empty<SettingsEntity>());

        var response = await context.CreateCreateHandler().Handle(
            new CreateCmd { AllowedDocumentTypes = DocumentType.Pdf | DocumentType.Word, MaxFileSizeBytes = 2_000_000, MaxFilesPerComment = 7, MaxFilesPerDay = 100 },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.MaxFileSizeBytes.Should().Be(2_000_000);
        response.Data.MaxFilesPerComment.Should().Be(7);
        response.Data.MaxFilesPerDay.Should().Be(100);
        context.SettingsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<SettingsEntity>()), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary> <c>PUT</c> on a missing row → not-configured so the UI creates it first. </summary>
    [Fact]
    public async Task Put_WhenRowMissing_RespondsAttachmentSettingsNotConfigured()
    {
        var context = new SettingsTestContext(Guid.NewGuid());
        context.SettingsRepositoryMock.SetupRows(Array.Empty<SettingsEntity>());

        var response = await context.CreateUpdateHandler().Handle(
            new UpdateCmd { AllowedDocumentTypes = DocumentType.Pdf, MaxFileSizeBytes = 1024, MaxFilesPerComment = 1, MaxFilesPerDay = 1 },
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.AttachmentSettingsNotConfigured);
    }

    /// <summary> <c>PUT</c> replaces the row with the supplied limits and persists the change. </summary>
    [Fact]
    public async Task Put_WhenRowExists_ReplacesFieldsAndSaves()
    {
        var companyId = Guid.NewGuid();
        var existing = new SettingsEntity
        {
            CompanyId = companyId,
            AllowedDocumentTypes = DocumentType.Pdf,
            MaxFileSizeBytes = 1024,
            MaxFilesPerComment = 1,
            MaxFilesPerDay = 1
        };
        var context = new SettingsTestContext(companyId);
        context.SettingsRepositoryMock.SetupRows(new[] { existing });

        var response = await context.CreateUpdateHandler().Handle(
            new UpdateCmd { AllowedDocumentTypes = DocumentType.Pdf | DocumentType.Excel, MaxFileSizeBytes = 4_000_000, MaxFilesPerComment = 5, MaxFilesPerDay = 200 },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.MaxFileSizeBytes.Should().Be(4_000_000);
        response.Data.AllowedDocumentTypes.Should().Be((int)(DocumentType.Pdf | DocumentType.Excel));
        context.SettingsRepositoryMock.Verify(x => x.UpdateAsync(It.Is<SettingsEntity>(s => s.MaxFileSizeBytes == 4_000_000)), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class SettingsTestContext
    {
        public SettingsTestContext(Guid companyId)
        {
            CompanyId = companyId;
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            UnitOfWorkMock.Setup(x => x.GetRepository<SettingsEntity>()).Returns(SettingsRepositoryMock.Object);
        }

        public Guid CompanyId { get; }
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<SettingsEntity>> SettingsRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetHandler CreateGetHandler() =>
            new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);

        public CreateHandler CreateCreateHandler() =>
            new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);

        public UpdateHandler CreateUpdateHandler() =>
            new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}