using Dapper;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

/// <summary>
/// SPEC 69 — handles <see cref="DownloadCalendarEventDocumentQuery"/>: gates the request by the activity
/// visibility (regla 5), pulls the row with Dapper (queries read through <c>ISqlConnectionFactory</c> per
/// the project convention), and opens the file with the provider that originally stored it. The provider is
/// resolved by the <see cref="StorageProviderKind"/> persisted on the row, so a record written to local
/// storage still streams from local even if the environment switches to R2. A soft-deleted document no longer
/// downloads (rule 4): it answers <c>CALENDAR_DOCUMENT_NOT_FOUND</c>.
/// </summary>
public sealed class DownloadCalendarEventDocumentQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarEventRecordsAccess access,
    IFileStorageResolver fileStorageResolver)
    : IRequestHandler<DownloadCalendarEventDocumentQuery, Response<CalendarEventDocumentDownloadResult>>
{
    public async Task<Response<CalendarEventDocumentDownloadResult>> Handle(
        DownloadCalendarEventDocumentQuery request,
        CancellationToken cancellationToken)
    {
        var eventAccess = await access.ResolveAsync(request.EventId, cancellationToken);
        if (!eventAccess.IsFoundAndVisible)
        {
            return Response<CalendarEventDocumentDownloadResult>.Error(
                CalendarErrorCodes.DocumentNotFound,
                ["The calendar document was not found or its activity is not visible."]);
        }

        using var connection = connectionFactory.CreateConnection();
        const string sql = """
            SELECT d.Path, d.StorageProvider, d.ContentType, d.OriginalName
            FROM Calendar.EventDocuments d
            WHERE d.Id = @Id
              AND d.CalendarEventId = @EventId
              AND d.CompanyId = @CompanyId
              AND d.GcRecord = 0
              AND d.DeletedUtc IS NULL;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<DownloadRow>(new CommandDefinition(
            sql,
            new { Id = request.DocumentId, request.EventId, CompanyId = eventAccess.CalendarEvent!.CompanyId },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return Response<CalendarEventDocumentDownloadResult>.Error(
                CalendarErrorCodes.DocumentNotFound,
                ["The calendar document was not found in the current tenant."]);
        }

        var provider = fileStorageResolver.Resolve((Domain.Enums.StorageProviderKind)row.StorageProvider);
        if (provider is null)
        {
            return Response<CalendarEventDocumentDownloadResult>.Error(
                CalendarErrorCodes.StorageProviderNotConfigured,
                [$"The storage provider '{(Domain.Enums.StorageProviderKind)row.StorageProvider}' of this document is not configured in the current environment."]);
        }

        var stream = await provider.OpenReadAsync(row.Path, cancellationToken);
        return stream is null
            ? Response<CalendarEventDocumentDownloadResult>.Error(
                CalendarErrorCodes.FileNotFoundInStorage,
                ["The underlying file is missing from storage."])
            : new Response<CalendarEventDocumentDownloadResult>
            {
                IsSuccess = true,
                Message = "Calendar document retrieved successfully.",
                Data = new CalendarEventDocumentDownloadResult(stream, row.ContentType, row.OriginalName)
            };
    }

    private sealed class DownloadRow
    {
        public string Path { get; set; } = string.Empty;
        public int StorageProvider { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
    }
}