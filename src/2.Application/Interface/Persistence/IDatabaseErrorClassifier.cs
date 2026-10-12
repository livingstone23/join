// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.Interface.Persistence;

/// <summary>
/// Recognizes provider errors without the Application layer referencing the database driver (SPEC 45-post:
/// a retried agent request that loses the race on the <c>OriginReference</c> unique index is answered as a
/// duplicate, not as a failure).
/// </summary>
public interface IDatabaseErrorClassifier
{
    /// <summary>
    /// <c>true</c> when <paramref name="exception"/> (or one of its inner exceptions) is a unique violation of the
    /// index or constraint <paramref name="constraintName"/> (compared without case).
    /// </summary>
    bool IsUniqueViolation(Exception exception, string constraintName);
}
