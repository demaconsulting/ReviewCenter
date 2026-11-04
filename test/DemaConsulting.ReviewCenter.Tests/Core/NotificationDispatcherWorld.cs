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
///     The world of the dispatcher tests: the real inbox service over the fake review store of
///     <see cref="InboxTestWorld"/>, the fake notification store and sender, a counting audit store and a counting
///     inbox store, and a dispatcher built from them. Nothing sleeps; the clock moves when a test moves it.
/// </summary>
internal sealed class NotificationDispatcherWorld
{
    /// <summary>
    ///     The address links in a mail start with.
    /// </summary>
    public const string BaseUrl = "https://reviews.example.com";

    /// <summary>
    ///     The batch window of the mode every change, when a test does not choose one.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The settings of the dispatcher.
    /// </summary>
    private readonly NotificationSettings _settings;

    /// <summary>
    ///     The length of the batch window.
    /// </summary>
    private readonly TimeSpan _window;

    /// <summary>
    ///     The schedule of the daily summary.
    /// </summary>
    private readonly DigestSchedule _schedule;

    /// <summary>
    ///     Whether the title of a review is part of a mail.
    /// </summary>
    private readonly bool _includeTitles;

    /// <summary>
    ///     The inbox service the dispatcher reads, built over the counting inbox store.
    /// </summary>
    private readonly InboxService _inboxService;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationDispatcherWorld"/> class.
    /// </summary>
    /// <param name="inbox">The world of reviews and users.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="window">The batch window.</param>
    /// <param name="schedule">The schedule of the daily summary.</param>
    /// <param name="includeTitles">Whether titles are part of a mail.</param>
    private NotificationDispatcherWorld(
        InboxTestWorld inbox,
        NotificationSettings settings,
        TimeSpan window,
        DigestSchedule schedule,
        bool includeTitles)
    {
        Inbox = inbox;
        _settings = settings;
        _window = window;
        _schedule = schedule;
        _includeTitles = includeTitles;
        AuditStore = new FakeAuditStore(inbox.Clock);
        Audit = new CountingAuditStore(AuditStore);
        InboxStore = new CountingInboxStore(inbox.InboxStore);
        _inboxService = new InboxService(InboxStore, inbox.Store, inbox.Access, inbox.UserStore, inbox.Clock);
        Dispatcher = NewDispatcher();
    }

    /// <summary>
    ///     Gets the world of reviews, users and the real inbox service.
    /// </summary>
    public InboxTestWorld Inbox { get; }

    /// <summary>
    ///     Gets the fake audit store.
    /// </summary>
    public FakeAuditStore AuditStore { get; }

    /// <summary>
    ///     Gets the audit store the dispatcher reads; it counts the reads of the latest sequence.
    /// </summary>
    public CountingAuditStore Audit { get; }

    /// <summary>
    ///     Gets the inbox store the dispatcher reads through the inbox service; it counts the reads.
    /// </summary>
    public CountingInboxStore InboxStore { get; }

    /// <summary>
    ///     Gets the fake notification store.
    /// </summary>
    public FakeNotificationStore Store { get; } = new();

    /// <summary>
    ///     Gets the fake sender.
    /// </summary>
    public FakeNotificationSender Sender { get; } = new();

    /// <summary>
    ///     Gets the errors the dispatcher reported through its callback.
    /// </summary>
    public List<Exception> Errors { get; } = [];

    /// <summary>
    ///     Gets the dispatcher under test.
    /// </summary>
    public NotificationDispatcher Dispatcher { get; private set; }

    /// <summary>
    ///     Gets the clock the services and the dispatcher share.
    /// </summary>
    public MovableTimeProvider Clock => Inbox.Clock;

    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Creates the world.
    /// </summary>
    /// <param name="settings">The settings; the default ones when omitted.</param>
    /// <param name="batchWindow">The batch window; <see cref="DefaultWindow"/> when omitted.</param>
    /// <param name="digestHour">The digest hour; 8 when omitted.</param>
    /// <param name="zone">The time zone of the digest; UTC when omitted.</param>
    /// <param name="includeTitles">Whether titles are part of a mail.</param>
    /// <param name="projectName">The name of the project; <c>Apollo</c> when omitted.</param>
    /// <returns>The world.</returns>
    public static async Task<NotificationDispatcherWorld> CreateAsync(
        NotificationSettings? settings = null,
        TimeSpan? batchWindow = null,
        int digestHour = 8,
        TimeZoneInfo? zone = null,
        bool includeTitles = true,
        string projectName = "Apollo")
    {
        var inbox = await InboxTestWorld.CreateAsync(projectName);
        return new NotificationDispatcherWorld(
            inbox,
            settings ?? new NotificationSettings(true, "Test Center", NotificationSettings.DefaultMaxMailsPerHour),
            batchWindow ?? DefaultWindow,
            new DigestSchedule(digestHour, zone ?? TimeZoneInfo.Utc),
            includeTitles);
    }

