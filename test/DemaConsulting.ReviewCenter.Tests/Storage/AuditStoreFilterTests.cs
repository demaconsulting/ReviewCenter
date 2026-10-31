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

using System.Diagnostics;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.Data.Sqlite;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the filter of <see cref="AuditStore.ListAsync"/> against a real SQLite database file: each
///     field, the scoping rule, the literal matching of the actor text, the day boundaries of the date range, the
///     keyset paging with a filter, and the read time on a larger trail.
/// </summary>
public class AuditStoreFilterTests
{
    /// <summary>
    ///     Builds a chain of rows with a chosen time and writes them directly, without the appender.
    /// </summary>
    private sealed class Seeder(TestStore db)
    {
        /// <summary>
        ///     The rows added so far, in sequence order.
        /// </summary>
        private readonly List<AuditEntry> _rows = [];

        /// <summary>
        ///     Gets the rows added so far.
        /// </summary>
        public IReadOnlyList<AuditEntry> Rows => _rows;

        /// <summary>
        ///     Adds a row with a correct chain hash, without going through the appender (so the test chooses the time).
        /// </summary>
        public void Add(
            DateTimeOffset time,
            string actor = "Ann",
            string action = "test.event",
            int? projectId = null,
            string? reviewId = null,
            int? actorUserId = null)
        {
            _rows.Add(Build(time, actor, action, projectId, reviewId, actorUserId));
        }

        /// <summary>
        ///     Writes all rows added in one transaction.
        /// </summary>
        public void Save()
        {
            using var context = db.Factory.CreateDbContext();
            context.AuditEntries.AddRange(_rows);
            context.SaveChanges();
        }

        /// <summary>
        ///     Builds the next row of the chain.
        /// </summary>
        private AuditEntry Build(
            DateTimeOffset time,
            string actor,
            string action,
            int? projectId,
            string? reviewId,
            int? actorUserId)
        {
            var pending = new AuditEntry
            {
                Sequence = _rows.Count + 1,
                TimestampUtc = time,
                ActorUserId = actorUserId,
                ActorName = actor,
                Action = action,
                ProjectId = projectId,
                ReviewId = reviewId,
                Detail = "detail " + (_rows.Count + 1),
                PreviousHash = _rows.Count == 0 ? AuditChain.GenesisHash : _rows[^1].Hash
            };
            return new AuditEntry
            {
                Sequence = pending.Sequence,
                TimestampUtc = pending.TimestampUtc,
                ActorUserId = pending.ActorUserId,
                ActorName = pending.ActorName,
                Action = pending.Action,
                ProjectId = pending.ProjectId,
                ReviewId = pending.ReviewId,
                Detail = pending.Detail,
                PreviousHash = pending.PreviousHash,
                Hash = AuditChain.ComputeHash(pending)
            };
        }
    }

