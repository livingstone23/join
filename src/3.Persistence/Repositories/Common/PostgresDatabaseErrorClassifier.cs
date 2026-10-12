// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface.Persistence;
using Npgsql;

namespace JOIN.Persistence.Repositories.Common;

/// <summary>
/// PostgreSQL implementation of <see cref="IDatabaseErrorClassifier"/>: a unique violation is a
/// <see cref="PostgresException"/> with <c>SqlState</c> <c>23505</c>; the name of the violated index travels in
/// <c>ConstraintName</c> (lower case, as the naming convention creates it).
/// </summary>
public sealed class PostgresDatabaseErrorClassifier : IDatabaseErrorClassifier
{
    public bool IsUniqueViolation(Exception exception, string constraintName)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
                && string.Equals(postgres.ConstraintName, constraintName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
