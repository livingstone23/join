using System.Text.Json;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Audit;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Audit;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using MediatR;

namespace JOIN.Application.UseCases.Security.Audit.Queries.GetSecurityAuditLog;

/// <summary>
/// Reads <c>Security.AuditLogs</c> through <see cref="IAuditLogRepository"/> and projects
/// each row into <see cref="SecurityAuditLogItemDto"/>. Bad JSON on a single row does
/// not abort the page: the offending row surfaces with an empty <c>Changes</c> list.
/// </summary>
public sealed class GetSecurityAuditLogQueryHandler(
    IAuditLogRepository repository,
    ICurrentUserService currentUserService,
    IUserAdminRepository userAdminRepository)
    : IRequestHandler<GetSecurityAuditLogQuery, Response<PagedResult<SecurityAuditLogItemDto>>>
{
    private readonly IAuditLogRepository _repository = repository;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IUserAdminRepository _userAdminRepository = userAdminRepository;

    public async Task<Response<PagedResult<SecurityAuditLogItemDto>>> Handle(
        GetSecurityAuditLogQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Tenant required.
        var companyId = _currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PagedResult<SecurityAuditLogItemDto>>.Error(
                "TENANT_REQUIRED",
                ["A tenant context is required to read the security audit log."]);
        }

        // 2. Entity enum parsing — INVALID_ENTITY surfaces as 400, not an empty list.
        AuditedEntity? entityFilter = null;
        if (!string.IsNullOrWhiteSpace(request.Entity))
        {
            if (!Enum.TryParse<AuditedEntity>(request.Entity, ignoreCase: true, out var parsed))
            {
                return Response<PagedResult<SecurityAuditLogItemDto>>.Error(
                    "INVALID_ENTITY",
                    [$"Unknown entity name '{request.Entity}'."]);
            }
            entityFilter = parsed;
        }

        // 3. Action enum parsing.
        AuditAction? actionFilter = null;
        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            if (!Enum.TryParse<AuditAction>(request.Action, ignoreCase: true, out var parsed))
            {
                return Response<PagedResult<SecurityAuditLogItemDto>>.Error(
                    "INVALID_ACTION",
                    [$"Unknown action '{request.Action}'."]);
            }
            actionFilter = parsed;
        }

        // 4. AllTenants only honored for SuperAdmin. Otherwise silently tenant-scoped.
        bool isSuperAdmin = await _userAdminRepository.IsSuperAdminAsync(_currentUserService.UserId, cancellationToken);
        Guid? scopeCompanyId = request.AllTenants && isSuperAdmin ? null : companyId;

        // 5. Clamp paging + normalize ToDate to exclusive.
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var fromUtc = request.FromDate?.ToUniversalTime();
        var toUtcExclusive = request.ToDate?.Date.AddDays(1);

        var (items, names, total) = await _repository.ListPagedAsync(
            scopeCompanyId,
            entityFilter?.ToString(),
            request.EntityId,
            request.ChangedBy,
            actionFilter?.ToString(),
            fromUtc,
            toUtcExclusive,
            pageNumber,
            pageSize,
            cancellationToken);

        var dtos = items.Select(item => new SecurityAuditLogItemDto(
            Id: item.Id,
            EntityName: item.EntityName,
            EntityId: item.EntityId,
            EntityLabel: item.EntityLabel,
            Action: item.Action,
            ChangedBy: item.ChangedBy,
            ChangedByName: names.TryGetValue(item.ChangedBy, out var name) ? name : null,
            ChangedAtUtc: item.ChangedAtUtc,
            IpAddress: item.IpAddress,
            Changes: MergeChanges(item.OldValuesJson, item.NewValuesJson),
            Metadata: item.MetadataJson)).ToList();

        var totalPages = pageSize > 0 ? (int)Math.Ceiling(total / (double)pageSize) : 0;

        var paged = new PagedResult<SecurityAuditLogItemDto>
        {
            Items = dtos,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = totalPages
        };

        return new Response<PagedResult<SecurityAuditLogItemDto>>
        {
            IsSuccess = true,
            Message = "Security audit log retrieved.",
            Data = paged
        };
    }

    /// <summary>
    /// Crosses old and new JSON into a flat list of <see cref="AuditFieldChangeDto"/>.
    /// Tolerant of malformed JSON on a single row: returns an empty list rather
    /// than aborting the whole page.
    /// </summary>
    private static IReadOnlyList<AuditFieldChangeDto> MergeChanges(string? oldJson, string? newJson)
    {
        var oldDict = SafeDeserialize(oldJson);
        var newDict = SafeDeserialize(newJson);

        var keys = new HashSet<string>(oldDict.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in newDict.Keys)
        {
            keys.Add(key);
        }

        var result = new List<AuditFieldChangeDto>(keys.Count);
        foreach (var key in keys)
        {
            oldDict.TryGetValue(key, out var oldVal);
            newDict.TryGetValue(key, out var newVal);
            result.Add(new AuditFieldChangeDto(
                Field: key,
                OldValue: oldVal,
                NewValue: newVal));
        }

        result.Sort((a, b) => string.Compare(a.Field, b.Field, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static Dictionary<string, string?> SafeDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                       ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            return dict.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ValueKind == JsonValueKind.Null ? null : kvp.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Corrupt JSON on a single row must not blow up the whole page.
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
