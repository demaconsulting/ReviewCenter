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

using System.Security.Claims;
using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Tests.Evidence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Base class for page tests: a bUnit context wired to the real domain services over an in-memory store.
/// </summary>
/// <remarks>
///     Using the real services (not mocks of them) means the pages are tested against the real validation
///     rules, so what a test sees is what the operator would see. Pages that need a signed-in user see
///     <c>ann</c> (display name <c>Ann</c>, a system administrator) unless a test calls
///     <see cref="SignInAs(string, string, bool)"/>.
///     The services the pages use enforce the real access rules; the services exposed as properties allow
///     everything, so that a test can arrange its data.
/// </remarks>
public abstract class PageTestBase : BunitContext
{
    /// <summary>
    ///     The authentication state the pages read.
    /// </summary>
    private readonly TestAuthenticationStateProvider _authentication = new();

    /// <summary>
    ///     Whether <see cref="UseRevalidation"/> replaced the authentication state provider.
    /// </summary>
    private bool _revalidating;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PageTestBase"/> class and registers the services.
    /// </summary>
    protected PageTestBase()
    {
        var store = new FakeReviewStore();
        Store = store;
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        Time = new ManualTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(Time);
        AuditStore = new FakeAuditStore(clock);
        Audit = new AuditService(AuditStore);
        var userStore = new FakeUserStore();
        UserStore = userStore;
        Users = new UserService(userStore, new FakePasswordHasher(), clock, Audit);
        TokenStore = new FakePersonalAccessTokenStore(userStore);
        Tokens = new PersonalAccessTokenService(TokenStore, userStore, Time);
        Services.AddSingleton(Tokens);
        RepositoryStore = new FakeRepositoryStore();
        Git = new FakeGitRepositoryProvider();
        SetParser = new FakeReviewSetParser();

        // The services the pages use enforce the real access rules for the signed-in user. The services a test
        // uses to arrange data (the properties below) share the stores but allow everything, so that a test does
        // not have to be a member of a project to prepare it.
        var allowAll = new AllowAllProjectAccess(store);
        Projects = new ProjectService(store, userStore, allowAll, clock);
        Threads = new ThreadService(store, allowAll, clock);
        Checklists = new ChecklistService(store, allowAll, clock);
        Repositories = new RepositoryService(RepositoryStore, new FakeCredentialProtector(), allowAll, clock);
        Reviews = new ReviewService(store, allowAll, clock, Repositories, Git);
        Signing = new SigningService(store, Users, allowAll, clock);
        Planner = new ReviewPlanner(store, Reviews, Repositories, Git, Signing, SetParser, allowAll);

        Access = new SignedInUserAccess(new ProjectAccess(userStore, store), userStore, () => _revalidating ? null : _authentication.Current);
        Services.AddSingleton<IProjectAccess>(Access);
        var pageProjects = new ProjectService(store, userStore, Access, clock);
        var pageRepositories = new RepositoryService(RepositoryStore, new FakeCredentialProtector(), Access, clock);
        var pageReviews = new ReviewService(store, Access, clock, pageRepositories, Git);
        var pageSigning = new SigningService(store, Users, Access, clock);
        Services.AddSingleton(Audit);
        Services.AddSingleton(pageProjects);
        Services.AddSingleton(pageReviews);
        Services.AddSingleton(new ThreadService(store, Access, clock));
        Services.AddSingleton(new ChecklistService(store, Access, clock));
        Services.AddSingleton(Users);
        Services.AddSingleton(pageRepositories);
        Services.AddSingleton(pageSigning);
        Services.AddSingleton(new ReviewPlanner(store, pageReviews, pageRepositories, Git, pageSigning, SetParser, Access));
        Services.AddSingleton<AuthenticationStateProvider>(_authentication);

        // The review page offers the attestation; the default key is an in-memory self-signed key, and a test that
        // needs another key or a problem registers its own provider before the first render
        Services.AddSingleton<IAttestationKeyProvider>(_ => new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa()));