    /// <summary>
    ///     A fixed base time for rows whose time does not matter.
    /// </summary>
    private static readonly DateTimeOffset _base = new(2025, 3, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Reads the sequences of a filtered page, newest first.
    /// </summary>
    private static async Task<List<long>> Sequences(
        TestStore db,
        AuditFilter filter,
        long? before = null,
        long? after = null,
        int take = 100)
    {
        var rows = await db.Audit.ListAsync(filter, before, after, take, TestContext.Current.CancellationToken);
        return [.. rows.Select(e => e.Sequence)];
    }

    /// <summary>
    ///     Test that every field of the filter selects exactly the matching entries, newest first.
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_EachField_SelectsMatchingEntries()
    {
        // Arrange
        using var db = new TestStore();
        var seed = new Seeder(db);
        seed.Add(_base, "Ann", "review.created", 1, "r1", 10);
        seed.Add(_base, "Bob Builder", "review.signed", 1, "r2", 11);
        seed.Add(_base, "Ann", "project.created", 2, null, 10);
        seed.Add(_base, "System", "auth.sign-in", null, null, null);
        seed.Add(_base, "Ann", "review.created", 2, "r1", 10);
        seed.Save();

        // Act / Assert
        Assert.Equal([5L, 3L, 1L], await Sequences(db, new AuditFilter(ActorUserId: 10)));
        Assert.Equal([2L], await Sequences(db, new AuditFilter(ActorText: "builder")));
        Assert.Equal([5L, 1L], await Sequences(db, new AuditFilter(Action: "review.created")));
        Assert.Equal([5L, 2L, 1L], await Sequences(db, new AuditFilter(ActionPrefix: "review.")));
        Assert.Equal([5L, 1L], await Sequences(db, new AuditFilter(ReviewId: "r1")));
        Assert.Equal([5L, 3L], await Sequences(db, new AuditFilter(ProjectId: 2)));
        Assert.Equal([5L, 4L, 3L, 2L, 1L], await Sequences(db, AuditFilter.None));
    }

    /// <summary>
    ///     Test that several fields together must all match.
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_CombinedFields_AllMustMatch()
    {
        // Arrange
        using var db = new TestStore();
        var seed = new Seeder(db);
        seed.Add(_base, "Ann", "review.created", 1, "r1", 10);
        seed.Add(_base, "Ann", "review.created", 2, "r1", 10);
        seed.Add(_base, "Bob", "review.created", 1, "r1", 11);
        seed.Add(_base, "Ann", "review.signed", 1, "r1", 10);
        seed.Add(_base, "Ann", "review.created", 1, "r2", 10);
        seed.Save();

        // Act
        var one = await Sequences(
            db, new AuditFilter(ProjectId: 1, ReviewId: "r1", ActorText: "an", Action: "review.created"));
        var prefix = await Sequences(db, new AuditFilter(ProjectId: 1, ActorUserId: 10, ActionPrefix: "review."));

        // Assert
        Assert.Equal([1L], one);
        Assert.Equal([5L, 4L, 1L], prefix);
    }

    /// <summary>
    ///     Test the scoping rule: a project filter never returns a global entry, and a global entry is reachable
    ///     only when no project is set.
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_ProjectFilter_NeverReturnsGlobalEntries()
    {
        // Arrange
        using var db = new TestStore();
        var seed = new Seeder(db);
        seed.Add(_base, "Ann", "project.created", 1);
        seed.Add(_base, "Ann", "auth.sign-in");
        seed.Add(_base, "Ann", "token.created");
        seed.Add(_base, "Ann", "review.created", 1, "r1");
        seed.Save();

        // Act
        var project = await Sequences(db, new AuditFilter(ProjectId: 1));
        var projectAndActor = await Sequences(db, new AuditFilter(ProjectId: 1, ActorText: "ann"));
        var projectAndAction = await Sequences(db, new AuditFilter(ProjectId: 1, ActionPrefix: "auth."));
        var whole = await Sequences(db, AuditFilter.None);

        // Assert
        Assert.Equal([4L, 1L], project);
        Assert.Equal([4L, 1L], projectAndActor);
        Assert.Empty(projectAndAction);
        Assert.Equal([4L, 3L, 2L, 1L], whole);
    }

    /// <summary>
    ///     Test that the actor text matches as a literal, case-insensitive substring: <c>%</c>, <c>_</c> and the
    ///     backslash have no special meaning.
    /// </summary>
    [Theory]
    [InlineData("%", new[] { 1L })]
    [InlineData("_", new[] { 2L })]
    [InlineData("\\", new[] { 3L })]
    [InlineData("100%", new[] { 1L })]
    [InlineData("a_c", new[] { 2L })]
    [InlineData("A\\B", new[] { 3L })]
    [InlineData("%%", new long[0])]
    [InlineData("\\%", new long[0])]
    [InlineData("PLAIN", new[] { 4L })]
    [InlineData("lain n", new[] { 4L })]
    public async Task AuditStore_ListAsync_ActorText_MatchesLiterallyAndIgnoresCase(string text, long[] expected)
    {
        // Arrange: names that would all match if the characters were wildcards
        using var db = new TestStore();
        var seed = new Seeder(db);
        seed.Add(_base, "100% done");
        seed.Add(_base, "a_c");
        seed.Add(_base, "a\\b");
        seed.Add(_base, "Plain Name");
        seed.Add(_base, "abc");
        seed.Save();

        // Act
        var found = await Sequences(db, new AuditFilter(ActorText: text));

        // Assert
        Assert.Equal(expected.OrderDescending(), found);
    }

    /// <summary>
    ///     Test that the action prefix matches as a literal start: <c>_</c> and <c>%</c> are not wildcards.
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_ActionPrefix_MatchesLiterally()
    {
        // Arrange
        using var db = new TestStore();
        var seed = new Seeder(db);
        seed.Add(_base, action: "a_b.one");
        seed.Add(_base, action: "axb.two");
        seed.Add(_base, action: "a%b.three");
        seed.Add(_base, action: "review.created");
        seed.Save();

        // Act
        var underscore = await Sequences(db, new AuditFilter(ActionPrefix: "a_"));
        var percent = await Sequences(db, new AuditFilter(ActionPrefix: "a%"));
        var group = await Sequences(db, new AuditFilter(ActionPrefix: "review."));

        // Assert
        Assert.Equal([1L], underscore);
        Assert.Equal([3L], percent);
        Assert.Equal([4L], group);
    }

    /// <summary>
    ///     Test the day boundaries of the date range: the first instant of the From day and the last tick of the To
    ///     day are in, the last tick of the day before and the first instant of the day after are out, whatever
    ///     offset the stored time was written with.
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_DateRange_DayBoundariesAreInclusive()
    {
        // Arrange: instants around the day 2025-03-04 (UTC), some written with a non-zero offset
        using var db = new TestStore();
        var seed = new Seeder(db);
        seed.Add(new DateTimeOffset(2025, 3, 3, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999)); // 1: just before
        seed.Add(new DateTimeOffset(2025, 3, 4, 0, 0, 0, TimeSpan.Zero)); // 2: first instant
        seed.Add(new DateTimeOffset(2025, 3, 4, 12, 0, 0, TimeSpan.Zero)); // 3: midday
        seed.Add(new DateTimeOffset(2025, 3, 4, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999)); // 4: last tick
        seed.Add(new DateTimeOffset(2025, 3, 5, 0, 0, 0, TimeSpan.Zero)); // 5: first of next day
        seed.Add(new DateTimeOffset(2025, 3, 4, 3, 0, 0, TimeSpan.FromHours(5))); // 6: 2025-03-03 22:00 UTC
        seed.Add(new DateTimeOffset(2025, 3, 4, 22, 0, 0, TimeSpan.FromHours(-5))); // 7: 2025-03-05 03:00 UTC
        seed.Add(new DateTimeOffset(2025, 3, 4, 5, 0, 0, TimeSpan.FromHours(5))); // 8: 2025-03-04 00:00 UTC
        seed.Save();
        var day = new DateOnly(2025, 3, 4);

        // Act
        var exact = await Sequences(db, new AuditFilter(From: day, To: day));
        var fromOnly = await Sequences(db, new AuditFilter(From: day));
        var toOnly = await Sequences(db, new AuditFilter(To: day));
        var nextDay = await Sequences(db, new AuditFilter(From: day.AddDays(1), To: day.AddDays(1)));

        // Assert
        Assert.Equal([8L, 4L, 3L, 2L], exact);
        Assert.Equal([8L, 7L, 5L, 4L, 3L, 2L], fromOnly);
        Assert.Equal([8L, 6L, 4L, 3L, 2L, 1L], toOnly);
        Assert.Equal([7L, 5L], nextDay);
    }

    /// <summary>
    ///     Test that the stored time is fixed-width UTC text, reads back as the same instant (so the chain hash is
    ///     unchanged), and that a chain written with offset times still verifies after the read.
    /// </summary>
    [Fact]
    public async Task AuditStore_Timestamp_StoredAsUtcTextAndKeepsTheChainVerifiable()
    {
        // Arrange: a chain of three entries, two with a non-zero offset, hashed from the values as written
        using var db = new TestStore();
        var seed = new Seeder(db);
        seed.Add(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)));
        seed.Add(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero).AddTicks(1234567));
        seed.Add(new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.FromHours(-8)));
        seed.Save();

        // Act
        var read = await db.Audit.ReadAfterAsync(0, 10, TestContext.Current.CancellationToken);
        string[] text;
        await using (var connection = new SqliteConnection(db.ConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TimestampUtc FROM AuditEntries ORDER BY Sequence";
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            var list = new List<string>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                list.Add(reader.GetString(0));
            }

            text = [.. list];
        }

        // Assert
        Assert.Equal(
            ["2025-03-04T03:06:07.0000000Z", "2025-03-04T05:06:07.1234567Z", "2026-01-01T07:00:00.0000000Z"],
            text);
        Assert.Equal(seed.Rows.Select(e => e.TimestampUtc.UtcTicks), read.Select(e => e.TimestampUtc.UtcTicks));
        Assert.Equal(seed.Rows.Select(e => e.Hash), read.Select(e => AuditChain.ComputeHash(e)));
        Assert.True(AuditChain.Verify(read).IsIntact);
    }

    /// <summary>
    ///     Test the keyset paging with a filter: the older and the newer page continue from a position inside the
    ///     matching entries only, and a take of one looks past either end.
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_FilterWithCursors_PagesThroughMatchesOnly()
    {
        // Arrange: ten entries of project 1, every other one by "Bob"
        using var db = new TestStore();
        var seed = new Seeder(db);
        for (var i = 1; i <= 10; i++)
        {
            seed.Add(_base, i % 2 == 0 ? "Bob" : "Ann", "test.event", 1);
        }

        seed.Add(_base, "Bob", "test.event", 2);
        seed.Save();
        var bob = new AuditFilter(ProjectId: 1, ActorText: "bob");

        // Act
        var first = await Sequences(db, bob, take: 2);
        var older = await Sequences(db, bob, before: 8, take: 2);
        var newer = await Sequences(db, bob, after: 4, take: 2);
        var lookPast = await Sequences(db, bob, before: 2, take: 1);
        var lookNewer = await Sequences(db, bob, after: 10, take: 1);

        // Assert
        Assert.Equal([10L, 8L], first);
        Assert.Equal([6L, 4L], older);
        Assert.Equal([8L, 6L], newer);
        Assert.Empty(lookPast);
        Assert.Empty(lookNewer);
    }

    /// <summary>
    ///     Test that a filtered page of a larger trail is read quickly and that the page size bounds the rows
    ///     returned (the read never needs a count).
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_LargeTrail_FilteredPagesAreFastAndBounded()
    {
        // Arrange: 3,000 entries over 3 projects, 5 actors and 4 actions, each a minute apart
        using var db = new TestStore();
        var seed = new Seeder(db);
        string[] actions = ["review.created", "review.signed", "project.created", "user.created"];
        for (var i = 0; i < 3000; i++)
        {
            seed.Add(_base.AddMinutes(i), "Actor " + (i % 5), actions[i % 4], (i % 3) + 1, "r" + (i % 7), 100 + (i % 5));
        }

        seed.Save();
        var filter = new AuditFilter(
            ProjectId: 2, ActorText: "actor 1", ActionPrefix: "review.", From: new DateOnly(2025, 3, 4), To: new DateOnly(2025, 3, 6));

        // Act
        var watch = Stopwatch.StartNew();
        var page = await db.Audit.ListAsync(filter, null, null, 50, TestContext.Current.CancellationToken);
        var older = await db.Audit.ListAsync(filter, page[^1].Sequence, null, 1, TestContext.Current.CancellationToken);
        var action = await db.Audit.ListAsync(
            new AuditFilter(Action: "user.created"), null, null, 200, TestContext.Current.CancellationToken);
        watch.Stop();

        // Assert: a loose bound, only to catch a read that scans and sorts the whole trail repeatedly
        Assert.True(page.Count is > 0 and <= 50);
        Assert.All(page, e => Assert.True(e.ProjectId == 2 && e.ActorName == "Actor 1"));
        Assert.True(page.Zip(page.Skip(1)).All(pair => pair.First.Sequence > pair.Second.Sequence));
        Assert.True(older.Count <= 1);
        Assert.Equal(200, action.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Reads took {watch.Elapsed}.");
    }
}
