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

using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Tests for <see cref="AuditChain"/> and <see cref="AuditChainVerifier"/>.
/// </summary>
public class AuditChainTests
{
    /// <summary>
    ///     The time of the first entry of the test chains.
    /// </summary>
    private static readonly DateTimeOffset _start = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The hash of the known-answer entry, computed independently of the code under test by encoding the
    ///     fields as documented (length-prefixed, big-endian) and hashing with SHA-256.
    /// </summary>
    private const string KnownAnswerHash = "08a55c11165f7037ecb76588f967ed00737ff85e392335d91299a3ef9eaf8f0f";

    /// <summary>
    ///     The hash of the known-answer entry whose optional fields are all absent.
    /// </summary>
    private const string KnownAnswerMinimalHash = "dadd8f701a5d287b1b0daafebab37d57c9f4941e0b959095754490d3cab7ada4";

    /// <summary>
    ///     Builds an entry with its hash computed from its fields.
    /// </summary>
    /// <param name="sequence">The sequence number.</param>
    /// <param name="previousHash">The previous hash.</param>
    /// <param name="actorName">The actor text.</param>
    /// <param name="action">The action code.</param>
    /// <param name="detail">The detail text.</param>
    /// <returns>The entry.</returns>
    private static AuditEntry Make(
        long sequence,
        string previousHash,
        string actorName = "Ann (ann)",
        string action = "project.created",
        string detail = "project 'Apollo'")
    {
        var entry = new AuditEntry
        {
            Sequence = sequence,
            TimestampUtc = _start.AddMinutes(sequence),
            ActorUserId = 7,
            ActorName = actorName,
            Action = action,
            ProjectId = 3,
            ReviewId = "rev-1",
            Detail = detail,
            PreviousHash = previousHash
        };
        return Copy(entry, hash: AuditChain.ComputeHash(entry));
    }

    /// <summary>
    ///     Copies an entry, replacing the given fields.
    /// </summary>
    /// <param name="entry">The entry to copy.</param>
    /// <param name="sequence">The new sequence, or <c>null</c> to keep it.</param>
    /// <param name="timestamp">The new time, or <c>null</c> to keep it.</param>
    /// <param name="actorName">The new actor text, or <c>null</c> to keep it.</param>
    /// <param name="action">The new action, or <c>null</c> to keep it.</param>
    /// <param name="detail">The new detail, or <c>null</c> to keep it.</param>
    /// <param name="previousHash">The new previous hash, or <c>null</c> to keep it.</param>
    /// <param name="hash">The new hash, or <c>null</c> to keep it.</param>
    /// <returns>The copy.</returns>
    private static AuditEntry Copy(
        AuditEntry entry,
        long? sequence = null,
        DateTimeOffset? timestamp = null,
        string? actorName = null,
        string? action = null,
        string? detail = null,
        string? previousHash = null,
        string? hash = null)
    {
        return new AuditEntry
        {
            Sequence = sequence ?? entry.Sequence,
            TimestampUtc = timestamp ?? entry.TimestampUtc,
            ActorUserId = entry.ActorUserId,
            ActorName = actorName ?? entry.ActorName,
            Action = action ?? entry.Action,
            ProjectId = entry.ProjectId,
            ReviewId = entry.ReviewId,
            Detail = detail ?? entry.Detail,
            PreviousHash = previousHash ?? entry.PreviousHash,
            Hash = hash ?? entry.Hash
        };
    }

    /// <summary>
    ///     Builds a valid chain.
    /// </summary>
    /// <param name="count">The number of entries.</param>
    /// <returns>The entries in order.</returns>
    private static List<AuditEntry> Chain(int count)
    {
        var entries = new List<AuditEntry>();
        var previous = AuditChain.GenesisHash;
        for (var sequence = 1; sequence <= count; sequence++)
        {
            var entry = Make(sequence, previous, detail: "detail " + sequence);
            entries.Add(entry);
            previous = entry.Hash;
        }

        return entries;
    }

    /// <summary>
    ///     Test that the genesis hash is 64 zeros.
    /// </summary>
    [Fact]
    public void AuditChain_GenesisHash_IsSixtyFourZeros()
    {
        // Arrange / Act / Assert
        Assert.Equal(AuditChain.GenesisHash, new string('0', 64));
    }

    /// <summary>
    ///     Test that the hash of a known entry equals the independently computed value, so the encoding cannot
    ///     change without notice (a change would invalidate every stored chain).
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_KnownEntry_MatchesPinnedValue()
    {
        // Arrange
        var entry = new AuditEntry
        {
            Sequence = 1,
            TimestampUtc = _start,
            ActorUserId = 7,
            ActorName = "Ann (ann)",
            Action = "project.created",
            ProjectId = 3,
            ReviewId = "rev-1",
            Detail = "project 'Apollo'",
            PreviousHash = AuditChain.GenesisHash
        };

        // Act
        var hash = AuditChain.ComputeHash(entry);

        // Assert
        Assert.Equal(KnownAnswerHash, hash);
    }

