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
///     Tests of <see cref="SigningService"/> for accounts with a second factor: a local user signs with the password
///     and a fresh authenticator code; accounts of an identity provider and API tokens are not affected.
/// </summary>
public class SigningServiceTotpTests
{
    /// <summary>
    ///     The review the tests sign.
    /// </summary>
    private const string ReviewId = "rev-1";

    /// <summary>
    ///     The actor who sets the review up.
    /// </summary>
    private static readonly Actor Boss = new("boss", "Boss");

    /// <summary>
    ///     Everything a test needs.
    /// </summary>
    /// <param name="Totp">The world of the second factor, with ann enrolled.</param>
    /// <param name="Signing">The signing service under test.</param>
    /// <param name="Proofs">The proofs of the external user.</param>
    /// <param name="Secret">The secret of ann's authenticator.</param>
    /// <param name="ProjectId">The project.</param>
    /// <param name="AnnSlotId">The slot of ann.</param>
    /// <param name="OliveSlotId">The slot of olive, an external user.</param>
    /// <param name="OliveId">The identity of olive.</param>
    /// <param name="RecoveryCodes">The recovery codes of ann.</param>
    private sealed record World(
        TotpTestWorld Totp,
        SigningService Signing,
        ReauthProofStore Proofs,
        byte[] Secret,
        int ProjectId,
        int AnnSlotId,
        int OliveSlotId,
        int OliveId,
        IReadOnlyList<string> RecoveryCodes);