        // bUnit pre-registers placeholder authorization services that throw when used; replace them with the
        // real ones so that AuthorizeRouteView evaluates the [Authorize] attributes of the pages
        Services.RemoveAll<IAuthorizationService>();
        Services.RemoveAll<IAuthorizationPolicyProvider>();
        Services.AddAuthorizationCore();
        Services.AddCascadingAuthenticationState();
    }

    /// <summary>
    ///     Gets the clock the pages use for timed feedback; a test moves it with
    ///     <see cref="ManualTimeProvider.Advance"/>.
    /// </summary>
    internal ManualTimeProvider Time { get; }

    /// <summary>
    ///     Gets the in-memory store behind the services, for arranging data the services would not create.
    /// </summary>
    internal FakeReviewStore Store { get; }

    /// <summary>
    ///     Gets the in-memory audit store behind the audit service, for arranging and tampering with entries.
    /// </summary>
    internal FakeAuditStore AuditStore { get; }

    /// <summary>
    ///     Gets the in-memory store behind the repository service, for arranging and inspecting registrations.
    /// </summary>
    internal FakeRepositoryStore RepositoryStore { get; }

    /// <summary>
    ///     Gets the in-memory token store behind the token service, for arranging and inspecting tokens.
    /// </summary>
    internal FakePersonalAccessTokenStore TokenStore { get; }

    /// <summary>
    ///     Gets the in-memory user store behind the user service.
    /// </summary>
    internal FakeUserStore UserStore { get; }

    /// <summary>
    ///     Gets the real access rules the pages use, for asking what a user may do.
    /// </summary>
    internal IProjectAccess Access { get; }

    /// <summary>
    ///     Gets the personal access token service the account page uses.
    /// </summary>
    protected PersonalAccessTokenService Tokens { get; }

    /// <summary>
    ///     Gets the scripted git provider behind the review service, for arranging repository reviews.
    /// </summary>
    internal FakeGitRepositoryProvider Git { get; }

    /// <summary>
    ///     Gets the scripted review-set parser behind the planner, for arranging the review-sets of a head.
    /// </summary>
    internal FakeReviewSetParser SetParser { get; }

    /// <summary>
    ///     Gets the audit service the pages use.
    /// </summary>
    protected AuditService Audit { get; }

    /// <summary>
    ///     Gets the project service the pages use.
    /// </summary>
    protected ProjectService Projects { get; }

    /// <summary>
    ///     Gets the review service the pages use.
    /// </summary>
    protected ReviewService Reviews { get; }

    /// <summary>
    ///     Gets the thread service the pages use.
    /// </summary>
    protected ThreadService Threads { get; }

    /// <summary>
    ///     Gets the checklist service the pages use.
    /// </summary>
    protected ChecklistService Checklists { get; }

    /// <summary>
    ///     Gets the user service the pages use.
    /// </summary>
    protected UserService Users { get; }

    /// <summary>
    ///     Gets the repository service the pages use.
    /// </summary>
    protected RepositoryService Repositories { get; }

    /// <summary>
    ///     Gets the planner the plan page uses.
    /// </summary>
    protected ReviewPlanner Planner { get; }

    /// <summary>
    ///     Gets the signing service the pages use.
    /// </summary>
    protected SigningService Signing { get; }

    /// <summary>
    ///     Makes the pages see another signed-in user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="isSystemAdmin">Whether the user is a system administrator; the default is <c>true</c>.</param>
    /// <remarks>
    ///     The user is created in the store when the first access check needs it and the test has not created it.
    ///     Tests of the access rules pass <c>false</c> and add a membership or none.
    /// </remarks>
    protected void SignInAs(string username, string displayName, bool isSystemAdmin = true)
    {
        if (_revalidating)
        {
            ((ServerAuthenticationStateProvider)Services.GetRequiredService<AuthenticationStateProvider>())
                .SetAuthenticationState(Task.FromResult(Create(username, displayName, isSystemAdmin)));
            return;
        }

        _authentication.Change(username, displayName, isSystemAdmin);
    }

    /// <summary>
    ///     Makes the pages see another signed-in user, using the test shorthand <see cref="UserRole"/>.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="role">The role of the user.</param>
    protected void SignInAs(string username, string displayName, UserRole role)
    {
        SignInAs(username, displayName, role == UserRole.Admin);
    }

    /// <summary>
    ///     Makes the pages see nobody signed in.
    /// </summary>
    protected void SignOut()
    {
        if (_revalidating)
        {
            ((ServerAuthenticationStateProvider)Services.GetRequiredService<AuthenticationStateProvider>())
                .SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
            return;
        }

        _authentication.SignOut();
    }

    /// <summary>
    ///     Makes the pages see nobody signed in, without telling the components that listen: the short time in
    ///     which an open page still shows its actions although the session has ended.
    /// </summary>
    protected void SignOutQuietly()
    {
        _authentication.SignOutQuietly();
    }

    /// <summary>
    ///     Replaces the authentication state provider by the real <see cref="RevalidatingAuthenticationStateProvider"/>,
    ///     so that the page ends its session when the user in the store is no longer valid.
    /// </summary>
    /// <param name="interval">The time between two checks; short so that a test does not have to wait long.</param>
    /// <remarks>
    ///     Call this before the first render and before <see cref="SignInAs(string, string, bool)"/>: nobody is
    ///     signed in until then. The
    ///     provider is created by the container, which also disposes it (and so stops its loop) with the context.
    /// </remarks>
    protected void UseRevalidation(TimeSpan interval)
    {
        Services.RemoveAll<AuthenticationStateProvider>();
        Services.AddSingleton<AuthenticationStateProvider>(_ =>
            new RevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, Users, interval));
        _revalidating = true;
    }

    /// <summary>
    ///     Builds the state of a signed-in user with the claims the application issues.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="isSystemAdmin">Whether the user is a system administrator.</param>
    /// <returns>The state.</returns>
    private static AuthenticationState Create(string username, string displayName, bool isSystemAdmin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(SignIn.DisplayNameClaimType, displayName)
        };
        if (isSystemAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, SignIn.AdminRole));
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")));
    }

    /// <summary>
    ///     An authentication state provider whose signed-in user a test can change.
    /// </summary>
    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        /// <summary>
        ///     The current state.
        /// </summary>
        private AuthenticationState _state = Create("ann", "Ann", true);

        /// <summary>
        ///     Gets the signed-in user, or <c>null</c> when nobody is signed in.
        /// </summary>
        public (string Username, string DisplayName, bool IsSystemAdmin)? Current
        {
            get
            {
                var user = _state.User;
                return user.Identity?.IsAuthenticated == true
                    ? (user.Identity.Name ?? string.Empty,
                        user.FindFirst(SignIn.DisplayNameClaimType)?.Value ?? string.Empty,
                        user.IsInRole(SignIn.AdminRole))
                    : null;
            }
        }

        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(_state);
        }

        /// <summary>
        ///     Signs everybody out and tells the components that listen.
        /// </summary>
        public void SignOut()
        {
            _state = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }

        /// <summary>
        ///     Signs everybody out without telling the components that listen, as in the short time before an
        ///     open page learns that its session has ended.
        /// </summary>
        public void SignOutQuietly()
        {
            _state = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        /// <summary>
        ///     Changes the signed-in user and tells the components that listen.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="isSystemAdmin">Whether the user is a system administrator.</param>
        public void Change(string username, string displayName, bool isSystemAdmin)
        {
            _state = Create(username, displayName, isSystemAdmin);
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }
    }
}

