// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace JOIN.Application.Common;

/// <summary>
/// Static helpers for the human-readable MFA recovery codes used when a user loses their
/// authenticator device. Codes are 10-character Crockford-base32 strings hashed with
/// PBKDF2-SHA256 (100 000 iterations + 16-byte salt) and stored as
/// <c>base64(salt):base64(hash)</c> for portability across DB providers.
/// </summary>
/// <remarks>
/// The 100k iteration constant keeps the cold path (setup / disable) within ~50 ms on
/// commodity hardware while staying portable and avoiding transparent data encryption
/// coupling. Bumping it invalidates every existing recovery-code row, which is acceptable
/// because disable already wipes the set.
/// </remarks>
public static class RecoveryCodeHasher
{
    /// <summary>
    /// Crockford-base32 alphabet. Drops the I/L/O/U letters that humans confuse.
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>
    /// Generates a fresh 10-character recovery code in Crockford-base32 (upper-case).
    /// </summary>
    public static string GenerateCode()
    {
        Span<byte> buffer = stackalloc byte[8];
        RandomNumberGenerator.Fill(buffer);
        var sb = new StringBuilder(10);
        ulong value = BitConverter.ToUInt64(buffer);
        for (int i = 0; i < 10; i++)
        {
            sb.Append(Alphabet[(int)(value & 0x1F)]);
            value >>= 5;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Produces a salted PBKDF2-SHA256 hash of the supplied recovery code, returned as
    /// <c>base64(salt):base64(hash)</c>.
    /// </summary>
    public static string Hash(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code is required.", nameof(code));
        }

        Span<byte> salt = stackalloc byte[SaltBytes];
        RandomNumberGenerator.Fill(salt);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(code.Trim()),
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashBytes);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Constant-time comparison of a candidate recovery code against the stored hash.
    /// </summary>
    /// <returns><c>true</c> when the candidate matches the stored salt+PBKDF2 hash.</returns>
    public static bool Verify(string code, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var separatorIndex = storedHash.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == storedHash.Length - 1)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(storedHash[..separatorIndex]);
            expectedHash = Convert.FromBase64String(storedHash[(separatorIndex + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(code.Trim()),
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
