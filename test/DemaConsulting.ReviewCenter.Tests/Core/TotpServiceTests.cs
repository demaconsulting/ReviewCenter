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

using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="TotpService"/>: enrollment, the second step of the sign-in, recovery codes, removal
///     and reset, the lockout, and that no secret ever reaches the audit trail. Every code is computed by
///     <see cref="TotpOracle"/>, not by the production algorithm.
/// </summary>
public partial class TotpServiceTests
{
    /// <summary>
    ///     Matches a recovery code in the form shown to people.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$")]
    private static partial Regex RecoveryShape();

    /// <summary>
    ///     Test that starting the enrollment returns a 160-bit secret in both forms and changes nothing for sign-in.
    /// </summary>
    [Fact]
    public async Task TotpService_BeginEnrollmentAsync_ValidPassword_ReturnsSecretAndUriButIsNotActive()
    {
        // Arrange: a user without a second factor
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: start the enrollment
        var enrollment = await world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, cancellationToken);
        var status = await world.Totp.GetStatusAsync(TestActors.Ann, cancellationToken);
        var step = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, cancellationToken);

        // Assert: 20 random bytes, a matching address, not active, and the password alone still signs in
        Assert.Equal(20, TotpOracle.FromBase32(enrollment.SecretBase32).Length);
        Assert.StartsWith("otpauth://totp/", enrollment.OtpauthUri, StringComparison.Ordinal);
        Assert.Contains("secret=" + enrollment.SecretBase32, enrollment.OtpauthUri, StringComparison.Ordinal);
        Assert.Contains("issuer=ReviewCenter", enrollment.OtpauthUri, StringComparison.Ordinal);
        Assert.True(status.Available);
        Assert.False(status.Enrolled);
        Assert.NotNull(step.User);
        Assert.False(step.SecondFactorRequired);
    }

    /// <summary>
    ///     Test that two enrollments get different secrets.
    /// </summary>
    [Fact]
    public async Task TotpService_BeginEnrollmentAsync_Twice_ReplacesThePendingSecret()
    {
        // Arrange: a user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: start twice
        var first = await world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, cancellationToken);
        var second = await world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, cancellationToken);

        // Assert: different secrets; the first one's code no longer confirms
        Assert.NotEqual(first.SecretBase32, second.SecretBase32);
        var oldCode = world.CodeNow(TotpOracle.FromBase32(first.SecretBase32));
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ConfirmEnrollmentAsync(TestActors.Ann, oldCode, cancellationToken));
        Assert.Equal(DomainRule.TotpCodeIncorrect, exception.Rule);
    }

    /// <summary>
    ///     Test that a wrong password stops the enrollment before a secret exists.
    /// </summary>
    [Fact]
    public async Task TotpService_BeginEnrollmentAsync_WrongPassword_ThrowsAndStoresNothing()
    {
        // Arrange: a user
        var world = new TotpTestWorld();
        var user = await world.AddUserAsync("ann");

        // Act: start with a wrong password
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.BeginEnrollmentAsync(TestActors.Ann, "not the password", TestContext.Current.CancellationToken));

        // Assert: refused and no row exists
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, exception.Rule);
        Assert.Null(await world.Store.GetAsync(user.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the accounts that can not use the feature are refused with the same rule.
    /// </summary>
    /// <param name="kind">The kind of account.</param>
    [Theory]
    [InlineData("api-token")]
    [InlineData("external")]
    [InlineData("must-change")]
    public async Task TotpService_BeginEnrollmentAsync_UnavailableAccount_ThrowsTotpNotAvailable(string kind)
    {
        // Arrange: the account of the kind
        var world = new TotpTestWorld();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = TestActors.Ann;
        switch (kind)
        {
            case "api-token":
                await world.AddUserAsync("ann");
                actor = new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" };
                break;
            case "external":
                await world.UserService.SignInExternalAsync("https://idp.example/", "sub-1", "ann", "Ann", cancellationToken);
                break;
            default:
                await world.Users.AddAsync(
                    new User
                    {
                        Username = "ann",
                        DisplayName = "Ann",
                        Enabled = true,
                        PasswordHash = "hash:" + TotpTestWorld.Password,
                        MustChangePassword = true,
                        Created = world.Clock.GetUtcNow()
                    },
                    _ => AuditDraft.By(Actor.CommandLine, "user.created", "test"),
                    cancellationToken);
                break;
        }

        // Act: start the enrollment
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.BeginEnrollmentAsync(actor, TotpTestWorld.Password, cancellationToken));

        // Assert: not available
        Assert.Equal(DomainRule.TotpNotAvailable, exception.Rule);
    }

    /// <summary>
    ///     Test that a switched-off feature refuses every account operation.
    /// </summary>
    [Fact]
    public async Task TotpService_BeginEnrollmentAsync_FeatureDisabled_ThrowsTotpNotAvailable()
    {
        // Arrange: a server with the feature off
        var world = new TotpTestWorld(new TotpSettings(false, "ReviewCenter"));
        await world.AddUserAsync("ann");

        // Act: start the enrollment and read the status
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, TestContext.Current.CancellationToken));
        var status = await world.Totp.GetStatusAsync(TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: refused, and the status says the feature is off
        Assert.Equal(DomainRule.TotpNotAvailable, exception.Rule);
        Assert.False(status.FeatureEnabled);
        Assert.False(status.Available);
    }

    /// <summary>
    ///     Test that a second enrollment is refused while one is active.
    /// </summary>
    [Fact]
    public async Task TotpService_BeginEnrollmentAsync_AlreadyEnrolled_ThrowsTotpAlreadyEnrolled()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");

        // Act: start again
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, TestContext.Current.CancellationToken));

        // Assert: refused
        Assert.Equal(DomainRule.TotpAlreadyEnrolled, exception.Rule);
    }

    /// <summary>
    ///     Test that confirming with the right code turns the second factor on and issues ten distinct recovery codes.
    /// </summary>
    [Fact]
    public async Task TotpService_ConfirmEnrollmentAsync_ValidCode_ActivatesAndIssuesTenRecoveryCodes()
    {
        // Arrange: a user and a started enrollment
        var world = new TotpTestWorld();
        var user = await world.AddUserAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: enroll completely
        var (_, codes) = await world.EnrollAsync("ann");
        var status = await world.Totp.GetStatusAsync(TestActors.Ann, cancellationToken);
        var step = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, cancellationToken);

        // Assert: ten distinct codes of the shown form, stored only as hashes, one audit entry, and the password
        // alone no longer signs in
        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, code => Assert.Matches(RecoveryShape(), code));
        Assert.Equal(10, world.Store.CodeHashes(user.Id).Count);
        Assert.DoesNotContain(world.Store.CodeHashes(user.Id), hash => codes.Any(code => hash.Contains(code.Replace("-", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)));
        Assert.True(status.Enrolled);
        Assert.Equal(10, status.RecoveryCodesLeft);
        Assert.Equal(TotpService.EnrolledAction, Assert.Single(world.Store.Audits).Action);
        Assert.True(step.SecondFactorRequired);
    }

    /// <summary>
    ///     Test that a wrong code leaves the enrollment pending, counts toward the lockout and says nothing about why.
    /// </summary>
    [Fact]
    public async Task TotpService_ConfirmEnrollmentAsync_WrongCode_ThrowsAndStaysPending()
    {
        // Arrange: a started enrollment
        var world = new TotpTestWorld();
        var user = await world.AddUserAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var enrollment = await world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, cancellationToken);
        var right = world.CodeNow(TotpOracle.FromBase32(enrollment.SecretBase32));
        var wrong = right == "000000" ? "000001" : "000000";

        // Act: confirm with a wrong, then with a malformed code
        var first = await Assert.ThrowsAsync<DomainRuleException>(() => world.Totp.ConfirmEnrollmentAsync(TestActors.Ann, wrong, cancellationToken));
        var second = await Assert.ThrowsAsync<DomainRuleException>(() => world.Totp.ConfirmEnrollmentAsync(TestActors.Ann, "12ab", cancellationToken));

        // Assert: refused alike, still pending, two failures counted, and the right code still works
        Assert.Equal(DomainRule.TotpCodeIncorrect, first.Rule);
        Assert.Equal(first.Message, second.Message);
        Assert.False((await world.Store.GetAsync(user.Id, cancellationToken))!.IsActive);
        Assert.Equal(2, (await world.UserService.GetAsync("ann", cancellationToken))!.FailedLoginCount);
        Assert.Equal(10, (await world.Totp.ConfirmEnrollmentAsync(TestActors.Ann, right, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that a started enrollment ends after fifteen minutes.
    /// </summary>
    [Fact]
    public async Task TotpService_ConfirmEnrollmentAsync_AfterFifteenMinutes_ThrowsTotpEnrollmentExpired()
    {
        // Arrange: a started enrollment that waited too long
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var enrollment = await world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, cancellationToken);
        world.Clock.Advance(TimeSpan.FromMinutes(16));

        // Act: confirm with a code that is right now
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => world.Totp.ConfirmEnrollmentAsync(
            TestActors.Ann, world.CodeNow(TotpOracle.FromBase32(enrollment.SecretBase32)), cancellationToken));

        // Assert: expired
        Assert.Equal(DomainRule.TotpEnrollmentExpired, exception.Rule);
    }

    /// <summary>
    ///     Test that confirming without a started enrollment is refused.
    /// </summary>
    [Fact]
    public async Task TotpService_ConfirmEnrollmentAsync_NothingStarted_ThrowsTotpNotEnrolled()
    {
        // Arrange: a user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");

        // Act: confirm
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ConfirmEnrollmentAsync(TestActors.Ann, "123456", TestContext.Current.CancellationToken));

        // Assert: refused
        Assert.Equal(DomainRule.TotpNotEnrolled, exception.Rule);
    }

    /// <summary>
    ///     Test that cancelling removes a pending enrollment and leaves a confirmed one alone.
    /// </summary>
    [Fact]
    public async Task TotpService_CancelEnrollmentAsync_PendingOnly_RemovesPendingKeepsConfirmed()
    {
        // Arrange: ann has a pending enrollment, bob a confirmed one
        var world = new TotpTestWorld();
        var ann = await world.AddUserAsync("ann");
        var bob = await world.AddUserAsync("bob");
        var cancellationToken = TestContext.Current.CancellationToken;
        await world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, cancellationToken);
        await world.EnrollAsync("bob");

        // Act: both cancel
        await world.Totp.CancelEnrollmentAsync(TestActors.Ann, cancellationToken);
        await world.Totp.CancelEnrollmentAsync(TestActors.Bob, cancellationToken);

        // Assert: ann's row is gone, bob's is active
        Assert.Null(await world.Store.GetAsync(ann.Id, cancellationToken));
        Assert.True((await world.Store.GetAsync(bob.Id, cancellationToken))!.IsActive);
    }

    /// <summary>
    ///     Test that the code that confirmed the enrollment can not be used to sign in, but the next one can.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_CodeOfTheEnrollment_IsAReplay()
    {
        // Arrange: an enrolled user; the confirming code was for the current step
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: sign in with the same code, then with the next step's code
        var replay = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", world.CodeNow(secret), cancellationToken);
        var next = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);

        // Assert: the replay fails, the next code signs in
        Assert.Equal(TotpSignInOutcome.Failed, replay.Outcome);
        Assert.Equal(TotpSignInOutcome.Success, next.Outcome);
        Assert.Equal("ann", next.User!.Username);
    }

    /// <summary>
    ///     Test that the last sign-in of an enrolled user is stored when the second step succeeds, and not by the
    ///     password step or by a failed code.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_Success_RecordsLastSignInOnlyThen()
    {
        // Arrange: an enrolled user who never signed in
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = (await world.Users.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;

        // Act: the password step, a wrong code, then the code of the next step
        var token = await world.BeginSignInAsync("ann");
        var afterPassword = (await world.Users.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;
        var failed = await world.Totp.CompleteSignInAsync(token, "10.0.0.1", "000000", cancellationToken);
        var afterFailure = (await world.Users.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;
        var success = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);
        var afterSuccess = (await world.Users.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;

        // Assert: only the completed sign-in stored the time
        Assert.Null(before);
        Assert.Null(afterPassword);
        Assert.Equal(TotpSignInOutcome.Failed, failed.Outcome);
        Assert.Null(afterFailure);
        Assert.Equal(TotpSignInOutcome.Success, success.Outcome);
        Assert.Equal(world.Clock.GetUtcNow(), afterSuccess);
    }

    /// <summary>
    ///     Test the window of one step: a code one step away is accepted, two steps away is refused, and a code is
    ///     never accepted again once a later step was used.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_WindowAndOrder_AcceptsPlusMinusOneStepOnlyOnceForward()
    {
        // Arrange: an enrolled user whose clock moved five steps on
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        world.AdvanceSteps(5);
        var cancellationToken = TestContext.Current.CancellationToken;

        async Task<TotpSignInOutcome> TryAsync(int offset)
        {
            return (await world.Totp.CompleteSignInAsync(
                await world.BeginSignInAsync("ann"), "10.0.0.1", world.CodeNow(secret, offset), cancellationToken)).Outcome;
        }

        // Act: two steps ahead and behind, then one step behind, then the same again, then the current step
        var ahead2 = await TryAsync(2);
        var behind2 = await TryAsync(-2);
        var behind1 = await TryAsync(-1);
        var behind1Again = await TryAsync(-1);
        var current = await TryAsync(0);
        var olderNow = await TryAsync(-1);

        // Assert: outside the window refused; inside accepted once; later steps move forward, never back
        Assert.Equal(TotpSignInOutcome.Failed, ahead2);
        Assert.Equal(TotpSignInOutcome.Failed, behind2);
        Assert.Equal(TotpSignInOutcome.Success, behind1);
        Assert.Equal(TotpSignInOutcome.Failed, behind1Again);
        Assert.Equal(TotpSignInOutcome.Success, current);
        Assert.Equal(TotpSignInOutcome.Failed, olderNow);
    }

    /// <summary>
    ///     Test that a wrong, a reused and a malformed code give the same outcome, and that the outcome carries no user.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_WrongReusedMalformed_AreIndistinguishable()
    {
        // Arrange: a fresh world per attempt (five failures would lock one account), each with an enrolled user and
        // one accepted code
        var cancellationToken = TestContext.Current.CancellationToken;
        var results = new List<TotpSignInResult>();
        var firstOutcome = TotpSignInOutcome.Failed;

        // Act: reuse the accepted code, try a wrong one, a short one, text, a long one and nothing
        foreach (var kind in new[] { "good", "reused", "wrong", "short", "text", "long", "none" })
        {
            var world = new TotpTestWorld();
            await world.AddUserAsync("ann");
            var (secret, _) = await world.EnrollAsync("ann");
            var good = world.CodeNow(secret, 1);
            var first = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", good, cancellationToken);
            if (kind == "good")
            {
                firstOutcome = first.Outcome;
                continue;
            }

            var code = kind switch
            {
                "reused" => good,
                "wrong" => good == "000000" ? "000001" : "000000",
                "short" => "12345",
                "text" => "abcdef",
                "long" => "1234567",
                _ => null
            };
            results.Add(await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", code, cancellationToken));
        }

        // Assert: the first signed in; every other result is the same failure without a user
        Assert.Equal(TotpSignInOutcome.Success, firstOutcome);
        Assert.Equal(6, results.Count);
        Assert.All(results, r => Assert.Equal(new TotpSignInResult(TotpSignInOutcome.Failed, null, false, 0), r));
    }

    /// <summary>
    ///     Test that many requests with the same valid code let exactly one of them in (compare-and-set).
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_ConcurrentSameCode_ExactlyOneSucceeds()
    {
        // Arrange: an enrolled user and four pending sign-ins of the same user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var code = world.CodeNow(secret, 1);
        var tokens = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            tokens.Add(await world.BeginSignInAsync("ann"));
        }

        // Act: use the same code from all of them at once
        var results = await Task.WhenAll(tokens.Select(token => Task.Run(
            () => world.Totp.CompleteSignInAsync(token, "10.0.0.1", code, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken)));

        // Assert: one success, no other success, and the step moved forward once
        Assert.Equal(1, results.Count(r => r.Outcome == TotpSignInOutcome.Success));
        Assert.Equal(1, world.Store.AdvanceCount);
    }

    /// <summary>
    ///     Test that a recovery code signs in once, in any of its usual spellings, and records its use without the code.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_RecoveryCode_WorksOnceAndIsAudited()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (_, codes) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var spelled = codes[0].Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant();

        // Act: use it in a lower-case spelling with spaces, then again
        var first = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", spelled, cancellationToken);
        var second = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", codes[0], cancellationToken);

        // Assert: accepted once with nine left, then refused, with an audit entry that does not hold the code
        Assert.Equal(TotpSignInOutcome.Success, first.Outcome);
        Assert.True(first.UsedRecoveryCode);
        Assert.Equal(9, first.RecoveryCodesLeft);
        Assert.Equal(TotpSignInOutcome.Failed, second.Outcome);
        var audit = Assert.Single(world.Store.Audits, a => a.Action == TotpService.RecoveryUsedAction);
        Assert.DoesNotContain(codes[0].Replace("-", string.Empty, StringComparison.Ordinal), audit.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that many requests with the same recovery code let exactly one of them in.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_ConcurrentSameRecoveryCode_ExactlyOneSucceeds()
    {
        // Arrange: an enrolled user and pending sign-ins
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (_, codes) = await world.EnrollAsync("ann");
        var tokens = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            tokens.Add(await world.BeginSignInAsync("ann"));
        }

        // Act: use the same recovery code from all of them at once
        var results = await Task.WhenAll(tokens.Select(token => Task.Run(
            () => world.Totp.CompleteSignInAsync(token, "10.0.0.1", codes[3], TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken)));

        // Assert: exactly one success and nine codes left
        Assert.Equal(1, results.Count(r => r.Outcome == TotpSignInOutcome.Success));
        Assert.Equal(9, await world.Store.CountUnusedRecoveryCodesAsync(1, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the recovery codes of one user do not work for another, even if both were shown the same text.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_RecoveryCodeOfAnotherUser_IsRefused()
    {
        // Arrange: two enrolled users
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.AddUserAsync("bob");
        var (_, annCodes) = await world.EnrollAsync("ann");
        await world.EnrollAsync("bob");

        // Act: bob tries ann's code
        var result = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("bob"), "10.0.0.1", annCodes[0], TestContext.Current.CancellationToken);

        // Assert: refused
        Assert.Equal(TotpSignInOutcome.Failed, result.Outcome);
    }

    /// <summary>
    ///     Test that five wrong codes lock the account like five wrong passwords, and that a locked account refuses
    ///     even the right code until the lockout ends.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_FiveWrongCodes_LocksTheAccount()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrong = world.CodeNow(secret, 1) == "000000" ? "000001" : "000000";

        // Act: four wrong codes, a fifth, then the right code
        var outcomes = new List<TotpSignInOutcome>();
        for (var i = 0; i < 5; i++)
        {
            outcomes.Add((await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", wrong, cancellationToken)).Outcome);
        }

        var whileLocked = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);
        world.Clock.Advance(TimeSpan.FromMinutes(6));
        var afterLockout = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);

        // Assert: four failures, the fifth locks, the right code is refused while locked and works afterwards
        Assert.Equal([TotpSignInOutcome.Failed, TotpSignInOutcome.Failed, TotpSignInOutcome.Failed, TotpSignInOutcome.Failed, TotpSignInOutcome.LockedOut], outcomes);
        Assert.Equal(TotpSignInOutcome.LockedOut, whileLocked.Outcome);
        Assert.Equal(TotpSignInOutcome.Success, afterLockout.Outcome);
        Assert.Equal(5, world.AuditStore.Entries.Count(e => e.Action == TotpService.FailedAction));
        Assert.Contains(world.AuditStore.Entries, e => e.Action == "auth.locked-out");
    }

    /// <summary>
    ///     Test that wrong passwords and wrong codes share one counter.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_PasswordAndCodeFailures_ShareOneCounter()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrong = world.CodeNow(secret, 1) == "000000" ? "000001" : "000000";

        // Act: three wrong passwords and two wrong codes
        for (var i = 0; i < 3; i++)
        {
            await world.UserService.AuthenticatePasswordAsync("ann", "wrong password here", cancellationToken);
        }

        var first = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", wrong, cancellationToken);
        var second = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", wrong, cancellationToken);

        // Assert: the second wrong code is the fifth failure and locks
        Assert.Equal(TotpSignInOutcome.Failed, first.Outcome);
        Assert.Equal(TotpSignInOutcome.LockedOut, second.Outcome);
    }

    /// <summary>
    ///     Test that a right password does not reset the counter of an account with a second factor, so that guessing
    ///     codes with a known password still ends in the lockout; only a complete sign-in resets it.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_RightPasswordBetweenWrongCodes_DoesNotResetTheCounter()
    {
        // Arrange: an enrolled user who failed four times
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrong = world.CodeNow(secret, 1) == "000000" ? "000001" : "000000";
        for (var i = 0; i < 4; i++)
        {
            await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", wrong, cancellationToken);
        }

        // Act: the password is right again (which only begins the second step), then one more wrong code
        var step = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, cancellationToken);
        var fifth = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", wrong, cancellationToken);

        // Assert: the password step asked for the code, and the fifth failure still locked the account
        Assert.True(step.SecondFactorRequired);
        Assert.Equal(TotpSignInOutcome.LockedOut, fifth.Outcome);
    }

    /// <summary>
    ///     Test that a complete sign-in clears the counter.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_Success_ClearsTheCounter()
    {
        // Arrange: an enrolled user who failed four times
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrong = world.CodeNow(secret, 1) == "000000" ? "000001" : "000000";
        for (var i = 0; i < 4; i++)
        {
            await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", wrong, cancellationToken);
        }

        // Act: sign in completely, then fail four times again
        var success = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);
        var after = new List<TotpSignInOutcome>();
        for (var i = 0; i < 4; i++)
        {
            after.Add((await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", wrong, cancellationToken)).Outcome);
        }

        // Assert: signed in, and four more failures do not lock
        Assert.Equal(TotpSignInOutcome.Success, success.Outcome);
        Assert.All(after, outcome => Assert.Equal(TotpSignInOutcome.Failed, outcome));
    }

    /// <summary>
    ///     Test that a challenge can be used once, only from the client it was issued to, and only while it lives.
    /// </summary>
    /// <param name="case">The way the challenge is wrong.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("garbage")]
    [InlineData("foreign-client")]
    [InlineData("used")]
    [InlineData("expired")]
    [InlineData("user-disabled")]
    public async Task TotpService_CompleteSignInAsync_BadChallenge_IsChallengeInvalid(string @case)
    {
        // Arrange: an enrolled user and a challenge that is wrong in the given way
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var code = world.CodeNow(secret, 1);
        var token = await world.BeginSignInAsync("ann");
        var address = "10.0.0.1";
        switch (@case)
        {
            case "missing":
                token = null!;
                break;
            case "garbage":
                token = "not-a-token";
                break;
            case "foreign-client":
                address = "10.9.9.9";
                break;
            case "used":
                await world.Totp.CompleteSignInAsync(token, address, code, cancellationToken);
                break;
            case "expired":
                world.Clock.Advance(TotpChallengeStore.Lifetime + TimeSpan.FromSeconds(1));
                break;
            default:
                await world.UserService.SetEnabledAsync("ann", false, Actor.CommandLine, cancellationToken);
                break;
        }

        // Act: complete with a right code
        var result = await world.Totp.CompleteSignInAsync(token, address, code, cancellationToken);

        // Assert: not signed in, and no user is carried
        Assert.NotEqual(TotpSignInOutcome.Success, result.Outcome);
        Assert.Null(result.User);
    }

    /// <summary>
    ///     Test that a challenge issued to one client is not spent by another client's attempt.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_ForeignClient_DoesNotSpendTheChallenge()
    {
        // Arrange: an enrolled user and a challenge of one client
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await world.BeginSignInAsync("ann", "10.0.0.1");

        // Act: another client tries first, then the owner
        var foreign = await world.Totp.CompleteSignInAsync(token, "10.9.9.9", world.CodeNow(secret, 1), cancellationToken);
        var owner = await world.Totp.CompleteSignInAsync(token, "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);

        // Assert: the foreign attempt is refused and the owner still signs in
        Assert.Equal(TotpSignInOutcome.ChallengeInvalid, foreign.Outcome);
        Assert.Equal(TotpSignInOutcome.Success, owner.Outcome);
    }

    /// <summary>
    ///     Test that a switched-off feature makes an existing enrollment inert: the password alone signs in again and
    ///     no challenge can be completed.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_FeatureDisabled_IsChallengeInvalidAndPasswordAloneSignsIn()
    {
        // Arrange: a row exists, but the feature is off
        var world = new TotpTestWorld(new TotpSettings(false, "ReviewCenter"));
        var user = await world.AddUserAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        await world.Store.SavePendingAsync(
            new UserTotp { UserId = user.Id, ProtectedSecret = world.Protector.Protect(user.Id, TotpOracle.VectorSecret), Created = world.Clock.GetUtcNow() },
            cancellationToken);
        await world.Store.ConfirmAsync(user.Id, 0, world.Clock.GetUtcNow(), [], AuditDraft.By(TestActors.Ann, "x", "x"), cancellationToken);

        // Act: begin a sign-in, complete it, and check the password
        var token = await world.BeginSignInAsync("ann");
        var result = await world.Totp.CompleteSignInAsync(token, "10.0.0.1", TotpOracle.CodeAt(TotpOracle.VectorSecret, world.Clock.GetUtcNow()), cancellationToken);
        var step = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, cancellationToken);

        // Assert: refused, and the password alone is enough while the feature is off
        Assert.Equal(TotpSignInOutcome.ChallengeInvalid, result.Outcome);
        Assert.NotNull(step.User);
        Assert.False(step.SecondFactorRequired);
        Assert.False(await world.Totp.IsEnrolledAsync("ann", cancellationToken));
    }

    /// <summary>
    ///     Test that an unreadable secret refuses the code like a wrong one, while recovery codes still work.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_UnreadableSecret_RefusesCodesButRecoveryStillWorks()
    {
        // Arrange: an enrolled user whose key ring was lost
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, codes) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        world.Protector.Unreadable = true;

        // Act: a right code, then a recovery code
        var byCode = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);
        var byRecovery = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", codes[0], cancellationToken);

        // Assert: the code fails, the recovery code signs in
        Assert.Equal(TotpSignInOutcome.Failed, byCode.Outcome);
        Assert.Equal(TotpSignInOutcome.Success, byRecovery.Outcome);
    }

    /// <summary>
    ///     Test that removal needs the password and a code, and that it ends pending sign-ins.
    /// </summary>
    [Fact]
    public async Task TotpService_DisableAsync_PasswordAndCode_RemovesAndInvalidatesPendingSignIns()
    {
        // Arrange: an enrolled user with a pending sign-in
        var world = new TotpTestWorld();
        var user = await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var pending = await world.BeginSignInAsync("ann");

        // Act: remove with a wrong password, a wrong code, and then the right ones
        var badPassword = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.DisableAsync(TestActors.Ann, "wrong password here", world.CodeNow(secret, 1), cancellationToken));
        var badCode = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.DisableAsync(TestActors.Ann, TotpTestWorld.Password, "000000", cancellationToken));
        await world.Totp.DisableAsync(TestActors.Ann, TotpTestWorld.Password, world.CodeNow(secret, 1), cancellationToken);
        var late = await world.Totp.CompleteSignInAsync(pending, "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);
        var step = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, cancellationToken);

        // Assert: refused twice, then removed with an audit entry; the pending sign-in ended and the password alone signs in
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, badPassword.Rule);
        Assert.Equal(DomainRule.TotpCodeIncorrect, badCode.Rule);
        Assert.Null(await world.Store.GetAsync(user.Id, cancellationToken));
        Assert.Contains(world.Store.Audits, a => a.Action == TotpService.DisabledAction);
        Assert.NotEqual(TotpSignInOutcome.Success, late.Outcome);
        Assert.NotNull(step.User);
        Assert.False(step.SecondFactorRequired);
    }

    /// <summary>
    ///     Test that a recovery code can remove the second factor (the lost device case).
    /// </summary>
    [Fact]
    public async Task TotpService_DisableAsync_RecoveryCode_Removes()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        var user = await world.AddUserAsync("ann");
        var (_, codes) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: remove with a recovery code
        await world.Totp.DisableAsync(TestActors.Ann, TotpTestWorld.Password, codes[2], cancellationToken);

        // Assert: removed, and the use of the code was recorded
        Assert.Null(await world.Store.GetAsync(user.Id, cancellationToken));
        Assert.Contains(world.Store.Audits, a => a.Action == TotpService.RecoveryUsedAction);
    }

    /// <summary>
    ///     Test that removal without a second factor is refused.
    /// </summary>
    [Fact]
    public async Task TotpService_DisableAsync_NotEnrolled_ThrowsTotpNotEnrolled()
    {
        // Arrange: a user without a second factor
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");

        // Act: remove
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.DisableAsync(TestActors.Ann, TotpTestWorld.Password, "123456", TestContext.Current.CancellationToken));

        // Assert: refused
        Assert.Equal(DomainRule.TotpNotEnrolled, exception.Rule);
    }

    /// <summary>
    ///     Test that regenerating needs a code (not a recovery code), replaces all codes and records it.
    /// </summary>
    [Fact]
    public async Task TotpService_RegenerateRecoveryCodesAsync_PasswordAndCode_ReplacesTheCodes()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, oldCodes) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: try with a recovery code, then with a code
        var byRecovery = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.RegenerateRecoveryCodesAsync(TestActors.Ann, TotpTestWorld.Password, oldCodes[0], cancellationToken));
        var newCodes = await world.Totp.RegenerateRecoveryCodesAsync(TestActors.Ann, TotpTestWorld.Password, world.CodeNow(secret, 1), cancellationToken);
        var old = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", oldCodes[0], cancellationToken);
        var fresh = await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", newCodes[0], cancellationToken);

        // Assert: the recovery code was refused here, ten new codes were issued, the old stopped and the new works
        Assert.Equal(DomainRule.TotpCodeIncorrect, byRecovery.Rule);
        Assert.Equal(10, newCodes.Count);
        Assert.Empty(newCodes.Intersect(oldCodes));
        Assert.Equal(TotpSignInOutcome.Failed, old.Outcome);
        Assert.Equal(TotpSignInOutcome.Success, fresh.Outcome);
        Assert.Contains(world.Store.Audits, a => a.Action == TotpService.RecoveryRegeneratedAction);
    }

    /// <summary>
    ///     Test that only a system administrator can reset, that the reset is audited with the target, and that it
    ///     ends the target's pending sign-ins.
    /// </summary>
    [Fact]
    public async Task TotpService_ResetAsync_SystemAdministrator_RemovesAndInvalidatesPendingSignIns()
    {
        // Arrange: an administrator and an enrolled user with a pending sign-in
        var world = new TotpTestWorld();
        await world.AddUserAsync("root", UserRole.Admin);
        var ann = await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var pending = await world.BeginSignInAsync("ann");

        // Act: the administrator resets it
        await world.Totp.ResetAsync(new Actor("root", "root"), "ANN", cancellationToken);
        var late = await world.Totp.CompleteSignInAsync(pending, "10.0.0.1", world.CodeNow(secret, 1), cancellationToken);

        // Assert: removed, recorded with the administrator, and the pending sign-in ended
        Assert.Null(await world.Store.GetAsync(ann.Id, cancellationToken));
        var audit = Assert.Single(world.Store.Audits, a => a.Action == TotpService.ResetAction);
        Assert.Equal("root", audit.ActorUsername);
        Assert.Contains("ann", audit.Detail, StringComparison.Ordinal);
        Assert.NotEqual(TotpSignInOutcome.Success, late.Outcome);
    }

    /// <summary>
    ///     Test that nobody but an enabled system administrator can reset.
    /// </summary>
    /// <param name="who">The kind of caller.</param>
    [Theory]
    [InlineData("ordinary-user")]
    [InlineData("project-admin")]
    [InlineData("target-itself")]
    [InlineData("api-token-admin")]
    [InlineData("disabled-admin")]
    [InlineData("unknown")]
    public async Task TotpService_ResetAsync_NotASystemAdministrator_ThrowsActorNotAllowed(string who)
    {
        // Arrange: an enrolled user and a caller of the kind (a project administrator is just an ordinary user here)
        var world = new TotpTestWorld();
        await world.AddUserAsync("root", UserRole.Admin);
        await world.AddUserAsync("ann");
        await world.AddUserAsync("pat");
        await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var caller = who switch
        {
            "api-token-admin" => new Actor("root", "root") { ApiTokenId = "K7M2QX9P" },
            "target-itself" => new Actor("ann", "ann"),
            "unknown" => new Actor("nobody", "nobody"),
            "disabled-admin" => new Actor("root", "root"),
            _ => new Actor("pat", "pat")
        };
        if (who == "disabled-admin")
        {
            await world.AddUserAsync("root2", UserRole.Admin);
            await world.UserService.SetEnabledAsync("root", false, Actor.CommandLine, cancellationToken);
        }

        // Act: try to reset ann's second factor
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => world.Totp.ResetAsync(caller, "ann", cancellationToken));

        // Assert: refused, and the second factor is still there
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
        Assert.True(await world.Totp.IsEnrolledAsync("ann", cancellationToken));
    }

    /// <summary>
    ///     Test the reset of a user that does not exist and of one without a second factor.
    /// </summary>
    [Fact]
    public async Task TotpService_ResetAsync_UnknownOrNotEnrolled_Throws()
    {
        // Arrange: an administrator and a user without a second factor
        var world = new TotpTestWorld();
        await world.AddUserAsync("root", UserRole.Admin);
        await world.AddUserAsync("ann");
        var admin = new Actor("root", "root");

        // Act: reset an unknown user and a user without one
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => world.Totp.ResetAsync(admin, "nobody", TestContext.Current.CancellationToken));
        var none = await Assert.ThrowsAsync<DomainRuleException>(() => world.Totp.ResetAsync(admin, "ann", TestContext.Current.CancellationToken));

        // Assert: the rules are named
        Assert.Equal(DomainRule.UserNotFound, unknown.Rule);
        Assert.Equal(DomainRule.TotpNotEnrolled, none.Rule);
    }

    /// <summary>
    ///     Test that the list of enrolled users holds exactly the users with a confirmed second factor.
    /// </summary>
    [Fact]
    public async Task TotpService_ListEnrolledUserIdsAsync_HoldsConfirmedEnrollmentsOnly()
    {
        // Arrange: ann enrolled, bob pending, cy nothing
        var world = new TotpTestWorld();
        var ann = await world.AddUserAsync("ann");
        await world.AddUserAsync("bob");
        await world.AddUserAsync("cyd");
        await world.EnrollAsync("ann");
        await world.Totp.BeginEnrollmentAsync(TestActors.Bob, TotpTestWorld.Password, TestContext.Current.CancellationToken);

        // Act: list
        var ids = await world.Totp.ListEnrolledUserIdsAsync(TestContext.Current.CancellationToken);

        // Assert: only ann
        Assert.Equal([ann.Id], ids);
    }

    /// <summary>
    ///     Test that neither a secret, a code nor a recovery code reaches the audit trail or an error message, through
    ///     the whole life of a second factor.
    /// </summary>
    [Fact]
    public async Task TotpService_WholeLifeCycle_NeverPutsSecretsOrCodesInAuditOrErrors()
    {
        // Arrange: a user who enrolls, fails, signs in with a code and a recovery code, regenerates and is removed
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var enrollment = await world.Totp.BeginEnrollmentAsync(TestActors.Ann, TotpTestWorld.Password, cancellationToken);
        var secret = TotpOracle.FromBase32(enrollment.SecretBase32);
        var secrets = new List<string> { enrollment.SecretBase32 };
        var messages = new List<string>();
        var codes = await world.Totp.ConfirmEnrollmentAsync(TestActors.Ann, world.CodeNow(secret), cancellationToken);
        secrets.AddRange(codes);
        secrets.AddRange(codes.Select(code => code.Replace("-", string.Empty, StringComparison.Ordinal)));

        // Act: fail a code on the account page and at sign-in, use a code and a recovery code, then regenerate and remove
        var typed = new[] { "135790", "246801" };
        secrets.AddRange(typed);
        messages.Add((await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.DisableAsync(TestActors.Ann, TotpTestWorld.Password, typed[0], cancellationToken))).Message);
        await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", typed[1], cancellationToken);
        var good = world.CodeNow(secret, 1);
        secrets.Add(good);
        await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", good, cancellationToken);
        await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", codes[0], cancellationToken);
        world.AdvanceSteps(2);
        var newCodes = await world.Totp.RegenerateRecoveryCodesAsync(TestActors.Ann, TotpTestWorld.Password, world.CodeNow(secret), cancellationToken);
        secrets.AddRange(newCodes);
        messages.Add((await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.DisableAsync(TestActors.Ann, TotpTestWorld.Password, "000000", cancellationToken))).Message);
        await world.Totp.DisableAsync(TestActors.Ann, TotpTestWorld.Password, newCodes[1], cancellationToken);

        // Assert: nothing secret appears in any audit text or message (the 6 digit codes only count when they are
        // not part of a longer number, which the audit texts do not contain)
        var text = world.AllAuditText + "\n" + string.Join('\n', messages);
        Assert.All(secrets, secretText => Assert.DoesNotContain(secretText, text, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(TotpService.FailedAction, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test the reading of the status for a user the feature does not apply to.
    /// </summary>
    [Fact]
    public async Task TotpService_GetStatusAsync_ApiTokenActor_IsNotAvailable()
    {
        // Arrange: a user and an actor that arrives through a token
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");

        // Act: read the status
        var status = await world.Totp.GetStatusAsync(
            new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" }, TestContext.Current.CancellationToken);

        // Assert: the feature is on but not available to the token
        Assert.True(status.FeatureEnabled);
        Assert.False(status.Available);
    }

    /// <summary>
    ///     The new password the password-change tests choose.
    /// </summary>
    private const string NewPassword = "another long passphrase";

    /// <summary>
    ///     Gets a code that is wrong at every step the window accepts.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="secret">The secret of the enrolled user.</param>
    /// <returns>A six digit code that is not valid now.</returns>
    private static string WrongCode(TotpTestWorld world, byte[] secret)
    {
        var valid = new[] { world.CodeNow(secret, -1), world.CodeNow(secret), world.CodeNow(secret, 1) };
        return WrongCodeCandidates.First(code => !valid.Contains(code));
    }

    /// <summary>
    ///     The codes tried, in order, when looking for one that is not valid now.
    /// </summary>
    private static readonly string[] WrongCodeCandidates = ["000000", "000001"];

    /// <summary>
    ///     Reads the failure counter of a user.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The counter.</returns>
    private static async Task<int> FailuresAsync(TotpTestWorld world, string username)
    {
        return (await world.Users.GetByUsernameAsync(username, TestContext.Current.CancellationToken))!.FailedLoginCount;
    }

    /// <summary>
    ///     Reads the stored password hash of a user.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The hash.</returns>
    private static async Task<string> HashAsync(TotpTestWorld world, string username)
    {
        return (await world.Users.GetByUsernameAsync(username, TestContext.Current.CancellationToken))!.PasswordHash;
    }

    /// <summary>
    ///     Test that an enrolled user cannot change the password with the password alone, or with a missing code: the
    ///     change is refused, the hash stays, and the refusal counts as a failure.
    /// </summary>
    /// <param name="code">The missing code.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TotpService_ChangeOwnPasswordAsync_NoCode_IsRefusedCountsAndKeepsTheHash(string? code)
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");
        var before = await HashAsync(world, "ann");

        // Act: change with the right password and no code
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, code, TestContext.Current.CancellationToken));

        // Assert: refused with the general message, nothing changed but the counter, and a failure is on record
        Assert.Equal(DomainRule.TotpCodeIncorrect, exception.Rule);
        Assert.Equal(before, await HashAsync(world, "ann"));
        Assert.Equal(1, await FailuresAsync(world, "ann"));
        Assert.Contains(world.AuditStore.Entries, e => e.Action == TotpService.FailedAction && e.Detail.Contains("password change", StringComparison.Ordinal));
        Assert.DoesNotContain(world.Users.Audits, a => a.Action == "user.password-changed");
    }

    /// <summary>
    ///     Test that a wrong code is refused and counted, and a wrong password is refused before the code is looked at.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_WrongCodeOrWrongPassword_IsRefusedAndKeepsTheHash()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await HashAsync(world, "ann");

        // Act: a wrong code, then a wrong password with a valid code
        var wrongCode = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, WrongCode(world, secret), cancellationToken));
        var wrongPassword = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, "not the password", NewPassword, world.CodeNow(secret, 1), cancellationToken));

        // Assert: each is refused for its own reason, both counted, the valid code was not used up, and nothing changed
        Assert.Equal(DomainRule.TotpCodeIncorrect, wrongCode.Rule);
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, wrongPassword.Rule);
        Assert.Equal(2, await FailuresAsync(world, "ann"));
        Assert.Equal(0, world.Store.AdvanceCount);
        Assert.Equal(before, await HashAsync(world, "ann"));
    }

    /// <summary>
    ///     Test that the code that confirmed the enrollment (an already accepted step) cannot change the password.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_ReusedCode_IsRefused()
    {
        // Arrange: an enrolled user; the confirming code is for the current step
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var before = await HashAsync(world, "ann");

        // Act: change with the already accepted code
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, world.CodeNow(secret), TestContext.Current.CancellationToken));

        // Assert: refused as a replay
        Assert.Equal(DomainRule.TotpCodeIncorrect, exception.Rule);
        Assert.Equal(before, await HashAsync(world, "ann"));
    }

    /// <summary>
    ///     Test that the right password and a fresh code change the password, clear the counter (only now), use the
    ///     code up and record the change.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_PasswordAndValidCode_ChangesAndClearsTheCounter()
    {
        // Arrange: an enrolled user with three failures on record
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var i = 0; i < 3; i++)
        {
            await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", cancellationToken);
        }

        var code = world.CodeNow(secret, 1);

        // Act: change with the password and the fresh code, then try the same code once more
        await world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, code, cancellationToken);
        var replay = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, NewPassword, "yet another passphrase", code, cancellationToken));

        // Assert: the new password works, the counter was cleared by the first change (the replay counts one), the step moved once
        Assert.NotNull(await world.UserService.ConfirmPasswordAsync("ann", NewPassword, cancellationToken));
        Assert.Equal(DomainRule.TotpCodeIncorrect, replay.Rule);
        Assert.Equal(1, await FailuresAsync(world, "ann"));
        Assert.Equal(1, world.Store.AdvanceCount);
        Assert.Contains(world.Users.Audits, a => a.Action == "user.password-changed");
    }

    /// <summary>
    ///     Test that a recovery code can change the password once and its use is audited without the code.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_RecoveryCode_WorksOnceAndIsAudited()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (_, codes) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: change with a recovery code, then try to use it again
        await world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, codes[0], cancellationToken);
        var again = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, NewPassword, "yet another passphrase", codes[0], cancellationToken));

        // Assert: changed once, refused the second time, nine codes left, audited without the code
        Assert.NotNull(await world.UserService.ConfirmPasswordAsync("ann", NewPassword, cancellationToken));
        Assert.Equal(DomainRule.TotpCodeIncorrect, again.Rule);
        Assert.Equal(9, await world.Store.CountUnusedRecoveryCodesAsync((await world.Users.GetByUsernameAsync("ann", cancellationToken))!.Id, cancellationToken));
        var audit = Assert.Single(world.Store.Audits, a => a.Action == TotpService.RecoveryUsedAction);
        Assert.Contains("change the password", audit.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(codes[0].Replace("-", string.Empty, StringComparison.Ordinal), world.AllAuditText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that wrong codes at a password change share the lockout counter: five lock the account, and then even
    ///     the right code is refused until the lockout ends.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_FiveWrongCodes_LockTheAccount()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrong = WrongCode(world, secret);

        // Act: five wrong codes, then the right code while locked, then after the lockout
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, wrong, cancellationToken));
        }

        var whileLocked = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, world.CodeNow(secret, 1), cancellationToken));
        world.Clock.Advance(TimeSpan.FromMinutes(6));
        await world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, world.CodeNow(secret, 1), cancellationToken);

        // Assert: locked on the fifth failure, refused while locked, and changed afterwards
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, whileLocked.Rule);
        Assert.Contains(world.AuditStore.Entries, e => e.Action == "auth.locked-out");
        Assert.NotNull(await world.UserService.ConfirmPasswordAsync("ann", NewPassword, cancellationToken));
    }

    /// <summary>
    ///     Test that a right password does not reset the shared counter: password-only attempts between wrong codes
    ///     still end in the lockout.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_PasswordOnlyBetweenWrongCodes_DoesNotResetTheCounter()
    {
        // Arrange: an enrolled user who failed four times with wrong codes at sign-in
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrong = WrongCode(world, secret);
        for (var i = 0; i < 4; i++)
        {
            await world.Totp.CompleteSignInAsync(await world.BeginSignInAsync("ann"), "10.0.0.1", wrong, cancellationToken);
        }

        // Act: a password-only change (the password is right) through the Core service
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, null, cancellationToken));
        var afterLockout = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.UserService.ChangeOwnPasswordAsync("ann", TotpTestWorld.Password, NewPassword, cancellationToken));

        // Assert: the attempt was the fifth failure and locked the account, so the password is not changed
        Assert.Equal(DomainRule.TotpCodeIncorrect, exception.Rule);
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, afterLockout.Rule);
        Assert.Contains(world.AuditStore.Entries, e => e.Action == "auth.locked-out");
        Assert.Equal("hash:" + TotpTestWorld.Password, await HashAsync(world, "ann"));
    }

    /// <summary>
    ///     Test that an unreadable secret refuses codes at a password change while a recovery code still works.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_UnreadableSecret_RefusesCodesButRecoveryStillWorks()
    {
        // Arrange: an enrolled user whose secret can no longer be read
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, codes) = await world.EnrollAsync("ann");
        var cancellationToken = TestContext.Current.CancellationToken;
        world.Protector.Unreadable = true;

        // Act: a six digit code, then a recovery code
        var code = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, world.CodeNow(secret, 1), cancellationToken));
        await world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, codes[0], cancellationToken);

        // Assert: the code is refused like a wrong one, the recovery code changes the password
        Assert.Equal(DomainRule.TotpCodeIncorrect, code.Rule);
        Assert.NotNull(await world.UserService.ConfirmPasswordAsync("ann", NewPassword, cancellationToken));
    }

    /// <summary>
    ///     Test that a token actor can never change a password through this service.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_ApiTokenActor_ThrowsActorNotAllowed()
    {
        // Arrange: a user and an actor that arrives through a token
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var before = await HashAsync(world, "ann");

        // Act: change the password
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Totp.ChangeOwnPasswordAsync(
                new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" },
                TotpTestWorld.Password,
                NewPassword,
                null,
                TestContext.Current.CancellationToken));

        // Assert: refused and unchanged
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
        Assert.Equal(before, await HashAsync(world, "ann"));
    }

    /// <summary>
    ///     Test that a user without a second factor, and an enrolled user while the feature is switched off, change
    ///     the password with the password alone.
    /// </summary>
    /// <param name="case">Which of the two.</param>
    [Theory]
    [InlineData("not-enrolled")]
    [InlineData("feature-off")]
    public async Task TotpService_ChangeOwnPasswordAsync_NotEnrolledOrFeatureOff_PasswordAloneWorks(string @case)
    {
        // Arrange: ann is enrolled (a world with the feature off cannot enroll); services with the feature off share the stores
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var totp = world.Totp;
        if (@case == "feature-off")
        {
            await world.EnrollAsync("ann");
            var off = new TotpSettings(false, TotpSettings.DefaultIssuer);
            var users = new UserService(world.Users, new FakePasswordHasher(), world.Clock, world.Audit, world.Store, off);
            totp = new TotpService(world.Store, new TotpAlgorithm(), world.Protector, users, world.Challenges, world.Clock, off);
        }

        // Act: change with the password alone
        await totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, null, TestContext.Current.CancellationToken);

        // Assert: changed
        Assert.NotNull(await world.UserService.ConfirmPasswordAsync("ann", NewPassword, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a user who has to replace a temporary password (and so can have no second factor) is not asked for a code.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_TemporaryPassword_NeedsNoCode()
    {
        // Arrange: a user with a temporary password
        var world = new TotpTestWorld();
        var cancellationToken = TestContext.Current.CancellationToken;
        await world.AddUserAsync("tim");
        var tim = (await world.Users.GetByUsernameAsync("tim", cancellationToken))!;
        tim.MustChangePassword = true;
        await world.Users.UpdateAsync(tim, null, cancellationToken);

        // Act: replace it without a code
        await world.Totp.ChangeOwnPasswordAsync(new Actor("tim", "Tim"), TotpTestWorld.Password, NewPassword, null, cancellationToken);

        // Assert: changed and no longer temporary
        Assert.False((await world.Users.GetByUsernameAsync("tim", cancellationToken))!.MustChangePassword);
    }

    /// <summary>
    ///     Test that many requests with the same valid code and password change the password exactly once.
    /// </summary>
    [Fact]
    public async Task TotpService_ChangeOwnPasswordAsync_ConcurrentSameCode_ExactlyOneSucceeds()
    {
        // Arrange: an enrolled user
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var code = world.CodeNow(secret, 1);

        // Act: four requests with the same password, new password and code at once
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
            async () =>
            {
                try
                {
                    await world.Totp.ChangeOwnPasswordAsync(TestActors.Ann, TotpTestWorld.Password, NewPassword, code, TestContext.Current.CancellationToken);
                    return true;
                }
                catch (DomainRuleException)
                {
                    return false;
                }
            },
            TestContext.Current.CancellationToken)));

        // Assert: one succeeded, the code was used once
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, world.Store.AdvanceCount);
    }

    /// <summary>
    ///     Test that a challenge whose user is invalidated while its code is being checked is not given back.
    /// </summary>
    [Fact]
    public async Task TotpService_CompleteSignInAsync_UserInvalidatedWhileCodeIsChecked_DoesNotResurrectTheChallenge()
    {
        // Arrange: an enrolled user with a pending sign-in; the second factor is reset while the code is checked
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        var (secret, _) = await world.EnrollAsync("ann");
        var token = await world.BeginSignInAsync("ann");
        world.Store.OnNextGet = () => world.Challenges.InvalidateUser("ann");

        // Act: a wrong code
        var result = await world.Totp.CompleteSignInAsync(token, "10.0.0.1", WrongCode(world, secret), TestContext.Current.CancellationToken);

        // Assert: the attempt failed and the challenge is gone, so it can not be used again
        Assert.Equal(TotpSignInOutcome.Failed, result.Outcome);
        Assert.Null(world.Totp.PeekChallenge(token, "10.0.0.1"));
        var retry = await world.Totp.CompleteSignInAsync(token, "10.0.0.1", world.CodeNow(secret, 1), TestContext.Current.CancellationToken);
        Assert.Equal(TotpSignInOutcome.ChallengeInvalid, retry.Outcome);
    }

    /// <summary>
    ///     Test that the constructor and the public methods refuse null arguments.
    /// </summary>
    [Fact]
    public async Task TotpService_NullArguments_Throw()
    {
        // Arrange: a world
        var world = new TotpTestWorld();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act and assert: null where an object is required
        Assert.Throws<ArgumentNullException>(() => new TotpService(null!, new TotpAlgorithm(), world.Protector, world.UserService, world.Challenges, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new TotpService(world.Store, null!, world.Protector, world.UserService, world.Challenges, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new TotpService(world.Store, new TotpAlgorithm(), null!, world.UserService, world.Challenges, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new TotpService(world.Store, new TotpAlgorithm(), world.Protector, null!, world.Challenges, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new TotpService(world.Store, new TotpAlgorithm(), world.Protector, world.UserService, null!, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new TotpService(world.Store, new TotpAlgorithm(), world.Protector, world.UserService, world.Challenges, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => world.Totp.GetStatusAsync(null!, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => world.Totp.ResetAsync(null!, "ann", cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => world.Totp.IsEnrolledAsync(null!, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => world.Totp.ChangeOwnPasswordAsync(null!, "a", "b", null, cancellationToken));
    }
}
