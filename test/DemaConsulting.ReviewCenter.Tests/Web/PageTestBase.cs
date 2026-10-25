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
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Base class for page tests: a bUnit context wired to the real domain services over an in-memory store.
/// </summary>
/// <remarks>
///     Using the real services (not mocks of them) means the pages are tested against the real validation
///     rules, so what a test sees is what the operator would see. Pages that need a signed-in user see
///     <c>ann</c> (display name <c>Ann</c>, role reviewer) unless a test calls <see cref="SignInAs"/>.
/// </remarks>
public abstract class PageTestBase : BunitContext
{
    /// <summary>
    ///     The authentication state the pages read.
    /// </summary>
    private readonly TestAuthenticationStateProvider _authentication = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="PageTestBase"/> class and registers the services.
    /// </summary>
    protected PageTestBase()
    {
        var store = new FakeReviewStore();
        Store = store;
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        Projects = new ProjectService(store, clock);
        Reviews = new ReviewService(store, clock);
        Threads = new ThreadService(store, clock);
        Checklists = new ChecklistService(store, clock);
        Users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        Services.AddSingleton(Projects);
        Services.AddSingleton(Reviews);
        Services.AddSingleton(Threads);
        Services.AddSingleton(Checklists);
        Services.AddSingleton(Users);
        Signing = new SigningService(store, Users, clock);
        Services.AddSingleton(Signing);
        Services.AddSingleton<AuthenticationStateProvider>(_authentication);

        // bUnit pre-registers placeholder authorization services that throw when used; replace them with the
        // real ones so that AuthorizeRouteView evaluates the [Authorize] attributes of the pages
        Services.RemoveAll<IAuthorizationService>();
        Services.RemoveAll<IAuthorizationPolicyProvider>();
        Services.AddAuthorizationCore();
        Services.AddCascadingAuthenticationState();
    }

    /// <summary>
    ///     Gets the in-memory store behind the services, for arranging data the services would not create.
    /// </summary>
    internal FakeReviewStore Store { get; }

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
    ///     Gets the signing service the pages use.
    /// </summary>
    protected SigningService Signing { get; }

    /// <summary>
    ///     Makes the pages see another signed-in user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="role">The role of the user.</param>
    protected void SignInAs(string username, string displayName, UserRole role = UserRole.Reviewer)
    {
        _authentication.Change(username, displayName, role);
    }

    /// <summary>
    ///     Makes the pages see nobody signed in.
    /// </summary>
    protected void SignOut()
    {
        _authentication.SignOut();
    }

    /// <summary>
    ///     An authentication state provider whose signed-in user a test can change.
    /// </summary>
    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        /// <summary>
        ///     The current state.
        /// </summary>
        private AuthenticationState _state = Create("ann", "Ann", UserRole.Reviewer);

        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(_state);
        }

        /// <summary>
        ///     Signs everybody out.
        /// </summary>
        public void SignOut()
        {
            _state = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        /// <summary>
        ///     Changes the signed-in user.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="role">The role of the user.</param>
        public void Change(string username, string displayName, UserRole role)
        {
            _state = Create(username, displayName, role);
        }

        /// <summary>
        ///     Builds the state of a signed-in user with the claims the application issues.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="role">The role of the user.</param>
        /// <returns>The state.</returns>
        private static AuthenticationState Create(string username, string displayName, UserRole role)
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, username),
                    new Claim(SignIn.DisplayNameClaimType, displayName),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                "Test");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
    }
}
