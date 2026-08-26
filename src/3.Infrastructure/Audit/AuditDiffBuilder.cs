// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Text.Json;

namespace JOIN.Infrastructure.Audit;

/// <summary>
/// Builds the <c>OldValuesJson</c> / <c>NewValuesJson</c> pair for a bitácora row.
/// Carries only the keys whose value actually changed; sensitive keys are discarded
/// outright so they never reach the table.
/// </summary>
public static class AuditDiffBuilder
{
    /// <summary>
    /// Field names that must NEVER be persisted in the bitácora. Discarded entirely
    /// (no <c>"***"</c> masking) so a row's absence makes clear the value was never recorded.
    /// </summary>
    private static readonly HashSet<string> Redacted = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash",
        "SecurityStamp",
        "ConcurrencyStamp",
        "MfaSecretKey",
        "Token",
        "RefreshToken",
        "CodeHash"
    };

    private static readonly JsonSerializerOptions JsonOptions = new();

    /// <summary>
    /// Compares <paramref name="oldValues"/> against <paramref name="newValues"/> and
    /// returns two JSON blobs containing only the changed keys. <c>null</c> and
    /// <see cref="string.Empty"/> are treated as distinct. Returns <c>(null, null)</c>
    /// when nothing changed.
    /// </summary>
    /// <param name="oldValues">Pre-mutation values; may be <c>null</c> on Created.</param>
    /// <param name="newValues">Post-mutation values; may be <c>null</c> on Deleted.</param>
    public static (string? OldJson, string? NewJson) Build(
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues)
    {
        // Treat a Created row (oldValues null, newValues present) as "all new".
        if (oldValues is null && newValues is null)
        {
            return (null, null);
        }

        var oldOut = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var newOut = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Collect the union of non-redacted keys.
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (oldValues is not null)
        {
            foreach (var key in oldValues.Keys)
            {
                if (!Redacted.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }
        if (newValues is not null)
        {
            foreach (var key in newValues.Keys)
            {
                if (!Redacted.Contains(key))
                {
                    keys.Add(key);
                }
            }
        }

        foreach (var key in keys)
        {
            object? oldVal = null;
            object? newVal = null;
            var hasOld = oldValues is not null && oldValues.TryGetValue(key, out oldVal);
            var hasNew = newValues is not null && newValues.TryGetValue(key, out newVal);

            if (AreEqual(hasOld ? oldVal : null, hasNew ? newVal : null))
            {
                continue;
            }

            oldOut[key] = hasOld ? oldVal : null;
            newOut[key] = hasNew ? newVal : null;
        }

        if (oldOut.Count == 0 && newOut.Count == 0)
        {
            return (null, null);
        }

        return (
            JsonSerializer.Serialize(oldOut, JsonOptions),
            JsonSerializer.Serialize(newOut, JsonOptions));
    }

    /// <summary>
    /// Value comparison that distinguishes <c>null</c> from <see cref="string.Empty"/>
    /// — required so that an operator who clears a field is recorded as having changed it.
    /// </summary>
    private static bool AreEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        // String.Empty vs null must NOT be treated as equal.
        if (left is string ls && right is string rs)
        {
            return string.Equals(ls, rs, StringComparison.Ordinal);
        }

        return left.Equals(right);
    }
}