    /// <summary>
    ///     Builds a dispatcher over the world, as a restarted server would.
    /// </summary>
    /// <returns>The new dispatcher, which remembers nothing.</returns>
    public NotificationDispatcher NewDispatcher()
    {
        return new NotificationDispatcher(
            Store,
            _inboxService,
            Audit,
            Sender,
            new NotificationComposer(_settings.InstanceName, BaseUrl, _includeTitles),
            _schedule,
            _settings,
            Clock,
            _window,
            Errors.Add);
    }

    /// <summary>
    ///     Replaces the dispatcher by a new one, as a restart of the server does.
    /// </summary>
    public void Restart()
    {
        Dispatcher = NewDispatcher();
    }

    /// <summary>
    ///     Gets the identity of a user of the world.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The identity.</returns>
    public int IdOf(string username)
    {
        return Inbox.Ids[username];
    }

    /// <summary>
    ///     Gets the address of a user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The confirmed address the world gives the user.</returns>
    public static string AddressOf(string username)
    {
        return $"{username}@example.com";
    }

    /// <summary>
    ///     Makes a user a recipient with a confirmed address.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="baselinePending">Whether the baseline is pending, as right after the mode was switched on.</param>
    /// <param name="isDisabled">Whether the account is disabled.</param>
    /// <param name="mustChangePassword">Whether the user has to replace a temporary password.</param>
    /// <param name="displayName">The display name of the user; the upper-case user name when omitted.</param>
    public void Enroll(
        string username,
        NotificationMode mode,
        bool baselinePending = false,
        bool isDisabled = false,
        bool mustChangePassword = false,
        string? displayName = null)
    {
        Store.Seed(IdOf(username), username, AddressOf(username), mode, baselinePending, isDisabled, mustChangePassword, displayName);
    }

    /// <summary>
    ///     Runs one pass of the dispatcher.
    /// </summary>
    /// <returns>The result.</returns>
    public Task<NotificationPassResult> PassAsync()
    {
        return Dispatcher.RunPassAsync(Ct);
    }

    /// <summary>
    ///     Appends an entry to the audit trail, so the sequence the dispatcher reads moves, as every change of a
    ///     review does.
    /// </summary>
    /// <returns>A task that completes when the entry is stored.</returns>
    public async Task ChangeAsync()
    {
        await AuditStore.AppendAsync(AuditDraft.By(InboxTestWorld.ActorOf("author"), "test.change", "change"), Ct);
    }

    /// <summary>
    ///     Moves the clock forward.
    /// </summary>
    /// <param name="span">How far.</param>
    public void Advance(TimeSpan span)
    {
        Clock.Advance(span);
    }

    /// <summary>
    ///     Moves the clock forward to an instant.
    /// </summary>
    /// <param name="utc">The instant; not before the clock.</param>
    public void AdvanceTo(DateTimeOffset utc)
    {
        Clock.Advance(utc - Clock.GetUtcNow());
    }

    /// <summary>
    ///     Creates a review with a seat for a reviewer, so the reviewer finds it to answer in the inbox, and moves
    ///     the audit sequence.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="reviewer">The user name of the reviewer.</param>
    /// <param name="title">The title.</param>
    /// <returns>A task that completes when the review exists.</returns>
    public async Task SeatAsync(string reviewId, string reviewer, string title = "A review")
    {
        await Inbox.CreateReviewAsync(reviewId, "author", null, title);
        await Inbox.AddSlotAsync(reviewId, reviewer);
        await ChangeAsync();
    }

    /// <summary>
    ///     Runs the passes of a recipient of the mode every change: one that starts the window, then one after the
    ///     window.
    /// </summary>
    /// <returns>The messages sent so far.</returns>
    public async Task<IReadOnlyList<NotificationMessage>> DeliverEveryAsync()
    {
        await PassAsync();
        Advance(_window);
        await PassAsync();
        return Sender.Sent;
    }

    /// <summary>
    ///     Creates a review that ann and bob approve, ann first, so ann finds an "approved" update in the inbox, and
    ///     moves the audit sequence.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>A task that completes when the review is approved.</returns>
    public async Task ApproveAsync(string reviewId)
    {
        await Inbox.CreateReviewAsync(reviewId);
        await Inbox.AnswerAllAsync(reviewId);
        var first = await Inbox.AddSlotAsync(reviewId, "ann");
        var second = await Inbox.AddSlotAsync(reviewId, "bob");
        await Inbox.SignAsync(reviewId, first.Id, "ann");
        Advance(TimeSpan.FromMinutes(1));
        await Inbox.SignAsync(reviewId, second.Id, "bob");
        await ChangeAsync();
    }

    /// <summary>
    ///     Reopens a review that <see cref="ApproveAsync"/> approved, as cyd, and moves the audit sequence.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>A task that completes when the review is reopened.</returns>
    public async Task ReopenAsync(string reviewId)
    {
        Advance(TimeSpan.FromMinutes(1));
        await Inbox.Signing.ReopenReviewAsync(InboxTestWorld.ActorOf("cyd"), Inbox.ProjectId, reviewId, "One more thing", Ct);
        await ChangeAsync();
    }