    /// <summary>
    ///     Test that the hash of a known entry without the optional fields equals the independently computed value.
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_KnownEntryWithoutOptionalFields_MatchesPinnedValue()
    {
        // Arrange
        var entry = new AuditEntry
        {
            Sequence = 1,
            TimestampUtc = _start,
            ActorUserId = null,
            ActorName = "Ann",
            Action = "a",
            ProjectId = null,
            ReviewId = null,
            Detail = string.Empty,
            PreviousHash = AuditChain.GenesisHash
        };

        // Act
        var hash = AuditChain.ComputeHash(entry);

        // Assert
        Assert.Equal(KnownAnswerMinimalHash, hash);
    }

    /// <summary>
    ///     Test that the hash is stable, lowercase hexadecimal and ignores the stored hash.
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_SameFields_IsStableLowercaseHexAndIgnoresStoredHash()
    {
        // Arrange
        var entry = Make(1, AuditChain.GenesisHash);
        var other = Copy(entry, hash: "not the hash");

        // Act
        var first = AuditChain.ComputeHash(entry);
        var second = AuditChain.ComputeHash(other);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    /// <summary>
    ///     Test that an offset time is hashed by its UTC instant.
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_SameInstantOtherOffset_SameHash()
    {
        // Arrange
        var entry = Make(1, AuditChain.GenesisHash);
        var shifted = Copy(entry, timestamp: entry.TimestampUtc.ToOffset(TimeSpan.FromHours(5)));

        // Act / Assert
        Assert.Equal(AuditChain.ComputeHash(entry), AuditChain.ComputeHash(shifted));
    }

    /// <summary>
    ///     Test that a null entry is refused.
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_NullEntry_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentNullException>(() => AuditChain.ComputeHash(null!));
        Assert.Throws<ArgumentNullException>(() => AuditChain.Verify(null!));
        Assert.Throws<ArgumentNullException>(() => new AuditChainVerifier().Add(null!));
    }

    /// <summary>
    ///     Test that changing any one field changes the hash.
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_AnyFieldChanged_ChangesHash()
    {
        // Arrange
        var entry = Make(1, AuditChain.GenesisHash);
        var original = AuditChain.ComputeHash(entry);
        var variants = new List<AuditEntry>
        {
            Copy(entry, sequence: 2),
            Copy(entry, timestamp: entry.TimestampUtc.AddTicks(1)),
            Copy(entry, actorName: "Bob (bob)"),
            Copy(entry, action: "project.deleted"),
            Copy(entry, detail: "project 'Gemini'"),
            Copy(entry, previousHash: new string('1', 64)),
            new() { Sequence = 1, TimestampUtc = entry.TimestampUtc, ActorUserId = 8, ActorName = entry.ActorName, Action = entry.Action, ProjectId = 3, ReviewId = "rev-1", Detail = entry.Detail, PreviousHash = entry.PreviousHash },
            new() { Sequence = 1, TimestampUtc = entry.TimestampUtc, ActorUserId = null, ActorName = entry.ActorName, Action = entry.Action, ProjectId = 3, ReviewId = "rev-1", Detail = entry.Detail, PreviousHash = entry.PreviousHash },
            new() { Sequence = 1, TimestampUtc = entry.TimestampUtc, ActorUserId = 7, ActorName = entry.ActorName, Action = entry.Action, ProjectId = 4, ReviewId = "rev-1", Detail = entry.Detail, PreviousHash = entry.PreviousHash },
            new() { Sequence = 1, TimestampUtc = entry.TimestampUtc, ActorUserId = 7, ActorName = entry.ActorName, Action = entry.Action, ProjectId = null, ReviewId = "rev-1", Detail = entry.Detail, PreviousHash = entry.PreviousHash },
            new() { Sequence = 1, TimestampUtc = entry.TimestampUtc, ActorUserId = 7, ActorName = entry.ActorName, Action = entry.Action, ProjectId = 3, ReviewId = "rev-2", Detail = entry.Detail, PreviousHash = entry.PreviousHash },
            new() { Sequence = 1, TimestampUtc = entry.TimestampUtc, ActorUserId = 7, ActorName = entry.ActorName, Action = entry.Action, ProjectId = 3, ReviewId = null, Detail = entry.Detail, PreviousHash = entry.PreviousHash }
        };

        // Act
        var hashes = variants.Select(AuditChain.ComputeHash).ToList();

        // Assert: every variant differs from the original and from every other variant
        Assert.DoesNotContain(original, hashes);
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }

    /// <summary>
    ///     Test that an absent review and an empty review identifier hash differently.
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_NullReviewVersusEmptyReview_DiffersFromEachOther()
    {
        // Arrange
        var entry = Make(1, AuditChain.GenesisHash);
        var none = new AuditEntry { Sequence = 1, TimestampUtc = entry.TimestampUtc, ActorName = "a", Action = "b", ReviewId = null, Detail = "c", PreviousHash = entry.PreviousHash };
        var empty = new AuditEntry { Sequence = 1, TimestampUtc = entry.TimestampUtc, ActorName = "a", Action = "b", ReviewId = string.Empty, Detail = "c", PreviousHash = entry.PreviousHash };

        // Act / Assert
        Assert.NotEqual(AuditChain.ComputeHash(none), AuditChain.ComputeHash(empty));
    }

    /// <summary>
    ///     Test that moving text between two adjacent fields changes the hash: the length prefix removes the
    ///     ambiguity of plain concatenation.
    /// </summary>
    [Theory]
    [InlineData("ab", "c", "a", "bc")]
    [InlineData("", "abc", "abc", "")]
    [InlineData("a|b", "c", "a", "b|c")]
    public void AuditChain_ComputeHash_TextMovedBetweenAdjacentFields_ChangesHash(
        string actor1,
        string action1,
        string actor2,
        string action2)
    {
        // Arrange
        var first = new AuditEntry { Sequence = 1, TimestampUtc = _start, ActorName = actor1, Action = action1, Detail = "d", PreviousHash = AuditChain.GenesisHash };
        var second = new AuditEntry { Sequence = 1, TimestampUtc = _start, ActorName = actor2, Action = action2, Detail = "d", PreviousHash = AuditChain.GenesisHash };

        // Act / Assert
        Assert.NotEqual(AuditChain.ComputeHash(first), AuditChain.ComputeHash(second));
    }

    /// <summary>
    ///     Test that text moved from the detail to the previous hash position changes the hash.
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_TextMovedFromDetailToReview_ChangesHash()
    {
        // Arrange
        var first = new AuditEntry { Sequence = 1, TimestampUtc = _start, ActorName = "a", Action = "b", ReviewId = "x", Detail = "y", PreviousHash = AuditChain.GenesisHash };
        var second = new AuditEntry { Sequence = 1, TimestampUtc = _start, ActorName = "a", Action = "b", ReviewId = "xy", Detail = string.Empty, PreviousHash = AuditChain.GenesisHash };

        // Act / Assert
        Assert.NotEqual(AuditChain.ComputeHash(first), AuditChain.ComputeHash(second));
    }

    /// <summary>
    ///     Test that non-ASCII text hashes without error and differently from similar text.
    /// </summary>
    [Fact]
    public void AuditChain_ComputeHash_UnicodeText_IsHashed()
    {
        // Arrange
        var first = Make(1, AuditChain.GenesisHash, detail: "caf\u00e9 \U0001F600");
        var second = Make(1, AuditChain.GenesisHash, detail: "cafe \U0001F600");

        // Act / Assert
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.Equal(64, first.Hash.Length);
    }

    /// <summary>
    ///     Test that an empty chain is intact with the genesis head.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_EmptyChain_IsIntactWithGenesisHead()
    {
        // Arrange / Act
        var result = AuditChain.Verify([]);

        // Assert
        Assert.True(result.IsIntact);
        Assert.Equal(0, result.Count);
        Assert.Null(result.FirstBadSequence);
        Assert.Equal(AuditChain.GenesisHash, result.HeadHash);
    }

    /// <summary>
    ///     Test that a valid chain is intact and reports its head hash.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_ValidChain_IsIntactWithLastHashAsHead()
    {
        // Arrange
        var entries = Chain(5);

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.True(result.IsIntact);
        Assert.Equal(5, result.Count);
        Assert.Null(result.FirstBadSequence);
        Assert.Equal(entries[^1].Hash, result.HeadHash);
    }

    /// <summary>
    ///     Test that editing any field of any entry, without fixing the hash, breaks the chain at that entry.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void AuditChain_Verify_EditedEntry_ReportsThatEntry(int target)
    {
        // Arrange
        var edits = new List<Func<AuditEntry, AuditEntry>>
        {
            e => Copy(e, timestamp: e.TimestampUtc.AddSeconds(1)),
            e => Copy(e, actorName: "Mallory"),
            e => Copy(e, action: "project.renamed"),
            e => Copy(e, detail: "changed"),
            e => Copy(e, hash: new string('a', 64))
        };

        foreach (var edit in edits)
        {
            var entries = Chain(5);
            entries[target - 1] = edit(entries[target - 1]);

            // Act
            var result = AuditChain.Verify(entries);

            // Assert
            Assert.False(result.IsIntact);
            Assert.Equal(target, result.FirstBadSequence);
            Assert.Equal(target - 1, result.Count);
        }
    }

