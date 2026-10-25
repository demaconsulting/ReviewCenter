// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     The outcome of verifying the audit chain.
/// </summary>
/// <param name="IsIntact"><c>true</c> when every entry verified.</param>
/// <param name="Count">The number of entries that verified; for a broken chain, the entries before the break.</param>
/// <param name="FirstBadSequence">The sequence number of the first entry that failed; <c>null</c> when intact.</param>
/// <param name="HeadHash">The hash of the last entry that verified; <see cref="AuditChain.GenesisHash"/> for an empty chain.</param>
public sealed record AuditVerificationResult(bool IsIntact, long Count, long? FirstBadSequence, string HeadHash);

/// <summary>
///     Computes the hash of an audit entry and verifies a whole chain of entries.
/// </summary>
/// <remarks>
///     The hash is SHA-256 over a canonical, unambiguous encoding of every field of the entry including the
///     previous hash, written as 64 lowercase hexadecimal characters. Encoding (version 1): the domain
///     separator <c>ReviewCenter.Audit.v1</c>, the sequence, the time as UTC ticks, the actor identity, the
///     actor text, the action, the project, the review, the detail and the previous hash, in that order.
///     Numbers are 64-bit big-endian; an optional value is a presence byte (0 or 1) followed by the value
///     when present; a string is its UTF-8 bytes preceded by a 32-bit big-endian byte count. The length
///     prefix makes the encoding unambiguous, so moving text between two adjacent fields changes the hash.
///     The class is stateless and thread-safe.
/// </remarks>
public static class AuditChain
{
    /// <summary>
    ///     The previous hash of the first entry: 64 zeros.
    /// </summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    ///     The text that starts every encoded entry and names the encoding version.
    /// </summary>
    private const string DomainSeparator = "ReviewCenter.Audit.v1";

    /// <summary>
    ///     The UTF-8 encoding used for text; invalid text is replaced rather than thrown on, because a hash must
    ///     always be computable.
    /// </summary>
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>
    ///     Computes the hash of an entry from its fields; the stored <see cref="AuditEntry.Hash"/> is not used.
    /// </summary>
    /// <param name="entry">The entry. Must not be null.</param>
    /// <returns>The hash as 64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    public static string ComputeHash(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var buffer = new MemoryStream();
        WriteString(buffer, DomainSeparator);
        WriteInt64(buffer, entry.Sequence);
        WriteInt64(buffer, entry.TimestampUtc.UtcTicks);
        WriteOptionalInt64(buffer, entry.ActorUserId);
        WriteString(buffer, entry.ActorName);
        WriteString(buffer, entry.Action);
        WriteOptionalInt64(buffer, entry.ProjectId);
        if (entry.ReviewId is null)
        {
            buffer.WriteByte(0);
        }
        else
        {
            buffer.WriteByte(1);
            WriteString(buffer, entry.ReviewId);
        }

        WriteString(buffer, entry.Detail);
        WriteString(buffer, entry.PreviousHash);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>
    ///     Verifies a whole chain, in order.
    /// </summary>
    /// <param name="entries">The entries in sequence order, starting with sequence 1. Must not be null.</param>
    /// <returns>The result; verification stops at the first entry that fails.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries"/> is null.</exception>
    public static AuditVerificationResult Verify(IEnumerable<AuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var verifier = new AuditChainVerifier();

        // Any stops at the first entry that fails; later entries are not looked at
        _ = entries.Any(entry => !verifier.Add(entry));
        return verifier.Result;
    }

    /// <summary>
    ///     Writes a signed 64-bit number in big-endian order.
    /// </summary>
    /// <param name="buffer">The stream to write to.</param>
    /// <param name="value">The number.</param>
    private static void WriteInt64(MemoryStream buffer, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        buffer.Write(bytes);
    }

    /// <summary>
    ///     Writes an optional number: a presence byte, then the number when there is one.
    /// </summary>
    /// <param name="buffer">The stream to write to.</param>
    /// <param name="value">The number, or <c>null</c>.</param>
    private static void WriteOptionalInt64(MemoryStream buffer, long? value)
    {
        if (value is null)
        {
            buffer.WriteByte(0);
            return;
        }

        buffer.WriteByte(1);
        WriteInt64(buffer, value.Value);
    }

    /// <summary>
    ///     Writes a string as a 32-bit big-endian byte count followed by its UTF-8 bytes.
    /// </summary>
    /// <param name="buffer">The stream to write to.</param>
    /// <param name="value">The string; <c>null</c> is written as empty.</param>
    private static void WriteString(MemoryStream buffer, string? value)
    {
        var bytes = _utf8.GetBytes(value ?? string.Empty);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        buffer.Write(length);
        buffer.Write(bytes);
    }
}

/// <summary>
///     Verifies audit entries one at a time, so a long chain can be checked while it is read in batches.
/// </summary>
/// <remarks>
///     An entry is good when it has the next sequence number (starting at 1, no gap), names the hash of the
///     previous good entry (the genesis hash for the first), and its stored hash equals the hash recomputed from
///     its fields. The first failure is remembered and later entries are ignored. The first bad sequence is the
///     expected number when a number is missing or out of order, and the entry's own sequence otherwise. The
///     class keeps state and is not thread-safe.
/// </remarks>
public sealed class AuditChainVerifier
{
    /// <summary>
    ///     The sequence number the next entry must have.
    /// </summary>
    private long _expected = 1;

    /// <summary>
    ///     The hash of the last entry that verified.
    /// </summary>
    private string _previousHash = AuditChain.GenesisHash;

    /// <summary>
    ///     Gets the sequence number of the first entry that failed, or <c>null</c> while the chain is intact.
    /// </summary>
    public long? FirstBadSequence { get; private set; }

    /// <summary>
    ///     Gets the number of entries that verified.
    /// </summary>
    public long Count { get; private set; }

    /// <summary>
    ///     Gets the current result.
    /// </summary>
    public AuditVerificationResult Result => new(FirstBadSequence is null, Count, FirstBadSequence, _previousHash);

    /// <summary>
    ///     Verifies the next entry.
    /// </summary>
    /// <param name="entry">The next entry in sequence order. Must not be null.</param>
    /// <returns><c>true</c> when the chain is still intact after the entry; <c>false</c> once an entry has failed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    public bool Add(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (FirstBadSequence is not null)
        {
            return false;
        }

        if (entry.Sequence != _expected)
        {
            // An entry is missing, repeated or out of order: the chain breaks where the expected entry should be
            FirstBadSequence = _expected;
            return false;
        }

        if (entry.PreviousHash != _previousHash
            || !string.Equals(entry.Hash, AuditChain.ComputeHash(entry), StringComparison.Ordinal))
        {
            FirstBadSequence = entry.Sequence;
            return false;
        }

        _previousHash = entry.Hash;
        _expected++;
        Count++;
        return true;
    }
}
