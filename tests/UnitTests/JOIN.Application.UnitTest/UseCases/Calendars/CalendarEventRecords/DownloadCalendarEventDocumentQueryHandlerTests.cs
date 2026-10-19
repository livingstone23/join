using System.Data;
using System.Data.Common;
using Dapper;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — the download query handler runs Dapper through the connection factory and resolves the
/// storage provider by the row's own <see cref="StorageProviderKind"/>. It also covers the two error
/// paths from the storage adapter (provider not configured, file missing on disk).
/// </summary>
public sealed class DownloadCalendarEventDocumentQueryHandlerTests
{
    /// <summary> Foreign-tenant probe → <c>CALENDAR_DOCUMENT_NOT_FOUND</c> before any storage roundtrip. </summary>
    [Fact]
    public async Task Handle_WhenActivityIsHidden_RespondsDocumentNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext(ownerCanSeeEvent: false);
        var connection = new FakeSqlConnectionFactory();
        var resolver = new Mock<IFileStorageResolver>();
        var handler = new DownloadCalendarEventDocumentQueryHandler(connection, context.Access, resolver.Object);

        var response = await handler.Handle(
            new DownloadCalendarEventDocumentQuery(context.EventId, Guid.NewGuid()),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DocumentNotFound);
    }

    /// <summary> Row missing → <c>CALENDAR_DOCUMENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenRowMissing_RespondsDocumentNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var connection = new FakeSqlConnectionFactory();
        var resolver = new Mock<IFileStorageResolver>();
        var handler = new DownloadCalendarEventDocumentQueryHandler(connection, context.Access, resolver.Object);

        var response = await handler.Handle(
            new DownloadCalendarEventDocumentQuery(context.EventId, Guid.NewGuid()),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DocumentNotFound);
    }

    /// <summary> Provider not configured for the row's storage kind → <c>CALENDAR_STORAGE_PROVIDER_NOT_CONFIGURED</c>. </summary>
    [Fact]
    public async Task Handle_WhenProviderIsNotConfigured_RespondsStorageProviderNotConfigured()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var connection = new FakeSqlConnectionFactory();
        connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Path"] = "calendar/x.pdf",
                ["StorageProvider"] = (int)StorageProviderKind.CloudflareR2,
                ["ContentType"] = "application/pdf",
                ["OriginalName"] = "x.pdf"
            }));
        var resolver = new Mock<IFileStorageResolver>();
        resolver.Setup(x => x.Resolve(StorageProviderKind.CloudflareR2)).Returns((IFileStorageService?)null);

        var handler = new DownloadCalendarEventDocumentQueryHandler(connection, context.Access, resolver.Object);
        var response = await handler.Handle(
            new DownloadCalendarEventDocumentQuery(context.EventId, Guid.NewGuid()),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.StorageProviderNotConfigured);
    }

    /// <summary> Provider resolved but file missing on disk → <c>CALENDAR_FILE_NOT_FOUND_IN_STORAGE</c>. </summary>
    [Fact]
    public async Task Handle_WhenFileMissingOnDisk_RespondsFileNotFoundInStorage()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var connection = new FakeSqlConnectionFactory();
        connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Path"] = "calendar/missing.pdf",
                ["StorageProvider"] = (int)StorageProviderKind.Local,
                ["ContentType"] = "application/pdf",
                ["OriginalName"] = "missing.pdf"
            }));
        var localService = new Mock<IFileStorageService>();
        localService.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);
        localService.Setup(x => x.OpenReadAsync("calendar/missing.pdf", It.IsAny<CancellationToken>())).ReturnsAsync((Stream?)null);
        var resolver = new Mock<IFileStorageResolver>();
        resolver.Setup(x => x.Resolve(StorageProviderKind.Local)).Returns(localService.Object);

        var handler = new DownloadCalendarEventDocumentQueryHandler(connection, context.Access, resolver.Object);
        var response = await handler.Handle(
            new DownloadCalendarEventDocumentQuery(context.EventId, Guid.NewGuid()),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.FileNotFoundInStorage);
    }

    /// <summary> Happy path — provider returns a stream and the handler wraps it with the row's metadata. </summary>
    [Fact]
    public async Task Handle_WhenFileExists_ReturnsStreamAndMetadata()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var connection = new FakeSqlConnectionFactory();
        connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Path"] = "calendar/x.pdf",
                ["StorageProvider"] = (int)StorageProviderKind.Local,
                ["ContentType"] = "application/pdf",
                ["OriginalName"] = "x.pdf"
            }));
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var localService = new Mock<IFileStorageService>();
        localService.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);
        localService.Setup(x => x.OpenReadAsync("calendar/x.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(stream);
        var resolver = new Mock<IFileStorageResolver>();
        resolver.Setup(x => x.Resolve(StorageProviderKind.Local)).Returns(localService.Object);

        var handler = new DownloadCalendarEventDocumentQueryHandler(connection, context.Access, resolver.Object);
        var response = await handler.Handle(
            new DownloadCalendarEventDocumentQuery(context.EventId, Guid.NewGuid()),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.OriginalName.Should().Be("x.pdf");
        response.Data.ContentType.Should().Be("application/pdf");
        response.Data.Content.Should().BeSameAs(stream);
        connection.LastCommandText.Should().Contain("d.DeletedUtc IS NULL", "a soft-deleted document no longer downloads");
    }
}