    /// <summary>
    ///     Test that editing an entry and recomputing its own hash still breaks the chain at the next entry,
    ///     because the next entry names the old hash.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_EditedEntryWithRecomputedHash_BreaksAtNextEntry()
    {
        // Arrange
        var entries = Chain(4);
        var edited = Copy(entries[1], detail: "rewritten");
        entries[1] = Copy(edited, hash: AuditChain.ComputeHash(edited));

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(3, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that deleting an entry from the middle breaks the chain at the deleted sequence.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_DeletedEntry_ReportsTheMissingSequence()
    {
        // Arrange
        var entries = Chain(5);
        entries.RemoveAt(2);

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(3, result.FirstBadSequence);
        Assert.Equal(2, result.Count);
    }

    /// <summary>
    ///     Test that deleting the first entry breaks the chain at sequence 1.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_DeletedFirstEntry_ReportsSequenceOne()
    {
        // Arrange
        var entries = Chain(3);
        entries.RemoveAt(0);

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(1, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that reordering two entries breaks the chain at the first position that is out of order.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_ReorderedEntries_ReportsFirstOutOfOrderSequence()
    {
        // Arrange
        var entries = Chain(5);
        (entries[2], entries[3]) = (entries[3], entries[2]);

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(3, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that inserting a forged entry breaks the chain at the inserted position.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_InsertedEntry_IsDetected()
    {
        // Arrange
        var entries = Chain(4);
        var forged = Make(3, entries[1].Hash, detail: "forged");
        entries.Insert(2, forged);

        // Act
        var result = AuditChain.Verify(entries);

        // Assert: the forged entry itself verifies, so the break is at the original third entry, which is now out of sequence
        Assert.False(result.IsIntact);
        Assert.Equal(4, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that a repeated entry breaks the chain at the repeated sequence.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_DuplicatedEntry_IsDetected()
    {
        // Arrange
        var entries = Chain(3);
        entries.Insert(2, entries[1]);

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(3, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that an entry with the right hash but a wrong previous hash is detected.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_WrongPreviousHash_ReportsThatEntry()
    {
        // Arrange
        var entries = Chain(3);
        entries[1] = Make(2, new string('f', 64), detail: "detail 2");

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(2, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that a chain that does not start at sequence 1 is broken at 1.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_FirstEntryWithOtherSequence_ReportsSequenceOne()
    {
        // Arrange
        var entries = new List<AuditEntry> { Make(2, AuditChain.GenesisHash) };

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(1, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that when two entries are bad, the first one is reported.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_TwoBadEntries_ReportsTheFirst()
    {
        // Arrange
        var entries = Chain(6);
        entries[4] = Copy(entries[4], detail: "late edit");
        entries[1] = Copy(entries[1], detail: "early edit");

        // Act
        var result = AuditChain.Verify(entries);

        // Assert
        Assert.Equal(2, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that verification stops at the first bad entry and does not read further.
    /// </summary>
    [Fact]
    public void AuditChain_Verify_BadEntry_StopsReadingTheSequence()
    {
        // Arrange
        var entries = Chain(5);
        entries[1] = Copy(entries[1], detail: "edit");
        var read = 0;
        IEnumerable<AuditEntry> Stream()
        {
            foreach (var entry in entries)
            {
                read++;
                yield return entry;
            }
        }

        // Act
        var result = AuditChain.Verify(Stream());

        // Assert
        Assert.Equal(2, result.FirstBadSequence);
        Assert.Equal(2, read);
    }

    /// <summary>
    ///     Test that the verifier remembers the first failure, ignores later entries and keeps the head of the
    ///     last good entry.
    /// </summary>
    [Fact]
    public void AuditChainVerifier_Add_AfterFailure_StaysFailedWithLastGoodHead()
    {
        // Arrange
        var entries = Chain(3);
        var verifier = new AuditChainVerifier();

        // Act
        var first = verifier.Add(entries[0]);
        var bad = verifier.Add(Copy(entries[1], detail: "edit"));
        var later = verifier.Add(entries[2]);

        // Assert
        Assert.True(first);
        Assert.False(bad);
        Assert.False(later);
        Assert.Equal(2, verifier.FirstBadSequence);
        Assert.Equal(1, verifier.Count);
        Assert.Equal(entries[0].Hash, verifier.Result.HeadHash);
        Assert.False(verifier.Result.IsIntact);
    }
}
