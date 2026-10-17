using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Customers.Leads.Queries;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Leads.Documents;

/// <summary><c>GET /leads/{leadId}/documents</c> (SPEC 62): documents of a visible lead, optionally of one contact.</summary>
public sealed record GetLeadDocumentsQuery(Guid LeadId, Guid? LeadInteractionId = null, bool? IncludeDeleted = null) : IRequest<Response<IReadOnlyList<LeadDocumentDto>>>;

/// <summary><c>GET /leads/{leadId}/documents/{id}/download</c> (SPEC 62): the file from the provider that stored it.</summary>
public sealed record DownloadLeadDocumentQuery(Guid LeadId, Guid Id) : IRequest<Response<LeadDocumentDownloadResult>>;

/// <summary>Stream and metadata of a downloaded lead document.</summary>
public sealed record LeadDocumentDownloadResult(Stream Content, string ContentType, string OriginalName);

/// <summary>Handles <see cref="GetLeadDocumentsQuery"/>.</summary>
public sealed class GetLeadDocumentsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService, LeadAccessGuard guard)
    : IRequestHandler<GetLeadDocumentsQuery, Response<IReadOnlyList<LeadDocumentDto>>>
{
    public async Task<Response<IReadOnlyList<LeadDocumentDto>>> Handle(GetLeadDocumentsQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<IReadOnlyList<LeadDocumentDto>>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        if (!await LeadQuerySql.IsVisibleAsync(connection, request.LeadId, actor.CompanyId, access, cancellationToken))
        {
            return LeadCommandContext.NotFound<IReadOnlyList<LeadDocumentDto>>();
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND d.GcRecord = 0";
        var interaction = request.LeadInteractionId is null ? string.Empty : "AND d.LeadInteractionId = @LeadInteractionId";
        var sql = $"""
            SELECT
                d.Id, d.GcRecord, d.LeadId, d.LeadInteractionId, ctc.Name AS ContactTypeName, i.OccurredAt AS InteractionOccurredAt,
                d.DocumentType, d.OriginalName, d.ContentType, d.SizeBytes, d.UploadedByUserId,
                CONCAT(u.FirstName, ' ', u.LastName) AS UploadedByUserName, d.Created AS CreatedAt
            FROM customer.leaddocuments d
            LEFT JOIN customer.leadinteractions i ON i.Id = d.LeadInteractionId
            LEFT JOIN customer.contacttypecompanies ctc ON ctc.Id = i.ContactTypeCompanyId
            LEFT JOIN security.users u ON u.Id = d.UploadedByUserId
            WHERE d.LeadId = @LeadId
              AND d.CompanyId = @CompanyId
              {deleted}
              {interaction}
            ORDER BY d.Created DESC;
            """;

        var rows = (await connection.QueryAsync<Row>(new CommandDefinition(
                sql, new { request.LeadId, actor.CompanyId, request.LeadInteractionId }, cancellationToken: cancellationToken)))
            .Select(r => new LeadDocumentDto
            {
                Id = r.Id, GcRecord = r.GcRecord, LeadId = r.LeadId, LeadInteractionId = r.LeadInteractionId, ContactTypeName = r.ContactTypeName,
                InteractionOccurredAt = r.InteractionOccurredAt, DocumentType = ((DocumentType)r.DocumentType).ToString(), OriginalName = r.OriginalName,
                ContentType = r.ContentType, SizeBytes = r.SizeBytes, UploadedByUserId = r.UploadedByUserId, UploadedByUserName = r.UploadedByUserName, CreatedAt = r.CreatedAt
            })
            .ToList();

        return new Response<IReadOnlyList<LeadDocumentDto>> { IsSuccess = true, Message = "Lead documents retrieved successfully.", Data = rows };
    }

    private sealed class Row
    {
        public Guid Id { get; set; }
        public int GcRecord { get; set; }
        public Guid LeadId { get; set; }
        public Guid? LeadInteractionId { get; set; }
        public string? ContactTypeName { get; set; }
        public DateTime? InteractionOccurredAt { get; set; }
        public int DocumentType { get; set; }
        public string OriginalName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public Guid UploadedByUserId { get; set; }
        public string? UploadedByUserName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

/// <summary>Handles <see cref="DownloadLeadDocumentQuery"/>.</summary>
public sealed class DownloadLeadDocumentQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    LeadAccessGuard guard,
    IFileStorageResolver fileStorageResolver)
    : IRequestHandler<DownloadLeadDocumentQuery, Response<LeadDocumentDownloadResult>>
{
    public async Task<Response<LeadDocumentDownloadResult>> Handle(DownloadLeadDocumentQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<LeadDocumentDownloadResult>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        if (!await LeadQuerySql.IsVisibleAsync(connection, request.LeadId, actor.CompanyId, access, cancellationToken))
        {
            return Response<LeadDocumentDownloadResult>.Error(LeadErrorCodes.DocumentNotFound, ["Lead document not found."]);
        }

        const string sql = """
            SELECT d.Path, d.StorageProvider, d.ContentType, d.OriginalName
            FROM customer.leaddocuments d
            WHERE d.Id = @Id
              AND d.LeadId = @LeadId
              AND d.CompanyId = @CompanyId
              AND d.GcRecord = 0;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<DownloadRow>(new CommandDefinition(
            sql, new { request.Id, request.LeadId, actor.CompanyId }, cancellationToken: cancellationToken));
        if (row is null)
        {
            return Response<LeadDocumentDownloadResult>.Error(LeadErrorCodes.DocumentNotFound, ["Lead document not found."]);
        }

        var provider = fileStorageResolver.Resolve((StorageProviderKind)row.StorageProvider);
        if (provider is null)
        {
            return Response<LeadDocumentDownloadResult>.Error(LeadErrorCodes.StorageProviderNotConfigured,
                [$"The storage provider '{(StorageProviderKind)row.StorageProvider}' of this document is not configured in the current environment."]);
        }

        var stream = await provider.OpenReadAsync(row.Path, cancellationToken);
        return stream is null
            ? Response<LeadDocumentDownloadResult>.Error(LeadErrorCodes.FileNotFoundInStorage, ["The underlying file is missing from storage."])
            : new Response<LeadDocumentDownloadResult> { IsSuccess = true, Message = "Lead document retrieved successfully.", Data = new LeadDocumentDownloadResult(stream, row.ContentType, row.OriginalName) };
    }

    private sealed class DownloadRow
    {
        public string Path { get; set; } = string.Empty;
        public int StorageProvider { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
    }
}
