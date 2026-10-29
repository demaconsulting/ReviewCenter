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
using DemaConsulting.ReviewCenter.Identity;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     The services of the second factor over in-memory fakes, with the production algorithm, so the tests of the
///     services, the user rules and the signing share one setup.
/// </summary>
/// <remarks>
///     Codes are always computed with <see cref="TotpOracle"/>, never with the production algorithm. Not thread-safe
///     for setup; the services themselves can be used from several tasks at once.
/// </remarks>
internal sealed class TotpTestWorld
{
    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    public const string Password = "correct horse battery";

    /// <summary>
    ///     The instant the clock starts at: in the middle of a time step, so a code stays valid for a while.
    /// </summary>
    public static readonly DateTimeOffset Start = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Initializes a new instance of the <see cref="TotpTestWorld"/> class.
    /// </summary>
    /// <param name="settings">The settings; <c>null</c> for the defaults.</param>
    public TotpTestWorld(TotpSettings? settings = null)
    {
        Clock = new MovableTimeProvider(Start);
        Users = new FakeUserStore();
        AuditStore = new FakeAuditStore(Clock);
        Audit = new AuditService(AuditStore, null, Clock);
        Store = new FakeTotpStore();
        Protector = new FakeTotpSecretProtector();
        Challenges = new TotpChallengeStore(Clock);
        UserService = new UserService(Users, new FakePasswordHasher(), Clock, Audit, Store, settings);
        Totp = new TotpService(Store, new TotpAlgorithm(), Protector, UserService, Challenges, Clock, settings);
    }

    /// <summary>
    ///     Gets the clock the tests move.
    /// </summary>
    public MovableTimeProvider Clock { get; }

    /// <summary>
    ///     Gets the user store.
    /// </summary>
    public FakeUserStore Users { get; }

    /// <summary>
    ///     Gets the store of the audit trail.
    /// </summary>
    public FakeAuditStore AuditStore { get; }

    /// <summary>
    ///     Gets the audit service.
    /// </summary>
    public AuditService Audit { get; }

    /// <summary>
    ///     Gets the store of the second factor.
    /// </summary>
    public FakeTotpStore Store { get; }

    /// <summary>
    ///     Gets the secret protector.
    /// </summary>
    public FakeTotpSecretProtector Protector { get; }

    /// <summary>
    ///     Gets the pending sign-in challenges.
    /// </summary>
    public TotpChallengeStore Challenges { get; }

    /// <summary>
    ///     Gets the user service.
    /// </summary>
    public UserService UserService { get; }

    /// <summary>
    ///     Gets the service of the second factor.
    /// </summary>
    public TotpService Totp { get; }

    /// <summary>
    ///     Gets every text the audit trail and the user store's audit drafts hold, to search for secrets.
    /// </summary>
    public string AllAuditText =>
        string.Join('\n', AuditStore.Entries.Select(e => $"{e.Action}|{e.Detail}|{e.ActorName}"))
        + "\n" + string.Join('\n', Store.Audits.Select(a => $"{a.Action}|{a.Detail}|{a.ActorName}"))
        + "\n" + string.Join('\n', Users.Audits.Select(a => $"{a.Action}|{a.Detail}|{a.ActorName}"));

    /// <summary>
    ///     Creates a local user with the standard password.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="role">The role.</param>
    /// <returns>The user.</returns>
    public Task<User> AddUserAsync(string username, UserRole role = UserRole.Reviewer)
    {
        return UserService.CreateAsync(
            username, username, role, Password, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Enrolls a user completely: begin, confirm with the code of the current step, and keep the secret.
    /// </summary>
    /// <param name="username">The user name of an existing local user.</param>
    /// <returns>The secret as the authenticator app holds it, and the recovery codes shown once.</returns>
    public async Task<(byte[] Secret, IReadOnlyList<string> RecoveryCodes)> EnrollAsync(string username)
    {
        var actor = new Actor(username, username);
        var cancellationToken = TestContext.Current.CancellationToken;
        var enrollment = await Totp.BeginEnrollmentAsync(actor, Password, cancellationToken);
        var secret = TotpOracle.FromBase32(enrollment.SecretBase32);
        var recovery = await Totp.ConfirmEnrollmentAsync(actor, CodeNow(secret), cancellationToken);
        return (secret, recovery);
    }

    /// <summary>
    ///     Computes the code of the current time step, or one that is some steps away.
    /// </summary>
    /// <param name="secret">The secret.</param>
    /// <param name="stepOffset">The steps before (negative) or after (positive) the current one.</param>
    /// <returns>The six digit code.</returns>
    public string CodeNow(byte[] secret, int stepOffset = 0)
    {
        return TotpOracle.CodeAt(secret, Clock.GetUtcNow(), stepOffset);
    }

    /// <summary>
    ///     Moves the clock forward by whole time steps.
    /// </summary>
    /// <param name="steps">The number of 30 second steps.</param>
    public void AdvanceSteps(int steps = 1)
    {
        Clock.Advance(TimeSpan.FromSeconds(30 * steps));
    }

    /// <summary>
    ///     Starts a sign-in as the server does after the password was right.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="address">The client address.</param>
    /// <returns>The challenge token.</returns>
    public async Task<string> BeginSignInAsync(string username, string address = "10.0.0.1")
    {
        var user = (await UserService.GetAsync(username, TestContext.Current.CancellationToken))!;
        return Totp.BeginSignIn(user, address, "/");
    }
}