    /// <summary>
    ///     Gets the keys of the items notified to a user, as text.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>For example <c>r1/ToAnswer=to-answer</c>, ordered.</returns>
    public IReadOnlyList<string> NotifiedOf(string username)
    {
        return Store.NotifiedOf(IdOf(username)).Select(i => $"{i.ReviewId}/{i.Section}={i.State}").ToList();
    }

    /// <summary>
    ///     Counts the messages sent to the address of a user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The number of messages.</returns>
    public int SentTo(string username)
    {
        return Sender.Sent.Count(m => m.ToAddress == AddressOf(username));
    }

    /// <summary>
    ///     An audit store that counts how often the latest sequence is read and can fail on it.
    /// </summary>
    /// <param name="inner">The store it delegates to.</param>
    internal sealed class CountingAuditStore(FakeAuditStore inner) : IAuditStore
    {
        /// <summary>
        ///     Gets how often the latest sequence was read.
        /// </summary>
        public int LatestSequenceReads { get; private set; }

        /// <summary>
        ///     Gets or sets an exception that the read of the latest sequence throws, or <c>null</c>.
        /// </summary>
        public Exception? FailLatestSequence { get; set; }

        /// <inheritdoc />
        public Task<AuditEntry> AppendAsync(AuditDraft draft, CancellationToken cancellationToken = default)
        {
            return inner.AppendAsync(draft, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AuditEntry>> ListAsync(
            AuditFilter filter,
            long? beforeSequence,
            long? afterSequence,
            int take,
            CancellationToken cancellationToken = default)
        {
            return inner.ListAsync(filter, beforeSequence, afterSequence, take, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AuditEntry>> ReadAfterAsync(
            long afterSequence,
            int count,
            CancellationToken cancellationToken = default)
        {
            return inner.ReadAfterAsync(afterSequence, count, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<AuditEntry>> ReadMatchingAsync(
            AuditFilter filter,
            long afterSequence,
            long upToSequence,
            int count,
            CancellationToken cancellationToken = default)
        {
            return inner.ReadMatchingAsync(filter, afterSequence, upToSequence, count, cancellationToken);
        }

        /// <inheritdoc />
        public Task<long> LatestSequenceAsync(CancellationToken cancellationToken = default)
        {
            LatestSequenceReads++;
            if (FailLatestSequence is not null)
            {
                throw FailLatestSequence;
            }

            return inner.LatestSequenceAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyDictionary<string, DateTimeOffset>> LastActivityAsync(
            int projectId,
            CancellationToken cancellationToken = default)
        {
            return inner.LastActivityAsync(projectId, cancellationToken);
        }
    }

    /// <summary>
    ///     An inbox store that counts how often the involved reviews are read and can fail for one user.
    /// </summary>
    /// <param name="inner">The inbox store it delegates to.</param>
    internal sealed class CountingInboxStore(FakeInboxStore inner) : IInboxStore
    {
        /// <summary>
        ///     The store it delegates to.
        /// </summary>
        private readonly FakeInboxStore _inner = inner;

        /// <summary>
        ///     Gets how often the involved reviews of any user were read.
        /// </summary>
        public int InboxReads { get; private set; }

        /// <summary>
        ///     Gets the identities of the users whose involved reviews were read, in order.
        /// </summary>
        public List<int> ReadUserIds { get; } = [];

        /// <summary>
        ///     Gets or sets the identity of a user whose read throws, or <c>null</c> for none.
        /// </summary>
        public int? FailForUserId { get; set; }

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxCandidate>> ListInvolvedReviewsAsync(
            int userId,
            string username,
            CancellationToken cancellationToken = default)
        {
            InboxReads++;
            ReadUserIds.Add(userId);
            if (FailForUserId == userId)
            {
                throw new InvalidOperationException("The inbox store was told to fail.");
            }

            return _inner.ListInvolvedReviewsAsync(userId, username, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ReviewUserState>> ListStatesAsync(int userId, CancellationToken cancellationToken = default)
        {
            return _inner.ListStatesAsync(userId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<ReviewUserState?> GetStateAsync(
            int userId,
            int projectId,
            string reviewId,
            CancellationToken cancellationToken = default)
        {
            return _inner.GetStateAsync(userId, projectId, reviewId, cancellationToken);
        }

        /// <inheritdoc />
        public Task MarkSeenAsync(
            int userId,
            int projectId,
            string reviewId,
            int lastSeenCommentId,
            DateTimeOffset seenAt,
            CancellationToken cancellationToken = default)
        {
            return _inner.MarkSeenAsync(userId, projectId, reviewId, lastSeenCommentId, seenAt, cancellationToken);
        }

        /// <inheritdoc />
        public Task MarkSeenManyAsync(
            int userId,
            IReadOnlyList<InboxSeenMark> marks,
            DateTimeOffset seenAt,
            CancellationToken cancellationToken = default)
        {
            return _inner.MarkSeenManyAsync(userId, marks, seenAt, cancellationToken);
        }
    }
}
