// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace JOIN.Application.Common;

/// <summary>
/// Deterministic SHA-256 hashing for high-entropy opaque bearer tokens (e.g. the MFA
/// <c>ChallengeToken</c>) that must be looked up by an equality match on their hash
/// (<c>WHERE TokenHash = @hash</c>).
/// </summary>
/// <remarks>
/// Unlike <see cref="RecoveryCodeHasher"/> (PBKDF2 + random salt, built for low-entropy
/// human-typed codes and always looked up by another key first, then verified), a token
/// minted from a 256-bit CSPRNG has enough entropy that a plain, unsalted SHA-256 digest is
/// safe to index and match directly — the salt in PBKDF2 defends against precomputed
/// dictionaries of *likely* secrets, which does not apply to a uniformly random 32-byte value.
/// </remarks>
public static class OpaqueTokenHasher
{
    /// <summary>
    /// Computes the deterministic SHA-256 hash of <paramref name="token"/>, returned as
    /// <c>base64(hash)</c> so the same token always maps to the same stored value.
    /// </summary>
    public static string Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token is required.", nameof(token));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }
}
