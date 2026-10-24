using System.Data;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using JOIN.Application.Interface;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Customers.Leads.Queries;
using JOIN.Domain.Admin;
using MediatR;
using Microsoft.Extensions.Options;
using PersonContactType = JOIN.Domain.Enums.ContactType;

namespace JOIN.Application.UseCases.Common.Emails.Queries;

/// <summary>SQL and checks shared by the email queries (SPEC 72).</summary>
internal static class PersonEmailQuerySql
{
    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(PersonEmailErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static async Task<bool> PersonExistsAsync(IDbConnection connection, Guid personId, Guid companyId, CancellationToken cancellationToken) =>
        await connection.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(
            "SELECT 1 FROM admin.persons p WHERE p.Id = @PersonId AND p.CompanyId = @CompanyId AND p.GcRecord = 0;",
            new { PersonId = personId, CompanyId = companyId }, cancellationToken: cancellationToken)) is not null;

    /// <summary>The person of a live customer of the company, or <c>null</c>.</summary>
    internal static Task<Guid?> CustomerPersonAsync(IDbConnection connection, Guid customerId, Guid companyId, CancellationToken cancellationToken) =>
        connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT c.PersonId FROM customer.customers c WHERE c.Id = @CustomerId AND c.CompanyId = @CompanyId AND c.GcRecord = 0;",
            new { CustomerId = customerId, CompanyId = companyId }, cancellationToken: cancellationToken));

    /// <summary>
    /// Resolves the scope of a history request into the filter on <c>admin.personemailmessages m</c>: the person (also for
    /// a customer) or the lead, visible to the user (SPEC 60). The error is the 404 of the scope.
    /// </summary>
    internal static async Task<(string? Filter, Guid FilterId, Response<T>? Error)> ResolveScopeAsync<T>(
        IDbConnection connection, PersonEmailHistoryScope scope, Guid id, Guid companyId, Guid userId, LeadAccessGuard guard, CancellationToken cancellationToken)
    {
        switch (scope)
        {
            case PersonEmailHistoryScope.Person:
                return await PersonExistsAsync(connection, id, companyId, cancellationToken)
                    ? ("m.PersonId = @FilterId", id, null)
                    : (null, Guid.Empty, Response<T>.Error(PersonEmailErrorCodes.PersonNotFound, ["Person not found."]));
            case PersonEmailHistoryScope.Customer:
                var personId = await CustomerPersonAsync(connection, id, companyId, cancellationToken);
                return personId is { } customerPerson
                    ? ("m.PersonId = @FilterId", customerPerson, null)
                    : (null, Guid.Empty, Response<T>.Error(PersonEmailErrorCodes.CustomerNotFound, ["Customer not found."]));
            default:
                var access = await guard.ResolveAsync(companyId, userId, cancellationToken);
                return userId != Guid.Empty && await LeadQuerySql.IsVisibleAsync(connection, id, companyId, access, cancellationToken)
                    ? ("m.LeadId = @FilterId", id, null)
                    : (null, Guid.Empty, Response<T>.Error(PersonEmailErrorCodes.LeadNotFound, ["Lead not found."]));
        }
    }

    internal static Guid UserId(ICurrentUserService currentUserService) =>
        Guid.TryParse(currentUserService.UserId, out var userId) ? userId : Guid.Empty;
}

/// <summary>Handles <see cref="GetPersonEmailAddressesQuery"/> with Dapper.</summary>
public sealed class GetPersonEmailAddressesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonEmailAddressesQuery, Response<IReadOnlyList<PersonEmailAddressDto>>>
{
    public async Task<Response<IReadOnlyList<PersonEmailAddressDto>>> Handle(GetPersonEmailAddressesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return PersonEmailQuerySql.CompanyRequired<IReadOnlyList<PersonEmailAddressDto>>();
        }

        using var connection = connectionFactory.CreateConnection();
        if (!await PersonEmailQuerySql.PersonExistsAsync(connection, request.PersonId, currentUserService.CompanyId, cancellationToken))
        {
            return Response<IReadOnlyList<PersonEmailAddressDto>>.Error(PersonEmailErrorCodes.PersonNotFound, ["Person not found."]);
        }