/// <summary>
///     A <see cref="TimeProvider"/> whose clock only moves when a test calls <see cref="Advance"/>.
/// </summary>
/// <remarks>
///     Only one-shot timers are supported (the period is ignored), which is all that
///     <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> needs. A timer fires on the thread
///     that calls <see cref="Advance"/>, once the clock has reached its due time.
/// </remarks>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>
    ///     Guards <see cref="_now"/> and <see cref="_timers"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The timers that have not been disposed.
    /// </summary>
    private readonly List<ManualTimer> _timers = [];

    /// <summary>
    ///     The current instant of the clock.
    /// </summary>
    private DateTimeOffset _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_lock)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    ///     Moves the clock forward and fires every timer that has become due.
    /// </summary>
    /// <param name="amount">How far to move the clock.</param>
    public void Advance(TimeSpan amount)
    {
        List<ManualTimer> due;
        lock (_lock)
        {
            _now += amount;
            due = [.. _timers.Where(timer => timer.DueTime <= _now)];
            foreach (var timer in due)
            {
                // One-shot: a fired timer waits until it is changed again
                timer.DueTime = null;
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    /// <summary>
    ///     A one-shot timer of the provider.
    /// </summary>
    /// <param name="owner">The provider that created the timer.</param>
    /// <param name="callback">The method to call when the timer fires.</param>
    /// <param name="state">The state passed to the callback.</param>
    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        /// <summary>
        ///     Gets or sets the instant at which the timer fires, or <c>null</c> when it is not running; guarded by
        ///     the lock of the owner.
        /// </summary>
        public DateTimeOffset? DueTime { get; set; }

        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._lock)
            {
                DueTime = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            }

            return true;
        }

        /// <summary>
        ///     Calls the callback of the timer.
        /// </summary>
        public void Fire()
        {
            callback(state);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (owner._lock)
            {
                DueTime = null;
                owner._timers.Remove(this);
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