    /// <summary>
    ///     Creates a review that can be signed, with a slot for ann (enrolled in the second factor) and a slot for an
    ///     external user, and moves the clock to the next time step so ann's enrollment code is no longer fresh.
    /// </summary>
    /// <param name="signingTotp">Whether the signing service knows the second factor service.</param>
    /// <returns>The prepared world.</returns>
    private static async Task<World> CreateAsync(bool signingTotp = true)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var totp = new TotpTestWorld();
        var store = new FakeReviewStore();
        var clock = totp.Clock;
        var boss = await totp.UserService.CreateAsync("boss", "Boss", UserRole.Reviewer, TotpTestWorld.Password, cancellationToken);
        var ann = await totp.UserService.CreateAsync("ann", "Ann", UserRole.Reviewer, TotpTestWorld.Password, cancellationToken);
        var external = await totp.UserService.SignInExternalAsync("https://idp.example/", "sub-1", "olive", "Olive", cancellationToken);
        var olive = external.User!;
        var (secret, recovery) = await totp.EnrollAsync("ann");
        var project = await TestServices.Projects(store, clock).CreateAsync("Apollo", null, cancellationToken);
        foreach (var id in new[] { boss.Id, ann.Id, olive.Id })
        {
            await store.AddMembershipAsync(id, project.Id, cancellationToken);
        }

        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, ReviewId, "First", [new("a.txt", TestContent.A)], cancellationToken);
        var checklists = TestServices.Checklists(store, clock);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(project.Id, ReviewId, item.Id, ChecklistOutcome.Pass, null, Boss, cancellationToken);
        }

        var proofs = new ReauthProofStore(clock);
        var signing = new SigningService(
            store, totp.UserService, new AllowAllProjectAccess(store), clock, proofs, signingTotp ? totp.Totp : null);
        var annSlot = await signing.AddSlotAsync(Boss, project.Id, ReviewId, ann.Id, null, cancellationToken);
        var oliveSlot = await signing.AddSlotAsync(Boss, project.Id, ReviewId, olive.Id, null, cancellationToken);
        totp.AdvanceSteps(2);
        return new World(totp, signing, proofs, secret, project.Id, annSlot.Id, oliveSlot.Id, olive.Id, recovery);
    }

    /// <summary>
    ///     Signs a slot for the current record.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="actor">The signer.</param>
    /// <param name="slotId">The slot.</param>
    /// <param name="password">The password.</param>
    /// <param name="code">The authenticator code.</param>
    /// <returns>The signature.</returns>
    private static async Task<ReviewSignature> SignAsync(World world, Actor actor, int slotId, string? password, string? code)
    {
        var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, ReviewId, TestContext.Current.CancellationToken))!;
        return await world.Signing.SignWithCredentialsAsync(
            actor,
            world.ProjectId,
            ReviewId,
            slotId,
            approval.Digest,
            new SignerCredentials(password, code),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Gets the state of a slot.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="slotId">The slot.</param>
    /// <returns>The state.</returns>
    private static async Task<SlotSignatureState> StateAsync(World world, int slotId)
    {
        var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, ReviewId, TestContext.Current.CancellationToken))!;
        return approval.Slots.Single(s => s.SlotId == slotId).State;
    }

    /// <summary>
    ///     Test that the right password and a fresh code sign, and that the same code can not sign again.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_EnrolledUserWithPasswordAndFreshCode_Signs()
    {
        // Arrange: ann is enrolled; the clock moved on, so the code is fresh
        var world = await CreateAsync();
        var actor = new Actor("ann", "Ann");
        var code = world.Totp.CodeNow(world.Secret);

        // Act: sign with the password and the code
        var signature = await SignAsync(world, actor, world.AnnSlotId, TotpTestWorld.Password, code);

        // Assert: signed by ann, and the code is spent (a failure afterwards proves the replay protection)
        Assert.Equal("Ann", signature.DisplayName);
        Assert.Equal(SlotSignatureState.Signed, await StateAsync(world, world.AnnSlotId));
        Assert.Equal(1, world.Totp.Store.AdvanceCount);
        Assert.False(await world.Totp.Totp.VerifyForSignatureAsync(
            (await world.Totp.UserService.GetAsync("ann", TestContext.Current.CancellationToken))!,
            code,
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a missing, wrong, malformed, recovery or already used code is refused with one message and nothing
    ///     is signed.
    /// </summary>
    /// <param name="kind">The kind of code.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("wrong")]
    [InlineData("malformed")]
    [InlineData("recovery")]
    [InlineData("used-at-enrollment")]
    public async Task SigningService_SignAsync_EnrolledUserWithoutRightCode_IsRefused(string kind)
    {
        // Arrange: ann is enrolled; the codes of the setup step are the oldest the window still covers
        var world = await CreateAsync();
        var actor = new Actor("ann", "Ann");
        var right = world.Totp.CodeNow(world.Secret);
        var code = kind switch
        {
            "missing" => null,
            "empty" => string.Empty,
            "wrong" => right == "000000" ? "000001" : "000000",
            "malformed" => "12ab56",
            "recovery" => world.RecoveryCodes[0],
            _ => world.Totp.CodeNow(world.Secret, -2)
        };

        // Act: sign with the right password and that code
        var failure = await Assert.ThrowsAsync<DomainRuleException>(
            () => SignAsync(world, actor, world.AnnSlotId, TotpTestWorld.Password, code));

        // Assert: refused with the one message that names both, the recovery code was not spent, and nothing is signed
        Assert.Equal(DomainRule.ReauthenticationFailed, failure.Rule);
        Assert.Equal("The password or the code is incorrect.", failure.Message);
        Assert.Equal(SlotSignatureState.NotSigned, await StateAsync(world, world.AnnSlotId));
        var annId = (await world.Totp.UserService.GetAsync("ann", TestContext.Current.CancellationToken))!.Id;
        Assert.Equal(10, await world.Totp.Store.CountUnusedRecoveryCodesAsync(annId, TestContext.Current.CancellationToken));
        Assert.Equal(0, world.Totp.Store.AdvanceCount);
    }

    /// <summary>
    ///     Test that a wrong password is refused with the same message even with a right code, and that the code is not
    ///     spent by the refusal.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_WrongPasswordRightCode_IsRefusedAndKeepsTheCode()
    {
        // Arrange: ann is enrolled and has a fresh code
        var world = await CreateAsync();
        var actor = new Actor("ann", "Ann");
        var code = world.Totp.CodeNow(world.Secret);

        // Act: sign with a wrong password and the right code, then with both right
        var failure = await Assert.ThrowsAsync<DomainRuleException>(
            () => SignAsync(world, actor, world.AnnSlotId, "not the password", code));
        var signature = await SignAsync(world, actor, world.AnnSlotId, TotpTestWorld.Password, code);

        // Assert: the first said nothing about which part was wrong; the code was still good for the second
        Assert.Equal(DomainRule.ReauthenticationFailed, failure.Rule);
        Assert.Equal("The password or the code is incorrect.", failure.Message);
        Assert.Equal("Ann", signature.DisplayName);
    }

    /// <summary>
    ///     Test that wrong codes while signing count toward the lockout shared with the password.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_WrongCodes_LockTheAccountForSigningAndSignIn()
    {
        // Arrange: ann is enrolled
        var world = await CreateAsync();
        var actor = new Actor("ann", "Ann");
        var wrong = world.Totp.CodeNow(world.Secret) == "000000" ? "000001" : "000000";

        // Act: five wrong codes, then the right password and code
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<DomainRuleException>(
                () => SignAsync(world, actor, world.AnnSlotId, TotpTestWorld.Password, wrong));
        }

        var locked = await Assert.ThrowsAsync<DomainRuleException>(
            () => SignAsync(world, actor, world.AnnSlotId, TotpTestWorld.Password, world.Totp.CodeNow(world.Secret)));

        // Assert: refused the same way, not signed, and the password step of the sign-in is locked as well
        Assert.Equal(DomainRule.ReauthenticationFailed, locked.Rule);
        Assert.Equal(SlotSignatureState.NotSigned, await StateAsync(world, world.AnnSlotId));
        var step = await world.Totp.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, TestContext.Current.CancellationToken);
        Assert.Null(step.User);
        Assert.Contains(world.Totp.AuditStore.Entries, e => e.Action == "signature.reauth-failed");
    }

    /// <summary>
    ///     Test that a signing service without the second factor service refuses an enrolled user (fail closed).
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_EnrolledUserWithoutTotpService_FailsClosed()
    {
        // Arrange: a signing service that was not given the second factor service
        var world = await CreateAsync(signingTotp: false);
        var actor = new Actor("ann", "Ann");

        // Act: sign with everything right
        var failure = await Assert.ThrowsAsync<DomainRuleException>(
            () => SignAsync(world, actor, world.AnnSlotId, TotpTestWorld.Password, world.Totp.CodeNow(world.Secret)));

        // Assert: refused and not signed
        Assert.Equal(DomainRule.ReauthenticationFailed, failure.Rule);
        Assert.Equal(SlotSignatureState.NotSigned, await StateAsync(world, world.AnnSlotId));
    }

    /// <summary>
    ///     Test that a user without a second factor signs with the password alone and gets the password message.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_UserWithoutSecondFactor_NeedsNoCode()
    {
        // Arrange: boss has no second factor; the signing service knows the feature
        var world = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var boss = (await world.Totp.UserService.GetAsync("boss", cancellationToken))!;
        var extra = await world.Signing.AddSlotAsync(Boss, world.ProjectId, ReviewId, boss.Id, null, cancellationToken);

        // Act: a wrong password, then the right one without a code
        var failure = await Assert.ThrowsAsync<DomainRuleException>(
            () => SignAsync(world, Boss, extra.Id, "wrong password", null));
        var signature = await SignAsync(world, Boss, extra.Id, TotpTestWorld.Password, null);

        // Assert: the message only names the password, and the signature exists
        Assert.Equal("The password is incorrect.", failure.Message);
        Assert.Equal("Boss", signature.DisplayName);
    }

    /// <summary>
    ///     Test that an external user signs with a proof exactly as before, whatever the second factor service holds.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ExternalUser_IsNotAffectedByTheSecondFactor()
    {
        // Arrange: a proof for olive
        var world = await CreateAsync();
        world.Proofs.Issue(world.OliveId, world.ProjectId, ReviewId, world.OliveSlotId);

        // Act: sign without a password and without a code
        var signature = await SignAsync(world, new Actor("olive", "Olive"), world.OliveSlotId, null, null);

        // Assert: signed
        Assert.Equal("Olive", signature.DisplayName);
        Assert.Equal(SlotSignatureState.Signed, await StateAsync(world, world.OliveSlotId));
    }

    /// <summary>
    ///     Test that an actor that came through an API token can not sign, even with a right password and code.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ApiTokenActorWithPasswordAndCode_IsRefusedAndKeepsTheCode()
    {
        // Arrange: ann arrives through a token and has a fresh code
        var world = await CreateAsync();
        var viaApi = new Actor("ann", "Ann") { ApiTokenId = "K7M2QX9P" };
        var code = world.Totp.CodeNow(world.Secret);

        // Act: sign with everything right
        var failure = await Assert.ThrowsAsync<DomainRuleException>(
            () => SignAsync(world, viaApi, world.AnnSlotId, TotpTestWorld.Password, code));
        var signature = await SignAsync(world, new Actor("ann", "Ann"), world.AnnSlotId, TotpTestWorld.Password, code);

        // Assert: refused as not allowed, and the refusal did not spend the code for the person
        Assert.Equal(DomainRule.ActorNotAllowed, failure.Rule);
        Assert.Equal("Ann", signature.DisplayName);
    }

    /// <summary>
    ///     Test that a user whose second factor is reset signs with the password alone again.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_AfterAdministratorReset_NeedsNoCode()
    {
        // Arrange: ann is enrolled; an administrator resets the second factor
        var world = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await world.Totp.UserService.CreateAsync("root", "Root", UserRole.Admin, TotpTestWorld.Password, cancellationToken);
        await world.Totp.Totp.ResetAsync(new Actor("root", "Root"), "ann", cancellationToken);

        // Act: sign with the password alone
        var signature = await SignAsync(world, new Actor("ann", "Ann"), world.AnnSlotId, TotpTestWorld.Password, null);

        // Assert: signed
        Assert.Equal("Ann", signature.DisplayName);
    }
}