        const string sql = """
            SELECT pc.Id, pc.ContactValue AS Value, pc.IsPrimary, pc.ContactType
            FROM admin.personcontacts pc
            WHERE pc.PersonId = @PersonId
              AND pc.CompanyId = @CompanyId
              AND pc.GcRecord = 0
              AND pc.IsActive = TRUE
              AND pc.ContactType IN (@PrimaryEmail, @AlternativeEmail);
            """;

        var rows = await connection.QueryAsync<ContactRow>(new CommandDefinition(
            sql,
            new
            {
                request.PersonId,
                currentUserService.CompanyId,
                PrimaryEmail = (int)PersonContactType.PrimaryEmail,
                AlternativeEmail = (int)PersonContactType.AlternativeEmail
            },
            cancellationToken: cancellationToken));

        var addresses = PersonEmailAddresses.Filter(rows.Select(r => new PersonEmailAddressDto
        {
            Id = r.Id,
            Value = r.Value.Trim(),
            IsPrimary = r.IsPrimary,
            ContactType = ((PersonContactType)r.ContactType).ToString()
        }));

        return new Response<IReadOnlyList<PersonEmailAddressDto>>
        {
            IsSuccess = true,
            Message = "Email addresses retrieved successfully.",
            Data = addresses
        };
    }

    private sealed class ContactRow
    {
        public Guid Id { get; set; }
        public string Value { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public int ContactType { get; set; }
    }
}

/// <summary>Handles <see cref="GetPersonEmailAttachmentCandidatesQuery"/> with Dapper.</summary>
public sealed class GetPersonEmailAttachmentCandidatesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    LeadAccessGuard guard)
    : IRequestHandler<GetPersonEmailAttachmentCandidatesQuery, Response<PersonEmailAttachmentCandidatesDto>>
{
    /// <summary>Maximum number of product documents returned (SPEC 72 C.10).</summary>
    private const int MaxProductDocuments = 50;

    public async Task<Response<PersonEmailAttachmentCandidatesDto>> Handle(GetPersonEmailAttachmentCandidatesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return PersonEmailQuerySql.CompanyRequired<PersonEmailAttachmentCandidatesDto>();
        }

        var companyId = currentUserService.CompanyId;
        using var connection = connectionFactory.CreateConnection();
        if (!await PersonEmailQuerySql.PersonExistsAsync(connection, request.PersonId, companyId, cancellationToken))
        {
            return Response<PersonEmailAttachmentCandidatesDto>.Error(PersonEmailErrorCodes.PersonNotFound, ["Person not found."]);
        }

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);
        parameters.Add("Limit", MaxProductDocuments);
        var search = string.Empty;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            search = "AND (p.Name ILIKE @Search OR p.Code ILIKE @Search OR d.DisplayName ILIKE @Search OR d.OriginalName ILIKE @Search)";
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        var productSql = $"""
            SELECT 'ProductDocument' AS SourceKind, d.Id AS SourceId, d.OriginalName AS FileName, d.ContentType, d.SizeBytes,
                   CONCAT(p.Name, ' — ', d.DisplayName) AS OriginName
            FROM catalog.productdocuments d
            INNER JOIN catalog.products p ON p.Id = d.ProductId AND p.GcRecord = 0
            WHERE d.CompanyId = @CompanyId
              AND d.GcRecord = 0
              {search}
            ORDER BY p.Name, d.DisplayName
            LIMIT @Limit;
            """;
        var items = (await connection.QueryAsync<PersonEmailAttachmentCandidateDto>(new CommandDefinition(
            productSql, parameters, cancellationToken: cancellationToken))).ToList();

        if (request.LeadId is { } leadId && leadId != Guid.Empty)
        {
            var userId = PersonEmailQuerySql.UserId(currentUserService);
            var access = await guard.ResolveAsync(companyId, userId, cancellationToken);
            if (userId == Guid.Empty || !await LeadQuerySql.IsVisibleAsync(connection, leadId, companyId, access, cancellationToken))
            {
                return Response<PersonEmailAttachmentCandidatesDto>.Error(PersonEmailErrorCodes.LeadNotFound, ["Lead not found."]);
            }

            const string leadSql = """
                SELECT 'LeadDocument' AS SourceKind, d.Id AS SourceId, d.OriginalName AS FileName, d.ContentType, d.SizeBytes,
                       l.Code AS OriginName
                FROM customer.leaddocuments d
                INNER JOIN customer.leads l ON l.Id = d.LeadId
                WHERE d.LeadId = @LeadId
                  AND d.CompanyId = @CompanyId
                  AND d.GcRecord = 0
                  AND l.PersonId = @PersonId
                ORDER BY d.Created DESC;
                """;
            items.AddRange(await connection.QueryAsync<PersonEmailAttachmentCandidateDto>(new CommandDefinition(
                leadSql, new { LeadId = leadId, CompanyId = companyId, request.PersonId }, cancellationToken: cancellationToken)));
        }

        return new Response<PersonEmailAttachmentCandidatesDto>
        {
            IsSuccess = true,
            Message = "Attachment candidates retrieved successfully.",
            Data = new PersonEmailAttachmentCandidatesDto
            {
                Items = items,
                MaxAttachmentsTotalBytes = await ReadMaxAttachmentsTotalBytesAsync(connection, companyId, cancellationToken)
            }
        };
    }

    /// <summary>
    /// The limit of the total size of the attachments of the company (SPEC 72.1): the
    /// <c>MaxAttachmentsTotalBytes</c> of its <c>EmailCompanySettings</c> row, or the default of SPEC 72 when the
    /// company has no row. It travels with the candidates so the sending modal does not need the
    /// <c>EmailSettings</c> permission to know it.
    /// </summary>
    private static async Task<long> ReadMaxAttachmentsTotalBytesAsync(IDbConnection connection, Guid companyId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT MaxAttachmentsTotalBytes
            FROM admin.emailcompanysettings
            WHERE CompanyId = @CompanyId AND GcRecord = 0;
            """;

        var value = await connection.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(
            sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
        return value ?? EmailCompanySettings.DefaultMaxAttachmentsTotalBytes;
    }
}

/// <summary>Handles <see cref="GetPersonEmailsQuery"/> with Dapper.</summary>
public sealed class GetPersonEmailsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService,
    LeadAccessGuard guard)
    : IRequestHandler<GetPersonEmailsQuery, Response<PagedResult<PersonEmailMessageListItemDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<PersonEmailMessageListItemDto>>> Handle(GetPersonEmailsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return PersonEmailQuerySql.CompanyRequired<PagedResult<PersonEmailMessageListItemDto>>();
        }

        var companyId = currentUserService.CompanyId;
        using var connection = connectionFactory.CreateConnection();
        var (filter, filterId, error) = await PersonEmailQuerySql.ResolveScopeAsync<PagedResult<PersonEmailMessageListItemDto>>(
            connection, request.Scope, request.Id, companyId, PersonEmailQuerySql.UserId(currentUserService), guard, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var sql = $"""
            SELECT m.Id, m.SentAt, m.Subject, m.ToAddress, m.Status, m.Origin, m.LeadId, l.Code AS LeadCode,
                   m.SentByUserId, CONCAT(u.FirstName, ' ', u.LastName) AS SentByUserName,
                   (SELECT COUNT(*)::int FROM admin.personemailmessageattachments a
                     WHERE a.PersonEmailMessageId = m.Id AND a.GcRecord = 0) AS AttachmentCount
            FROM admin.personemailmessages m
            LEFT JOIN customer.leads l ON l.Id = m.LeadId
            LEFT JOIN security.users u ON u.Id = m.SentByUserId
            WHERE m.CompanyId = @CompanyId
              AND m.GcRecord = 0
              AND {filter}
            ORDER BY m.SentAt DESC
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM admin.personemailmessages m
            WHERE m.CompanyId = @CompanyId
              AND m.GcRecord = 0
              AND {filter};
            """;

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new { CompanyId = companyId, FilterId = filterId, PageSize = pageSize, Offset = (pageNumber - 1) * pageSize },
            cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<PersonEmailMessageListItemDto>())
            .Select(i => i with { SentByUserName = i.SentByUserName.Trim() })
            .ToList();
        var total = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<PersonEmailMessageListItemDto>>
        {
            IsSuccess = true,
            Message = "Emails retrieved successfully.",
            Data = new PagedResult<PersonEmailMessageListItemDto> { Items = items, PageNumber = pageNumber, PageSize = pageSize, TotalCount = total }
        };
    }
}

/// <summary>Handles <see cref="GetPersonEmailQuery"/> with Dapper.</summary>
public sealed class GetPersonEmailQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    LeadAccessGuard guard)
    : IRequestHandler<GetPersonEmailQuery, Response<PersonEmailMessageDto>>
{
    public async Task<Response<PersonEmailMessageDto>> Handle(GetPersonEmailQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return PersonEmailQuerySql.CompanyRequired<PersonEmailMessageDto>();
        }

        var companyId = currentUserService.CompanyId;
        using var connection = connectionFactory.CreateConnection();
        var (filter, filterId, error) = await PersonEmailQuerySql.ResolveScopeAsync<PersonEmailMessageDto>(
            connection, request.Scope, request.Id, companyId, PersonEmailQuerySql.UserId(currentUserService), guard, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var sql = $"""
            SELECT m.Id, m.PersonId, m.CustomerId, m.LeadId, m.LeadInteractionId, m.EmailTemplateId, m.Origin,
                   m.ToAddress, m.CcAddresses, m.FromAddress, m.ReplyToAddress, m.Subject, m.BodyHtml, m.Status,
                   m.ProviderMessageId, m.ErrorMessage, m.SentByUserId, CONCAT(u.FirstName, ' ', u.LastName) AS SentByUserName, m.SentAt
            FROM admin.personemailmessages m
            LEFT JOIN security.users u ON u.Id = m.SentByUserId
            WHERE m.Id = @EmailId
              AND m.CompanyId = @CompanyId
              AND m.GcRecord = 0
              AND {filter};

            SELECT a.Id, a.SourceKind, a.SourceId, a.FileName, a.ContentType, a.SizeBytes
            FROM admin.personemailmessageattachments a
            WHERE a.PersonEmailMessageId = @EmailId
              AND a.CompanyId = @CompanyId
              AND a.GcRecord = 0
            ORDER BY a.FileName;
            """;

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
            sql, new { request.EmailId, CompanyId = companyId, FilterId = filterId }, cancellationToken: cancellationToken));
        var row = await multi.ReadSingleOrDefaultAsync<MessageRow>();
        var attachments = (await multi.ReadAsync<AttachmentRow>()).ToList();
        if (row is null)
        {
            return Response<PersonEmailMessageDto>.Error(PersonEmailErrorCodes.PersonEmailNotFound, ["Email not found."]);
        }

        return new Response<PersonEmailMessageDto>
        {
            IsSuccess = true,
            Message = "Email retrieved successfully.",
            Data = new PersonEmailMessageDto
            {
                Id = row.Id,
                PersonId = row.PersonId,
                CustomerId = row.CustomerId,
                LeadId = row.LeadId,
                LeadInteractionId = row.LeadInteractionId,
                EmailTemplateId = row.EmailTemplateId,
                Origin = row.Origin,
                ToAddress = row.ToAddress,
                CcAddresses = string.IsNullOrWhiteSpace(row.CcAddresses) ? [] : row.CcAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                FromAddress = row.FromAddress,
                ReplyToAddress = row.ReplyToAddress,
                Subject = row.Subject,
                BodyHtml = row.BodyHtml,
                Status = row.Status,
                ProviderMessageId = row.ProviderMessageId,
                ErrorMessage = row.ErrorMessage,
                SentByUserId = row.SentByUserId,
                SentByUserName = row.SentByUserName.Trim(),
                SentAt = row.SentAt,
                Attachments = attachments.Select(a => new PersonEmailAttachmentDto
                {
                    Id = a.Id,
                    SourceKind = ((PersonEmailAttachmentSourceKind)a.SourceKind).ToString(),
                    SourceId = a.SourceId,
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    SizeBytes = a.SizeBytes
                }).ToList()
            }
        };
    }

    private sealed class MessageRow
    {
        public Guid Id { get; set; }
        public Guid PersonId { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? LeadId { get; set; }
        public Guid? LeadInteractionId { get; set; }
        public Guid? EmailTemplateId { get; set; }
        public int Origin { get; set; }
        public string ToAddress { get; set; } = string.Empty;
        public string? CcAddresses { get; set; }
        public string FromAddress { get; set; } = string.Empty;
        public string? ReplyToAddress { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public int Status { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }
        public Guid SentByUserId { get; set; }
        public string SentByUserName { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }

    private sealed class AttachmentRow
    {
        public Guid Id { get; set; }
        public int SourceKind { get; set; }
        public Guid SourceId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
