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

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     System tests of the promises a regulated user relies on: the complete product is exercised as a user does, with
///     the real server process, a real SQLite database file, a real git repository and a real browser driven by Puppet,
///     and the HTTP API is read with a real token.
/// </summary>
/// <remarks>
///     These tests are the system-level evidence of the reviewer independence, signature re-authentication, signature
///     binding, record immutability (the lock of an approved review and its reopening), evidence and attestation
///     requirements of the system. Like the other Puppet tests
///     they are skipped locally when Chromium (or git) is missing, and fail in CI, where
///     <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>, so the evidence cannot be skipped silently.
/// </remarks>
[Collection("Sequential")]
public class ReviewCenterSystemTests
{
    /// <summary>
    ///     The identifier of the review-set that the test repository defines.
    /// </summary>
    private const string SetId = "docs";

    /// <summary>
    ///     The checklist sections of the default checklist (the test identifiers are the lower-case forms).
    /// </summary>
    private static readonly string[] Sections =
        ["req", "des", "doc", "code", "logic", "err", "use", "test", "sec", "read", "reqdoc", "reqimp", "reqtest", "codedoc"];

    /// <summary>
    ///     Test that only members of the project can be named in reviewer slots, that one person fills one slot only,
    ///     and that a member is added to a project by the exact user name.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_ReviewerSlots_NonMemberAndSamePerson_AreNotAcceptedAsReviewers()
    {
        // Arrange: a planned review, created by puppet, in a project whose members are puppet, second and third;
        // outsider is not a member
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await scenario.OpenReviewAsync();

        // Act: read who is offered, and add slots
        var offered = await browser.EvalAsync(
            "Array.from(document.querySelector('[data-testid=new-slot-primary-select]').options).map(o => o.textContent).join('|')");
        await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);
        var samePerson = await scenario.TryAddSlotAsync("Second Reviewer (second)", alternate: null);
        var alternateInOtherSlot = await scenario.TryAddSlotAsync("Third Reviewer (third)", "Second Reviewer (second)");
        var slotsBefore = await browser.CountAsync("slot-status-");
        await scenario.AddSlotAsync("Third Reviewer (third)", alternate: null);
        var slotsAfter = await browser.CountAsync("slot-status-");

        // Assert: a member can be named, a non-member is not offered, and nobody fills two slots
        Assert.Contains("(second)", offered, StringComparison.Ordinal);
        Assert.Contains("(third)", offered, StringComparison.Ordinal);
        Assert.DoesNotContain("outsider", offered, StringComparison.Ordinal);
        Assert.Contains("already in another slot", samePerson, StringComparison.Ordinal);
        Assert.Contains("already in another slot", alternateInOtherSlot, StringComparison.Ordinal);
        Assert.Equal(1, slotsBefore);
        Assert.Equal(2, slotsAfter);
    }

    /// <summary>
    ///     Test that a review cannot be signed while its checklist is unfinished, that a signature needs the password
    ///     again (a wrong one signs nothing), that a signature is refused when the record changed since the page was
    ///     drawn, that the right password then approves the review, and that the audit trail shows it all and is intact.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_Signing_FreshPasswordAndUnchangedRecord_ApprovesOnlyThen()
    {
        // Arrange: a planned review, created by puppet, with a slot for second, whose checklist is not answered yet;
        // second is signed in in a browser of their own, and puppet's browser changes the record later
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await using var reviewer = await scenario.StartSignedInBrowserAsync("reviewer", "second");
        await scenario.OpenReviewAsync();
        var slot = await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);
        await scenario.OpenReviewAsync(reviewer);
        var blockedText = await reviewer.TextOfAsync($"sign-blockers-{slot}");
        var blockedForm = await reviewer.ExistsAsync($"sign-form-{slot}");

        // Act: answer the checklist, then try a wrong password
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");
        await scenario.OpenReviewAsync(reviewer);
        var formWhenReady = await reviewer.ExistsAsync($"sign-form-{slot}");
        await reviewer.OkAsync("fill", $"sign-password-{slot}", "this is not the password");
        await reviewer.OkAsync("click", $"sign-button-{slot}");
        await reviewer.WaitUntilAsync(() => reviewer.ExistsAsync($"sign-error-{slot}"), "the error of the wrong password");
        var wrongError = await reviewer.TextOfAsync($"sign-error-{slot}");
        var approvalAfterWrong = await reviewer.TextOfAsync("review-approval");
        var statusAfterWrong = await reviewer.TextOfAsync($"slot-status-{slot}");

        // Act: somebody changes the record in another session while the page of the reviewer still shows the old one
        await browser.GotoAsync($"/projects/1/reviews/{scenario.ReviewId}/checklist", "checklist-page");
        await Scenario.OpenSectionAsync(browser, "req");
        await browser.OkAsync("fill", "section-req-reason", "Changed while the other page was open.");
        await browser.OkAsync("click", "section-req-save");
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        await browser.WaitUntilAsync(
            async () =>
            {
                await browser.GotoAsync($"/projects/1/reviews/{scenario.ReviewId}/checklist", "checklist-page");
                await Scenario.OpenSectionAsync(browser, "req");
                return (await browser.EvalAsync("document.querySelector('[data-testid=section-req-reason]').value"))
                    .Contains("Changed while", StringComparison.Ordinal);
            },
            "the changed reason to be stored");

        // Act: sign on the stale page with the right password
        await reviewer.OkAsync("fill", $"sign-password-{slot}", PuppetEndToEndTests.UserPassword);
        await reviewer.OkAsync("click", $"sign-button-{slot}");
        await reviewer.WaitUntilAsync(
            async () => (await reviewer.TextOfAsync($"sign-error-{slot}")).Contains("changed", StringComparison.Ordinal),
            "the refusal of the stale page");
        var approvalAfterStale = await reviewer.TextOfAsync("review-approval");

        // Act: reload the page, so that it shows the record as it is now, and sign with the right password
        await scenario.OpenReviewAsync(reviewer);
        await Scenario.SignAsync(reviewer, slot);

        // Act: read the audit trail of the server and verify its chain
        await browser.GotoAsync("/audit", "audit-page");
        var trail = await browser.TextOfAsync("audit-page");
        await browser.OkAsync("click", "audit-verify");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("audit-result"), "the result of the verification");
        var verification = await browser.TextOfAsync("audit-result");

        // Assert: signing was blocked while unfinished, a wrong password signed nothing, a stale page signed nothing
        Assert.Contains("unanswered", blockedText, StringComparison.Ordinal);
        Assert.False(blockedForm, "A review with unanswered checks must offer no way to sign.");
        Assert.True(formWhenReady, "A finished checklist must offer the form that asks for the password.");
        Assert.Contains("password is incorrect", wrongError, StringComparison.Ordinal);
        Assert.Contains("In review", approvalAfterWrong, StringComparison.Ordinal);
        Assert.DoesNotContain("Signed", statusAfterWrong, StringComparison.Ordinal);
        Assert.Contains("In review", approvalAfterStale, StringComparison.Ordinal);

        // Assert: the audit trail names the failed proof and the signature, never the password, and is intact
        Assert.Contains("signature.reauth-failed", trail, StringComparison.Ordinal);
        Assert.Contains("signature.added", trail, StringComparison.Ordinal);
        Assert.DoesNotContain("this is not the password", trail, StringComparison.Ordinal);
        Assert.DoesNotContain(PuppetEndToEndTests.UserPassword, trail, StringComparison.Ordinal);
        Assert.Contains("Chain intact", verification, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an approved review is delivered as a signed attestation and listed in the evidence index, that a
    ///     token can read it but never sign or arrange reviewers, that nothing is released without credentials, that a
    ///     later change (the reopening of the locked review by an administrator) makes the approval out of date without
    ///     removing the signature or changing the reviewed content, that no posted comment can be edited, and that a
    ///     link to a thread survives the sign-in.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_ApprovedReview_EvidenceTokenAndLaterChange_BehaveAsPromised()
    {
        // Arrange: puppet created the review, so second is its reviewer; puppet comments on it and resolves the
        // thread, answers the checklist, and second signs and holds a personal API token
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await using var reviewer = await scenario.StartSignedInBrowserAsync("reviewer", "second");
        await scenario.OpenReviewAsync();
        var slot = await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");
        await scenario.OpenReviewAsync();
        await browser.OkAsync("click", "file-select-0");
        await browser.OkAsync("wait", "new-thread-form");
        await browser.OkAsync("fill", "thread-text-input", "A comment before the approval.");
        await browser.OkAsync("click", "start-thread-button");
        await browser.OkAsync("wait", "css=[data-testid^=thread-state-]");
        var threadPath = await browser.EvalAsync(
            "document.querySelector('[data-testid^=\"thread-state-\"]').getAttribute('data-testid').replace('thread-state-', '')");
        await browser.OkAsync("click", $"thread-toggle-{threadPath}");
        await browser.WaitUntilAsync(
            async () => (await browser.TextOfAsync($"thread-state-{threadPath}")).Contains("Resolved", StringComparison.Ordinal),
            "the thread to be resolved");
        var commentControls = await browser.EvalAsync("document.querySelectorAll('.comment button, .comment textarea, .comment input').length");
        var token = await Scenario.CreateTokenAsync(reviewer);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(scenario.Server.Url) };
        using var anonymous = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(scenario.Server.Url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var reviewUrl = $"/api/projects/1/reviews/{scenario.ReviewId}";
        var attestationUrl = $"{reviewUrl}/attestation.pdf";
        const string indexUrl = "/api/projects/1/evidence/index.json";

        // Act: ask for the evidence while the review is not signed
        var unsigned = await GetJsonAsync(http, reviewUrl);
        var attestationBefore = await http.GetAsync(attestationUrl, TestContext.Current.CancellationToken);
        var indexBefore = await GetJsonAsync(http, indexUrl);

        // Act: sign with the password, and read the evidence
        await scenario.OpenReviewAsync(reviewer);
        await Scenario.SignAsync(reviewer, slot);
        var signed = await GetJsonAsync(http, reviewUrl);
        var attestation = await http.GetAsync(attestationUrl, TestContext.Current.CancellationToken);
        var pdf = await attestation.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var index = await GetJsonAsync(http, indexUrl);

        // Act: without credentials nothing is released, and a token reaches no page and no way to sign
        var anonymousIndex = await anonymous.GetAsync(indexUrl, TestContext.Current.CancellationToken);
        var anonymousAttestation = await anonymous.GetAsync(attestationUrl, TestContext.Current.CancellationToken);
        var tokenOnPage = await http.GetAsync($"/projects/1/reviews/{scenario.ReviewId}", TestContext.Current.CancellationToken);
        var tokenSigns = await http.PostAsync($"{reviewUrl}/signatures", new StringContent("{}"), TestContext.Current.CancellationToken);
        var tokenSlots = await http.PostAsync($"{reviewUrl}/slots", new StringContent("{}"), TestContext.Current.CancellationToken);
        var slotCanChange = await reviewer.ExistsAsync($"slot-remove-{slot}") || await reviewer.ExistsAsync($"slot-save-{slot}");

        // Act: an administrator reopens the approved review, which is the only later change that is possible
        await scenario.OpenReviewAsync();
        await browser.OkAsync("fill", "reopen-reason", "The reviewed content must be looked at again.");
        await browser.OkAsync("click", "reopen-button");
        await browser.WaitUntilAsync(
            async () => (await browser.TextOfAsync("review-approval")).Contains("In review", StringComparison.Ordinal),
            "the approval to end");
        var changed = await GetJsonAsync(http, reviewUrl);
        var attestationAfter = await http.GetAsync(attestationUrl, TestContext.Current.CancellationToken);
        var indexAfter = await GetJsonAsync(http, indexUrl);
        var slotCanChangeAfter = await browser.ExistsAsync($"slot-remove-{slot}") || await browser.ExistsAsync($"slot-save-{slot}");

        // Act: somebody who is signed out follows a link to the thread
        await using var other = await scenario.StartBrowserAsync("link");
        await other.OkAsync("goto", $"/projects/1/reviews/{scenario.ReviewId}/threads/{threadPath}");
        await other.OkAsync("wait", "login-button");
        await other.OkAsync("fill", "login-username-input", PuppetEndToEndTests.UserName);
        await other.OkAsync("fill", "login-password-input", PuppetEndToEndTests.UserPassword);
        await other.RunAsync("click", "login-button");
        await other.OkAsync("wait", "css=[data-testid=review-page]", "--timeout", "60000");
        var landedOn = await other.EvalAsync("location.pathname");

        // Assert: before the signature there is no attestation and nothing in the index
        Assert.False(unsigned.GetProperty("approved").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, attestationBefore.StatusCode);
        Assert.Equal(0, indexBefore.GetProperty("reviews").GetArrayLength());

        // Assert: after it the review is approved, the PDF is a digitally signed document, and the index names the set
        Assert.True(signed.GetProperty("approved").GetBoolean());
        Assert.Equal("Signed", signed.GetProperty("slots")[0].GetProperty("state").GetString());
        Assert.Equal("Second Reviewer", signed.GetProperty("slots")[0].GetProperty("signedBy").GetString());
        Assert.Equal(HttpStatusCode.OK, attestation.StatusCode);
        Assert.Equal("application/pdf", attestation.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Contains("/ByteRange", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        var entry = Assert.Single(index.GetProperty("reviews").EnumerateArray());
        Assert.Equal(SetId, entry.GetProperty("id").GetString());
        Assert.Equal(signed.GetProperty("contentFingerprint").GetString(), entry.GetProperty("fingerprint").GetString());
        Assert.Equal("pass", entry.GetProperty("result").GetString());

        // Assert: nothing is released without credentials, and a token can neither open a page nor sign
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousIndex.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAttestation.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, tokenOnPage.StatusCode);
        Assert.Contains("/login", tokenOnPage.Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, tokenSigns.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tokenSlots.StatusCode);
        Assert.False(slotCanChange, "A signed slot must offer no way to change or remove it.");

        // Assert: reopening ends the approval, keeps the signature as history and the content as it was
        Assert.False(changed.GetProperty("approved").GetBoolean());
        Assert.Equal("OutOfDate", changed.GetProperty("slots")[0].GetProperty("state").GetString());
        Assert.NotEqual(signed.GetProperty("approvalDigest").GetString(), changed.GetProperty("approvalDigest").GetString());
        Assert.Equal(signed.GetProperty("contentFingerprint").GetString(), changed.GetProperty("contentFingerprint").GetString());
        Assert.Equal(HttpStatusCode.Conflict, attestationAfter.StatusCode);
        Assert.Equal(0, indexAfter.GetProperty("reviews").GetArrayLength());
        Assert.Equal("0", commentControls);
        Assert.False(slotCanChangeAfter, "A slot that holds a signature must stay fixed, also when the signature is out of date.");

        // Assert: the link to the thread survived the sign-in
        Assert.EndsWith($"/threads/{threadPath}", landedOn, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an approved review is locked: it shows as locked with its controls disabled, a member who is not
    ///     an administrator cannot reopen it, an administrator needs a reason to reopen it, and the reopened review is
    ///     in review again with the earlier signature kept as history, needs a fresh signature, and has the reopening in
    ///     its history.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_ApprovedReview_IsLockedUntilAnAdministratorReopensItWithAReason()
    {
        // Arrange: a review that puppet (the administrator) created and second (a member) approved
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await using var reviewer = await scenario.StartSignedInBrowserAsync("reviewer", "second");
        await scenario.OpenReviewAsync();
        var slot = await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");
        await scenario.OpenReviewAsync(reviewer);
        await Scenario.SignAsync(reviewer, slot);

        // Act: look at the approved review as the administrator, on its page and on its checklist
        await scenario.OpenReviewAsync();
        var approvalLocked = await browser.TextOfAsync("review-approval");
        var lockedBadge = await browser.TextOfAsync("review-locked");
        var slotsDisabled = await browser.IsDisabledAsync("new-slot-primary-select")
                            && await browser.IsDisabledAsync("new-slot-alternate-select")
                            && await browser.IsDisabledAsync("new-slot-add-button");
        await browser.OkAsync("click", "file-select-0");
        await browser.OkAsync("wait", "selected-file-path");
        var commentDisabled = await browser.EvalAsync(
            "document.querySelectorAll('[data-testid=\"new-thread-form\"], [data-testid=\"thread-text-input\"], [data-testid=\"start-thread-button\"]').length") == "0";
        await browser.GotoAsync($"/projects/1/reviews/{scenario.ReviewId}/checklist", "checklist-page");
        await browser.OkAsync("wait", "checklist-locked");
        await Scenario.OpenSectionAsync(browser, "req");
        var checklistDisabled = await browser.IsDisabledAsync("section-req-applicable")
                                && await browser.IsDisabledAsync("section-req-reason")
                                && await browser.IsDisabledAsync("section-req-save");

        // Act: look at it as a member who is not an administrator
        await scenario.OpenReviewAsync(reviewer);
        var memberSeesLock = await reviewer.ExistsAsync("review-locked");
        var memberCanReopen = await reviewer.ExistsAsync("reopen-form") || await reviewer.ExistsAsync("reopen-button");

        // Act: the administrator tries to reopen without a reason, then with one
        await scenario.OpenReviewAsync();
        await browser.OkAsync("click", "reopen-button");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("reopen-error"), "the refusal of the missing reason");
        var noReasonError = await browser.TextOfAsync("reopen-error");
        var lockedAfterRefusal = await browser.ExistsAsync("review-locked");
        await browser.OkAsync("fill", "reopen-reason", "Found a problem that needs another look.");
        await browser.OkAsync("click", "reopen-button");
        await browser.WaitUntilAsync(
            async () => (await browser.TextOfAsync("review-approval")).Contains("In review", StringComparison.Ordinal),
            "the review to be reopened");
        var lockedAfterReopen = await browser.ExistsAsync("review-locked");
        var reopenedBy = await browser.TextOfAsync("reopen-by-0");
        var reopenReason = await browser.TextOfAsync("reopen-reason-0");
        var slotAfterReopen = await browser.TextOfAsync($"slot-status-{slot}");
        var slotsEnabledAfterReopen = !await browser.IsDisabledAsync("new-slot-add-button");

        // Act: the reviewer needs a fresh signature, and the review is approved and locked again with it
        await scenario.OpenReviewAsync(reviewer);
        var signFormAfterReopen = await reviewer.ExistsAsync($"sign-form-{slot}");
        var approvalBeforeFreshSignature = await reviewer.TextOfAsync("review-approval");
        await Scenario.SignAsync(reviewer, slot);
        var lockedAgain = await reviewer.ExistsAsync("review-locked");

        // Act: read the history of the review
        await browser.GotoAsync($"/projects/1/reviews/{scenario.ReviewId}/history", "history-page");
        await browser.OkAsync("wait", "history-table");
        var history = await browser.TextOfAsync("history-table");

        // Assert: the approved review is locked, and nothing that would change it can be used
        Assert.Contains("Approved", approvalLocked, StringComparison.Ordinal);
        Assert.Contains("Locked", lockedBadge, StringComparison.Ordinal);
        Assert.True(slotsDisabled, "The reviewer slots of a locked review must not be changeable.");
        Assert.True(commentDisabled, "A locked review must not offer a form for a new comment.");
        Assert.True(checklistDisabled, "The checklist of a locked review must be read-only.");
        Assert.True(memberSeesLock);
        Assert.False(memberCanReopen, "Only an administrator may be offered the reopening.");

        // Assert: a reason is required, and the review stays locked until it is given
        Assert.Contains("reason", noReasonError, StringComparison.Ordinal);
        Assert.True(lockedAfterRefusal, "A reopening without a reason must leave the review locked.");

        // Assert: the reopened review is in review, keeps the old signature as history, and shows who reopened it and why
        Assert.False(lockedAfterReopen);
        Assert.Equal("Puppet Admin", reopenedBy);
        Assert.Contains("Found a problem", reopenReason, StringComparison.Ordinal);
        Assert.Contains("out of date", slotAfterReopen, StringComparison.Ordinal);
        Assert.True(slotsEnabledAfterReopen);

        // Assert: a fresh signature is needed, and it approves and locks the review again
        Assert.True(signFormAfterReopen, "The reviewer must be able to sign the reopened review again.");
        Assert.Contains("In review", approvalBeforeFreshSignature, StringComparison.Ordinal);
        Assert.True(lockedAgain);

        // Assert: the history shows the first signature, the reopening with its reason, and the fresh signature
        Assert.Contains("review.reopened", history, StringComparison.Ordinal);
        Assert.Contains("Found a problem", history, StringComparison.Ordinal);
        Assert.Equal(2, history.Split("signature.added").Length - 1);
    }

    /// <summary>
    ///     Test that the author of a review is not offered as a reviewer while the project requires independent
    ///     reviewers, that an administrator can switch the rule off (which is audited) so that the author can be
    ///     seated, and that switching it on again stops the author from signing, with a clear message.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_IndependentReviewers_AuthorIsNotOfferedAndAdministratorCanSwitchTheRuleOff()
    {
        // Arrange: a review that puppet (a project administrator) created; the rule is on, as it is by default
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await scenario.OpenReviewAsync();
        var offeredWhenOn = await Scenario.OfferedReviewersAsync(browser);
        var hintWhenOn = await browser.ExistsAsync("author-independence-hint");

        // Act: the administrator switches the rule off, and the author is seated
        var statusOff = await scenario.SetIndependentReviewersAsync(required: false);
        await scenario.OpenReviewAsync();
        var offeredWhenOff = await Scenario.OfferedReviewersAsync(browser);
        var hintWhenOff = await browser.ExistsAsync("author-independence-hint");
        var slot = await scenario.AddSlotAsync("Puppet Admin (puppet)", alternate: null);
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");

        // Act: the rule is switched on again, and the author tries to sign with the right password
        var statusOn = await scenario.SetIndependentReviewersAsync(required: true);
        await scenario.OpenReviewAsync();
        await browser.OkAsync("fill", $"sign-password-{slot}", PuppetEndToEndTests.UserPassword);
        await browser.OkAsync("click", $"sign-button-{slot}");
        await browser.WaitUntilAsync(() => browser.ExistsAsync($"sign-error-{slot}"), "the refusal of the author");
        var signError = await browser.TextOfAsync($"sign-error-{slot}");
        var approval = await browser.TextOfAsync("review-approval");

        // Act: read the audit trail of the server
        await browser.GotoAsync("/audit", "audit-page");
        var trail = await browser.TextOfAsync("audit-page");

        // Assert: while the rule is on the author is not offered, and the page tells the author why
        Assert.Contains("(second)", offeredWhenOn, StringComparison.Ordinal);
        Assert.DoesNotContain("(puppet)", offeredWhenOn, StringComparison.Ordinal);
        Assert.True(hintWhenOn, "The author must be told that somebody else has to review.");

        // Assert: once an administrator switched it off the author is offered and can be seated
        Assert.Contains("no longer required", statusOff, StringComparison.Ordinal);
        Assert.Contains("(puppet)", offeredWhenOff, StringComparison.Ordinal);
        Assert.False(hintWhenOff);

        // Assert: after it is switched on again the author cannot sign, and is told why, and the review is not approved
        Assert.Contains("now required", statusOn, StringComparison.Ordinal);
        Assert.Contains("You created this review, so you cannot be a reviewer of it", signError, StringComparison.Ordinal);
        Assert.Contains("In review", approval, StringComparison.Ordinal);

        // Assert: the audit trail shows that the rule was changed
        Assert.Contains("project.independent-reviewers-changed", trail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an administrator can check a checklist definition on the checklist template page (every error of an
    ///     invalid one is listed with its path and nothing is stored), replace the checklist of the project with a valid
    ///     one, so that only reviews created afterwards start with it while a review created before keeps the default,
    ///     and reset the project to the built-in default, and that the audit trail records the changes.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_ChecklistTemplate_ReplaceChangesOnlyNewReviewsAndResetRestoresTheDefault()
    {
        // Arrange: a review that exists before the checklist is replaced, and the checklist template page of the project
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        var oldReview = scenario.ReviewId;
        await browser.GotoAsync("/projects/1/checklist", "project-checklist-page");
        var defaultSummary = await browser.TextOfAsync("checklist-current-counts");
        var defaultBadge = await browser.ExistsAsync("checklist-default-badge");

        // Act: check an invalid definition with four problems
        await Scenario.ValidateChecklistTextAsync(
            browser,
            "{\"version\":\"\",\"sections\":[{\"id\":\"a b\",\"title\":\"\",\"items\":[]}]}",
            valid: false);
        var errorPaths = await browser.EvalAsync(
            "Array.from(document.querySelectorAll('[data-testid=\"checklist-error-path\"]')).map(e => e.textContent).join('|')");
        var replaceOfferedForInvalid = await browser.ExistsAsync("checklist-replace");

        // Act: check a valid definition of two sections, and replace the checklist with it
        const string Valid = "{\"version\":\"system-test-1\",\"sections\":["
            + "{\"id\":\"alpha\",\"title\":\"Alpha\",\"items\":[{\"id\":\"alpha-1\",\"text\":\"Is alpha one right?\"},{\"id\":\"alpha-2\",\"text\":\"Is alpha two right?\"}]},"
            + "{\"id\":\"beta\",\"title\":\"Beta\",\"items\":[{\"id\":\"beta-1\",\"text\":\"Is beta right?\"}]}]}";
        await Scenario.ValidateChecklistTextAsync(browser, Valid, valid: true);
        var previewItems = await browser.CountAsync("checklist-preview-item-");
        await browser.OkAsync("click", "checklist-replace");
        await browser.OkAsync("wait", "checklist-replace-confirmation");
        var confirmation = System.Text.RegularExpressions.Regex.Replace(await browser.TextOfAsync("checklist-replace-confirmation"), @"\s+", " ");
        await browser.OkAsync("click", "checklist-replace-confirm");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("project-checklist-status"), "the confirmation of the replace");
        var replacedSummary = await browser.TextOfAsync("checklist-current-counts");
        var replacedBadge = await browser.ExistsAsync("checklist-default-badge");

        // Act: the review of before still has the default, and a review created now has the new checklist
        var oldAfterReplace = await Scenario.ReviewSectionIdsAsync(browser, scenario.ReviewId);
        var newReview = await scenario.PlanNewReviewAsync();
        var newAfterReplace = await Scenario.ReviewSectionIdsAsync(browser, newReview);
        await Scenario.OpenSectionAsync(browser, "alpha");
        var alphaChecks = await browser.CountAsync("check-alpha-");
        await Scenario.OpenSectionAsync(browser, "beta");
        var betaChecks = await browser.CountAsync("check-beta-");

        // Act: reset the project to the built-in default
        await browser.GotoAsync("/projects/1/checklist", "project-checklist-page");
        await browser.OkAsync("click", "checklist-reset");
        await browser.OkAsync("wait", "checklist-reset-confirmation");
        await browser.OkAsync("click", "checklist-reset-confirm");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("checklist-default-badge"), "the default checklist to be back");
        var resetSummary = await browser.TextOfAsync("checklist-current-counts");
        var resetStatus = await browser.TextOfAsync("project-checklist-status");
        var newAfterReset = await Scenario.ReviewSectionIdsAsync(browser, newReview);

        // Act: read the audit trail of the server
        await browser.GotoAsync("/audit", "audit-page");
        var trail = await browser.TextOfAsync("audit-page");

        // Assert: the project started with the built-in default
        Assert.Equal("default-1 (14 sections, 58 checks)", defaultSummary);
        Assert.True(defaultBadge, "The built-in default must be marked as such.");

        // Assert: every error of the invalid definition is listed with its path, and it cannot be replaced in
        Assert.Contains("version", errorPaths, StringComparison.Ordinal);
        Assert.Contains("sections[0].id", errorPaths, StringComparison.Ordinal);
        Assert.Contains("sections[0].title", errorPaths, StringComparison.Ordinal);
        Assert.Contains("sections[0].items", errorPaths, StringComparison.Ordinal);
        Assert.False(replaceOfferedForInvalid, "An invalid definition must offer no replace.");

        // Assert: the valid definition was previewed, the replace said what it does to existing reviews, and took effect
        Assert.Equal(3, previewItems);
        Assert.Contains("Existing reviews keep the checklist", confirmation, StringComparison.Ordinal);
        Assert.Equal("system-test-1 (2 sections, 3 checks)", replacedSummary);
        Assert.False(replacedBadge);

        // Assert: the review of before keeps the default, the new review starts with exactly the new checklist
        Assert.Equal(14, oldAfterReplace.Length);
        Assert.Equal(["alpha", "beta"], newAfterReplace);
        Assert.Equal(2, alphaChecks);
        Assert.Equal(1, betaChecks);
        Assert.NotEqual(oldReview, newReview);

        // Assert: the reset returns the project to the default for new reviews and leaves the existing reviews alone
        Assert.Equal("default-1 (14 sections, 58 checks)", resetSummary);
        Assert.Contains("built-in default", resetStatus, StringComparison.Ordinal);
        Assert.Equal(["alpha", "beta"], newAfterReset);

        // Assert: the audit trail shows both changes
        Assert.Contains("project.checklist-changed", trail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a review can be given a due date when it is planned and can have the date set, changed and shown
    ///     as overdue on its page (the date is a UTC calendar day), that every change of the date is audited, and that
    ///     an approved review keeps its date as history without the Overdue badge and cannot have it edited.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_DueDate_SetOnCreationAndEdit_ShowsOverdueAndIsAudited()
    {
        // Arrange: a review that puppet created without a due date, and two dates computed in UTC
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await using var reviewer = await scenario.StartSignedInBrowserAsync("reviewer", "second");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ahead = today.AddDays(7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await scenario.OpenReviewAsync();
        var noDateAtFirst = await browser.ExistsAsync("review-due-none");

        // Act: plan another review with a due date, and open it
        var planned = await scenario.PlanNewReviewAsync(ahead);
        await browser.GotoAsync($"/projects/1/reviews/{planned}", "review-page");
        var plannedDue = await browser.TextOfAsync("review-due-date");
        var plannedOverdue = await browser.ExistsAsync("review-overdue");

        // Act: change the date of that review to yesterday, which is allowed on edit and makes it overdue
        await browser.OkAsync("fill", "review-due-input", yesterday);
        await browser.OkAsync("click", "review-due-save");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("review-overdue"), "the Overdue badge");
        var editedDue = await browser.TextOfAsync("review-due-date");

        // Act: clear it again
        await browser.OkAsync("click", "review-due-clear");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("review-due-none"), "the date to be cleared");
        var overdueAfterClear = await browser.ExistsAsync("review-overdue");

        // Act: give the first review yesterday as its date, then approve it
        await scenario.OpenReviewAsync();
        await browser.OkAsync("fill", "review-due-input", yesterday);
        await browser.OkAsync("click", "review-due-save");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("review-overdue"), "the Overdue badge of the first review");
        var slot = await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");
        await scenario.OpenReviewAsync(reviewer);
        await Scenario.SignAsync(reviewer, slot);

        // Act: look at the approved review
        await scenario.OpenReviewAsync();
        var lockedDue = await browser.TextOfAsync("review-due-date");
        var lockedOverdue = await browser.ExistsAsync("review-overdue");
        var lockedForm = await browser.ExistsAsync("due-date-form");
        var lockedBadge = await browser.ExistsAsync("review-locked");

        // Act: read the audit trail of the server
        await browser.GotoAsync("/audit", "audit-page");
        var trail = await browser.TextOfAsync("audit-page");

        // Assert: the first review had no date, and the planned one had the date it was planned with, not overdue
        Assert.True(noDateAtFirst);
        Assert.Contains(ahead, plannedDue, StringComparison.Ordinal);
        Assert.Contains("UTC", plannedDue, StringComparison.Ordinal);
        Assert.False(plannedOverdue, "A date in the future must not be overdue.");

        // Assert: a date in the past is accepted on edit and shown as overdue, and clearing removes the badge
        Assert.Contains(yesterday, editedDue, StringComparison.Ordinal);
        Assert.False(overdueAfterClear);

        // Assert: an approved review keeps its date, is not overdue and offers no edit
        Assert.True(lockedBadge);
        Assert.Contains(yesterday, lockedDue, StringComparison.Ordinal);
        Assert.False(lockedOverdue, "An approved review is never overdue.");
        Assert.False(lockedForm, "The due date of an approved review must not be editable.");

        // Assert: the audit trail shows that the dates were changed
        Assert.Contains("review.due-date-changed", trail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the project page shows, for every review, its stage, signatures, who it is waiting on and the
    ///     overdue badge, counts the stages and the overdue reviews in the summary, filters the table by stage, by
    ///     overdue and by text through the query string and the filter form, and shows an approved review as such.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_ProjectOverview_ShowsWhoIsBlockingAndFiltersByState()
    {
        // Arrange: a first review that waits for the reviewer second and is overdue, and a second review in setup
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await using var reviewer = await scenario.StartSignedInBrowserAsync("reviewer", "second");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var first = scenario.ReviewId;
        var planned = await scenario.PlanNewReviewAsync();
        await scenario.OpenReviewAsync();
        await browser.OkAsync("fill", "review-due-input", yesterday);
        await browser.OkAsync("click", "review-due-save");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("review-overdue"), "the Overdue badge");
        var slot = await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);

        // Act: read the overview
        await browser.GotoAsync("/projects/1", "project-page");
        var rows = await browser.CountAsync("review-row-");
        var stage = await browser.TextOfAsync($"review-approval-{first}");
        var progress = await browser.TextOfAsync($"review-progress-{first}");
        var waiting = await browser.TextOfAsync($"review-waiting-{first}");
        var overdue = await browser.ExistsAsync($"review-overdue-{first}");
        var plannedStage = await browser.TextOfAsync($"review-approval-{planned}");
        var plannedOverdue = await browser.ExistsAsync($"review-overdue-{planned}");
        var summaryOverdue = await browser.TextOfAsync("project-summary-overdue");
        var summarySetup = await browser.TextOfAsync("project-summary-setup");

        // Act: filter by the query string
        await browser.GotoAsync("/projects/1?stage=setup", "project-page");
        var setupRows = await browser.EvalAsync("Array.from(document.querySelectorAll('[data-testid^=\"review-row-\"]')).map(e => e.getAttribute('data-testid')).join('|')");
        await browser.GotoAsync("/projects/1?overdue=1", "project-page");
        var overdueRows = await browser.EvalAsync("Array.from(document.querySelectorAll('[data-testid^=\"review-row-\"]')).map(e => e.getAttribute('data-testid')).join('|')");
        await browser.GotoAsync("/projects/1?stage=approved", "project-page");
        var approvedBefore = await browser.CountAsync("review-row-");
        var noMatch = await browser.ExistsAsync("reviews-no-match");

        // Act: filter by the form, by text
        await browser.GotoAsync("/projects/1", "project-page");
        await browser.OkAsync("fill", "filter-search", planned);
        await browser.OkAsync("click", "filter-apply");
        await browser.WaitUntilAsync(async () => await browser.CountAsync("review-row-") == 1, "the filter form to narrow the table");
        var searchRow = await browser.EvalAsync("document.querySelector('[data-testid^=\"review-row-\"]').getAttribute('data-testid')");

        // Act: approve the first review as second, then read the overview again
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");
        await scenario.OpenReviewAsync(reviewer);
        await Scenario.SignAsync(reviewer, slot);
        await browser.GotoAsync("/projects/1?stage=approved", "project-page");
        var approvedRows = await browser.CountAsync("review-row-");
        var approvedStage = await browser.TextOfAsync($"review-approval-{first}");
        var approvedOverdue = await browser.ExistsAsync($"review-overdue-{first}");
        var approvedWaiting = await browser.TextOfAsync($"review-waiting-{first}");
        var summaryApproved = await browser.TextOfAsync("project-summary-approved");
        var summaryOverdueAfter = await browser.TextOfAsync("project-summary-overdue");

        // Assert: the first review waits for second and is overdue; the planned one is in setup and not overdue
        Assert.Equal(2, rows);
        Assert.Equal("In review", stage);
        Assert.Equal("0/1", progress);
        Assert.Equal("Second Reviewer", waiting);
        Assert.True(overdue, "A review due yesterday that is not approved must show the Overdue badge.");
        Assert.Equal("Setup", plannedStage);
        Assert.False(plannedOverdue);
        Assert.Equal("1 overdue", summaryOverdue);
        Assert.Equal("1 in setup", summarySetup);

        // Assert: the filters narrow the table
        Assert.Equal($"review-row-{planned}", setupRows);
        Assert.Equal($"review-row-{first}", overdueRows);
        Assert.Equal(0, approvedBefore);
        Assert.True(noMatch);
        Assert.Equal($"review-row-{planned}", searchRow);

        // Assert: once approved the review is in the approved stage, is not overdue and waits for nobody
        Assert.Equal(1, approvedRows);
        Assert.Equal("Approved", approvedStage);
        Assert.False(approvedOverdue, "An approved review is never overdue.");
        Assert.Equal("-", approvedWaiting);
        Assert.Equal("1 approved", summaryApproved);
        Assert.Equal("0 overdue", summaryOverdueAfter);
    }

    /// <summary>
    ///     Test that the audit page filters the trail through its form by action and by actor, keeps the choice in the
    ///     address, offers to clear it, and says so when nothing matches.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_AuditTrail_FiltersByActionAndActor()
    {
        // Arrange: the scenario has signed in as the user puppet and created a review, so the trail holds both
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        const string actionsOfRows = "Array.from(document.querySelectorAll('[data-testid=\"audit-action\"]')).map(e => e.textContent).filter((v, i, a) => a.indexOf(v) === i).join('|')";
        const string interactive = "css=[data-testid=audit-page][data-interactive=true]";

        // Act: read the whole trail
        await browser.GotoAsync("/audit", "audit-page");
        var allRows = await browser.CountAsync("audit-row");
        var clearAtFirst = await browser.ExistsAsync("audit-filter-clear");

        // Act: choose an action in the form and apply it
        await browser.OkAsync("select", "audit-filter-action", "review.created");
        await browser.OkAsync("click", "audit-filter-apply");
        await browser.OkAsync("wait", "css=[data-testid=audit-filter-clear]", "--timeout", "30000");
        await browser.OkAsync("wait", interactive, "--timeout", "30000");
        var actionsShown = await browser.EvalAsync(actionsOfRows);
        var actionRows = await browser.CountAsync("audit-row");
        var address = await browser.EvalAsync("location.pathname + location.search");

        // Act: filter by the actor through the address, then by an actor nobody has
        await browser.GotoAsync($"/audit?actor={PuppetEndToEndTests.UserName}&action=auth.", "audit-page");
        var authActions = await browser.EvalAsync(actionsOfRows);
        var actorNames = await browser.EvalAsync("Array.from(document.querySelectorAll('[data-testid=\"audit-actor\"]')).every(e => e.textContent.includes('" + PuppetEndToEndTests.UserName + "')) ? 'all' : 'other'");
        await browser.GotoAsync("/audit?actor=nobody-has-this-name", "audit-page");
        var noMatch = await browser.ExistsAsync("audit-empty");
        var noMatchRows = await browser.CountAsync("audit-row");

        // Act: clear the filters
        await browser.OkAsync("click", "audit-filter-clear");
        await browser.OkAsync("wait", "css=[data-testid=audit-table]", "--timeout", "30000");
        await browser.OkAsync("wait", interactive, "--timeout", "30000");
        var clearedRows = await browser.CountAsync("audit-row");

        // Assert: the form narrows the trail to the chosen action, and the choice is in the address
        Assert.False(clearAtFirst, "Nothing is filtered at first, so there is nothing to clear.");
        Assert.True(actionRows > 0, "The review that was created must be in the trail.");
        Assert.True(actionRows < allRows, "The filter must leave out the entries of other actions.");
        Assert.Equal("review.created", actionsShown);
        Assert.Contains("action=review.created", address, StringComparison.Ordinal);

        // Assert: the actor and the group narrow it, and a text nobody matches shows the no-match state
        Assert.StartsWith("auth.", authActions, StringComparison.Ordinal);
        Assert.Equal("all", actorNames);
        Assert.True(noMatch);
        Assert.Equal(0, noMatchRows);
        Assert.Equal(allRows, clearedRows);
    }

    /// <summary>
    ///     Test that the export link of the audit page, after the trail was filtered, hands out the filtered trail as
    ///     a CSV download that states the chain is intact, using the session of the browser.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_AuditTrail_Export_ReportsChainStatus()
    {
        // Arrange: the scenario has signed in as the user puppet and created a review; filter by the action
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await browser.GotoAsync("/audit?action=review.created", "audit-page");
        var linkShown = await browser.ExistsAsync("audit-export-csv");
        var href = await browser.EvalAsync("document.querySelector('[data-testid=\"audit-export-csv\"]').getAttribute('href')");

        // Act: fetch the link from the page, so that the cookies of the session are used
        await browser.EvalAsync(
            "(() => { window.exportResult = null; fetch('" + href + "').then(async r => { const t = await r.text(); " +
            "window.exportResult = r.status + '|' + r.headers.get('X-ReviewCenter-Audit-Chain') + '|' + " +
            "r.headers.get('Content-Type') + '|' + r.headers.get('Content-Disposition') + '|' + " +
            "t.split('\\r\\n').filter(l => l.length > 0).length + '|' + t.includes('review.created'); })" +
            ".catch(e => { window.exportResult = 'error ' + e; }); return 'started'; })()");
        await browser.WaitUntilAsync(
            async () => await browser.EvalAsync("window.exportResult !== null") == "true",
            "the export to be fetched");
        var result = (await browser.EvalAsync("window.exportResult")).Split('|');

        // Assert: a CSV with the chain status in the header, holding the filtered action only
        Assert.True(linkShown, "The audit page must offer the CSV export.");
        Assert.Contains("format=csv", href, StringComparison.Ordinal);
        Assert.Contains("action=review.created", href, StringComparison.Ordinal);
        Assert.Equal("200", result[0]);
        Assert.Equal("intact", result[1]);
        Assert.StartsWith("text/csv", result[2], StringComparison.Ordinal);
        Assert.Contains("attachment", result[3], StringComparison.Ordinal);
        Assert.Contains("chain-intact.csv", result[3], StringComparison.Ordinal);
        Assert.True(int.Parse(result[4], System.Globalization.CultureInfo.InvariantCulture) >= 2, "The header and the created review must be in the file.");
        Assert.Equal("true", result[5]);
    }

    /// <summary>
    ///     Test that a follow-up review, created through the form from an earlier review that has a failed check, an
    ///     open thread and sections that do not apply, carries the Pass and N/A answers, the sections and the open
    ///     thread with visible marks, shows the earlier Fail for a new decision, can be signed once that is decided and
    ///     the carried thread is resolved, leaves the earlier review as it was, and names the earlier review in its
    ///     attestation.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_FollowUpReview_CarriesWhatHoldsAndIsApprovedOnItsOwn()
    {
        // Arrange: puppet fails a check in the planned review A, passes the rest of its section, marks the other
        // sections as not applicable and leaves an open thread; second is the reviewer of the follow-up
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await using var reviewer = await scenario.StartSignedInBrowserAsync("reviewer", "second");
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.", except: "req");
        await Scenario.OpenSectionAsync(browser, "req");
        await browser.OkAsync("click", "check-fail-req-01");
        await browser.OkAsync("wait", "check-justification-req-01");
        await browser.OkAsync("fill", "check-justification-req-01", "The requirement is not traceable.");
        await browser.OkAsync("click", "check-save-req-01");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("check-answered-by-req-01"), "the Fail of REQ-01 to be saved");
        for (var number = 2; number <= 8; number++)
        {
            await AnswerPassAsync(browser, $"req-0{number}");
        }

        await scenario.OpenReviewAsync();
        await ClickFileAsync(browser, "guide.md");
        await browser.OkAsync("wait", "new-thread-form");
        await browser.OkAsync("fill", "thread-text-input", "Please check this guide before it is relied on.");
        await browser.OkAsync("click", "start-thread-button");
        await browser.OkAsync("wait", "css=[data-testid^=thread-state-]");
        const string followId = "follow-up-b";
        var titleSuggestion = $"Follow-up to {scenario.ReviewId}";

        // Act: create the follow-up through the form, choosing the earlier review
        await browser.GotoAsync("/projects/1/reviews/new", "new-review-page");
        await browser.OkAsync("fill", "review-id-input", followId);
        await browser.OkAsync("check", "source-repository-radio");
        await browser.OkAsync("select", "repository-select", "1");
        await browser.WaitUntilAsync(
            async () => await browser.EvalAsync(
                $"!!document.querySelector('[data-testid=follow-source-select] option[value=\"{scenario.ReviewId}\"]')") == "true",
            "the earlier review to be offered");
        await browser.OkAsync("select", "follow-source-select", scenario.ReviewId);
        await browser.OkAsync("fill", "head-revision-input", "main");
        await browser.WaitUntilAsync(
            async () => await browser.EvalAsync("document.querySelector('[data-testid=review-title-input]').value") == titleSuggestion,
            "the suggested title");
        var hintShown = await browser.ExistsAsync("follow-preview-hint");
        await browser.OkAsync("click", "create-review-button");
        await browser.OkAsync("wait", "css=[data-testid=review-page][data-interactive=true]", "--timeout", "60000");

        // Act: read what the follow-up page says about it
        var followsLink = await browser.TextOfAsync("review-follows-link");
        var carriedCounts = await browser.TextOfAsync("review-carried-counts");
        await ClickFileAsync(browser, "guide.md");
        await browser.OkAsync("wait", "css=[data-testid^=thread-state-]");
        var threadId = await browser.EvalAsync(
            "document.querySelector('[data-testid^=\"thread-state-\"]').getAttribute('data-testid').replace('thread-state-', '')");
        var threadMarked = await browser.ExistsAsync($"thread-carried-{threadId}");

        // Act: read the checklist of the follow-up
        await browser.GotoAsync($"/projects/1/reviews/{followId}/checklist", "checklist-page");
        await Scenario.OpenSectionAsync(browser, "req");
        var passCarried = await browser.ExistsAsync("check-carried-req-02");
        var sectionCarried = await browser.ExistsAsync("section-des-carried");
        var failCarried = await browser.ExistsAsync("check-carried-req-01");
        var earlierFail = await browser.TextOfAsync("check-earlier-fail-req-01");
        var failAnswered = await browser.ExistsAsync("check-answered-by-req-01");

        // Act: decide the failed check again, resolve the carried thread, add the reviewer, and sign as second
        await browser.OkAsync("click", "check-pass-req-01");
        await browser.WaitUntilAsync(() => browser.ExistsAsync("check-answered-by-req-01"), "the new answer of REQ-01");
        var earlierFailAfter = await browser.ExistsAsync("check-earlier-fail-req-01");
        await browser.GotoAsync($"/projects/1/reviews/{followId}", "review-page");
        var slot = await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);
        await ClickFileAsync(browser, "guide.md");
        await browser.OkAsync("wait", $"thread-toggle-{threadId}");
        await browser.OkAsync("click", $"thread-toggle-{threadId}");
        await browser.WaitUntilAsync(
            async () => (await browser.TextOfAsync($"thread-state-{threadId}")).Contains("Resolved", StringComparison.Ordinal),
            "the carried thread to be resolved");
        await reviewer.GotoAsync($"/projects/1/reviews/{followId}", "review-page");
        await Scenario.SignAsync(reviewer, slot);
        var followApproval = await reviewer.TextOfAsync("review-approval");

        // Act: the earlier review is as it was, and lists its follow-up
        await scenario.OpenReviewAsync();
        var earlierApproval = await browser.TextOfAsync("review-approval");
        var followedBy = await browser.ExistsAsync($"review-followed-by-link-{followId}");

        // Act: read the attestation of the follow-up with a token
        var token = await Scenario.CreateTokenAsync(reviewer);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(scenario.Server.Url) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var attestation = await http.GetAsync($"/api/projects/1/reviews/{followId}/attestation.pdf", TestContext.Current.CancellationToken);
        var pdf = System.Text.Encoding.Latin1.GetString(await attestation.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        // Assert: the form helped with the choice, and the follow-up names its source and what it took over
        Assert.True(hintShown, "A repository follow-up must say that what is carried is counted when the review is created.");
        Assert.Equal(scenario.ReviewId, followsLink);
        Assert.Contains("thread carried", carriedCounts, StringComparison.Ordinal);
        Assert.True(threadMarked, "A carried thread must be marked with the review it came from.");

        // Assert: the Pass and the section are carried and marked; the Fail is not, and is shown for a new decision
        Assert.True(passCarried);
        Assert.True(sectionCarried);
        Assert.False(failCarried, "A Fail must never be carried over.");
        Assert.False(failAnswered, "The failed check must be open again in the follow-up.");
        Assert.Contains("Failed in review", earlierFail, StringComparison.Ordinal);
        Assert.Contains("The requirement is not traceable.", earlierFail, StringComparison.Ordinal);
        Assert.False(earlierFailAfter, "Once the check is decided again the earlier Fail is no longer shown.");

        // Assert: the follow-up is approved on its own, and the earlier review stays in review and lists it
        Assert.Contains("Approved", followApproval, StringComparison.Ordinal);
        Assert.Contains("In review", earlierApproval, StringComparison.Ordinal);
        Assert.True(followedBy, "The earlier review must list its follow-up.");

        // Assert: the attestation of the follow-up says what it follows
        Assert.Equal(HttpStatusCode.OK, attestation.StatusCode);
        Assert.Contains("Follows review", pdf, StringComparison.Ordinal);
        Assert.Contains(scenario.ReviewId, pdf, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a reviewer who is named in a slot sees the review in the inbox as a signature to give, that the
    ///     home page counts it, that the item is gone once the reviewer has signed, and that reading and signing leave
    ///     no inbox or read-state event in the audit trail.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_Inbox_AssignedReviewer_SeesToSignItemAndHomeCount_UntilSigned()
    {
        // Arrange: a planned review by puppet with a finished checklist and a slot for second, who is signed in in a
        // browser of their own
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await using var reviewer = await scenario.StartSignedInBrowserAsync("reviewer", "second");
        await scenario.OpenReviewAsync();
        var slot = await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");

        // Act: the reviewer opens the home page, then the inbox
        await reviewer.OkAsync("goto", "/");
        await reviewer.OkAsync("wait", "home-inbox-count");
        var homeCount = await reviewer.TextOfAsync("home-inbox-count");
        await reviewer.GotoAsync("/inbox", "inbox-page");
        var summary = await reviewer.TextOfAsync("inbox-summary");
        var chip = await reviewer.TextOfAsync("inbox-chip-1");
        var rowReview = await reviewer.TextOfAsync("inbox-review-1");
        var inboxWhenEmpty = await reviewer.ExistsAsync("inbox-empty");

        // Act: the reviewer follows the link, signs, and looks at the inbox again
        await reviewer.OkAsync("click", "inbox-link-1");
        await reviewer.OkAsync(
            "wait", "css=[data-testid=review-page][data-interactive=true]", "--timeout", "30000");
        await reviewer.OkAsync("wait", $"sign-form-{slot}");
        await Scenario.SignAsync(reviewer, slot);
        await reviewer.GotoAsync("/inbox", "inbox-page");
        var inboxAfterSigning = await reviewer.ExistsAsync("inbox-empty");
        var rowAfterSigning = await reviewer.ExistsAsync("inbox-row-1");

        // Act: read the audit trail of the server
        await browser.GotoAsync("/audit", "audit-page");
        var trail = await browser.TextOfAsync("audit-page");

        // Assert: one signature was waiting, named by the home page and the inbox with the review
        Assert.Contains("1 needs your action", homeCount, StringComparison.Ordinal);
        Assert.Contains("1 needs your action", summary, StringComparison.Ordinal);
        Assert.Contains("Not signed", chip, StringComparison.Ordinal);
        Assert.Contains(scenario.ReviewId, rowReview, StringComparison.Ordinal);
        Assert.False(inboxWhenEmpty, "An inbox with a signature to give must not show the empty state.");

        // Assert: after signing nothing is left, and the read state is not part of the audit trail
        Assert.True(inboxAfterSigning, "The inbox must show the empty state once the review was signed.");
        Assert.False(rowAfterSigning, "A signed review must leave the to-sign group.");
        Assert.Contains("signature.added", trail, StringComparison.Ordinal);
        Assert.DoesNotContain("inbox", trail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("read-state", trail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Answers a check as Pass and waits until the answer is stored.
    /// </summary>
    /// <param name="browser">The browser, showing the checklist with the section of the check open.</param>
    /// <param name="check">The lower-case check identifier as used in test identifiers, for example <c>req-02</c>.</param>
    /// <returns>A task that completes when the answer is shown.</returns>
    private static async Task AnswerPassAsync(Browser browser, string check)
    {
        await browser.OkAsync("click", $"check-pass-{check}");
        await browser.WaitUntilAsync(() => browser.ExistsAsync($"check-answered-by-{check}"), $"the answer of {check}");
    }

    /// <summary>
    ///     Selects the file of a review whose path contains a text, so that its content and threads are shown.
    /// </summary>
    /// <param name="browser">The browser, showing the review page.</param>
    /// <param name="name">A part of the path of the file.</param>
    /// <returns>A task that completes when the file was clicked.</returns>
    private static async Task ClickFileAsync(Browser browser, string name)
    {
        await browser.OkAsync("wait", "file-select-0");
        await browser.OkAsync(
            "eval",
            $"document.querySelector('[data-testid^=\"file-select-\"][aria-label$=\"{name}\"]').click()");
    }

    /// <summary>
    ///     Reads a JSON document from the API.
    /// </summary>
    /// <param name="http">The client, with its credentials.</param>
    /// <param name="url">The address.</param>
    /// <returns>The root element of the answer, which must be a success.</returns>
    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string url)
    {
        using var response = await http.GetAsync(url, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} answered {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    ///     One Puppet browser session with some helpers to read and wait for the page.
    /// </summary>
    /// <param name="controlFile">The control file of the session.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    private sealed class Browser(string controlFile, CancellationToken cancellationToken) : IAsyncDisposable
    {
        /// <summary>
        ///     Runs one Puppet command.
        /// </summary>
        /// <param name="args">The command and its arguments.</param>
        /// <returns>The exit code, the output and the error.</returns>
        public async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await PuppetProgram.RunAsync(args, output, error, controlFile, cancellationToken);
            return (code, output.ToString(), error.ToString());
        }

        /// <summary>
        ///     Runs one Puppet command that must succeed.
        /// </summary>
        /// <param name="args">The command and its arguments.</param>
        /// <returns>The output of the command.</returns>
        public async Task<string> OkAsync(params string[] args)
        {
            var result = await RunAsync(args);
            Assert.True(result.Code == 0, $"puppet {string.Join(' ', args)} failed: {result.Error}{result.Output}");
            return result.Output;
        }

        /// <summary>
        ///     Evaluates a JavaScript expression in the page.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <returns>The value; a string is returned as it is, anything else as its JSON text.</returns>
        public async Task<string> EvalAsync(string expression)
        {
            var line = (await OkAsync("eval", expression)).Split('\n')[0].Trim();
            return line.StartsWith('"') ? JsonSerializer.Deserialize<string>(line) ?? string.Empty : line;
        }

        /// <summary>
        ///     Gets the text of an element.
        /// </summary>
        /// <param name="testId">The test identifier of the element.</param>
        /// <returns>The text, or an empty text when there is no such element.</returns>
        public Task<string> TextOfAsync(string testId)
        {
            return EvalAsync($"document.querySelector('[data-testid=\"{testId}\"]')?.textContent ?? ''");
        }

        /// <summary>
        ///     Tells whether an element exists.
        /// </summary>
        /// <param name="testId">The test identifier of the element.</param>
        /// <returns><c>true</c> when the page has the element.</returns>
        public async Task<bool> ExistsAsync(string testId)
        {
            return await EvalAsync($"!!document.querySelector('[data-testid=\"{testId}\"]')") == "true";
        }

        /// <summary>
        ///     Tells whether an element exists and is disabled.
        /// </summary>
        /// <param name="testId">The test identifier of the element.</param>
        /// <returns><c>true</c> when the page has the element and it is disabled.</returns>
        public async Task<bool> IsDisabledAsync(string testId)
        {
            return await EvalAsync($"document.querySelector('[data-testid=\"{testId}\"]')?.disabled === true") == "true";
        }

        /// <summary>
        ///     Counts the elements whose test identifier starts with a prefix.
        /// </summary>
        /// <param name="prefix">The start of the test identifier.</param>
        /// <returns>The number of elements.</returns>
        public async Task<int> CountAsync(string prefix)
        {
            return int.Parse(await EvalAsync($"document.querySelectorAll('[data-testid^=\"{prefix}\"]').length"), System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     Waits until a condition holds, for at most 20 seconds.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="what">What is waited for, for the message of the failure.</param>
        /// <returns>A task that completes when the condition holds.</returns>
        public async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                if (await condition())
                {
                    return;
                }

                await Task.Delay(100, cancellationToken);
            }

            Assert.Fail($"Timed out waiting for {what}.");
        }

        /// <summary>
        ///     Opens a page and waits until its live connection works.
        /// </summary>
        /// <param name="path">The address, relative to the server.</param>
        /// <param name="pageTestId">The test identifier of the page.</param>
        /// <returns>A task that completes when the page is interactive.</returns>
        public async Task GotoAsync(string path, string pageTestId)
        {
            await OkAsync("goto", path);
            await OkAsync("wait", $"css=[data-testid={pageTestId}][data-interactive=true]", "--timeout", "30000");
        }

        /// <summary>
        ///     Signs in at the login page.
        /// </summary>
        /// <param name="userName">The user name; every test user has the same password.</param>
        /// <returns>A task that completes when the user is signed in.</returns>
        public async Task SignInAsync(string userName)
        {
            await OkAsync("goto", "/login");
            await OkAsync("fill", "login-username-input", userName);
            await OkAsync("fill", "login-password-input", PuppetEndToEndTests.UserPassword);

            // The form posts to the server, which hashes the password; on a busy machine that can outlast the click
            // command, so do not require the click to report success and wait for the signed-in header instead
            await RunAsync("click", "login-button");
            await OkAsync("wait", "current-user", "--timeout", "60000");
        }

        /// <summary>
        ///     Stops the browser; a no-op when it has stopped already.
        /// </summary>
        /// <returns>A task that completes when the browser has stopped.</returns>
        public async ValueTask DisposeAsync()
        {
            await RunAsync("stop");
        }
    }

    /// <summary>
    ///     The server, a git repository with one review-set, a project with members and a planned review, and a signed-in browser.
    /// </summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private readonly PuppetEndToEndTests.TestServer _server;

        private readonly PuppetEndToEndTests.TemporaryFolder _folder;

        private readonly DirectoryInfo _directory;

        private readonly CancellationToken _cancellationToken;

        private Scenario(
            PuppetEndToEndTests.TestServer server,
            DirectoryInfo directory,
            Browser browser,
            CancellationToken cancellationToken)
        {
            _server = server;
            _directory = directory;
            _folder = new PuppetEndToEndTests.TemporaryFolder(directory);
            Browser = browser;
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        ///     Gets the server.
        /// </summary>
        public PuppetEndToEndTests.TestServer Server => _server;

        /// <summary>
        ///     Gets the browser of the administrator.
        /// </summary>
        public Browser Browser { get; }

        /// <summary>
        ///     Gets the identifier of the planned review.
        /// </summary>
        public string ReviewId { get; private set; } = string.Empty;

        /// <summary>
        ///     Gets the location of the git repository of the project.
        /// </summary>
        public string Repository { get; private init; } = string.Empty;

        /// <summary>
        ///     Creates the scenario; the test is skipped when git or the browser is not available.
        /// </summary>
        /// <param name="cancellationToken">Cancels the set-up.</param>
        /// <returns>The scenario, with the review planned and its page not yet open.</returns>
        public static async Task<Scenario> CreateAsync(CancellationToken cancellationToken)
        {
            if (!await PuppetEndToEndTests.IsGitAvailableAsync(cancellationToken))
            {
                Assert.Skip("git is not available.");
            }

            // A repository whose ReviewMark file defines one review-set over the docs folder
            var directory = Directory.CreateTempSubdirectory("reviewcenter-system-tests-");
            var repository = Path.Combine(directory.FullName, "repo");
            Directory.CreateDirectory(Path.Combine(repository, "docs"));
            await PuppetEndToEndTests.RunGitAsync(repository, cancellationToken, "init", "-b", "main");
            await File.WriteAllTextAsync(Path.Combine(repository, "docs", "guide.md"), "# Guide\n\nFirst line.\nSecond line.\n", cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(repository, ".reviewmark.yaml"),
                $"reviews:\n  - id: {SetId}\n    title: Documentation\n    paths:\n      - \"docs/**\"\n",
                cancellationToken);
            await PuppetEndToEndTests.RunGitAsync(repository, cancellationToken, "add", "-A");
            await PuppetEndToEndTests.RunGitAsync(repository, cancellationToken, "commit", "-m", "initial");

            var server = await PuppetEndToEndTests.TestServer.StartAsync(
                cancellationToken,
                seedUser: true,
                gitCachePath: Path.Combine(directory.FullName, "git-cache"),
                extraUsers: [("second", "Second Reviewer"), ("third", "Third Reviewer"), ("outsider", "Outside Person")]);
            var browser = new Browser(Path.Combine(directory.FullName, "control-main.json"), cancellationToken);
            var scenario = new Scenario(server, directory, browser, cancellationToken) { Repository = repository };
            try
            {
                var start = await PuppetEndToEndTests.StartPuppetAsync(args => browser.RunAsync(args), server.Url);
                Assert.True(start.Code == 0, start.Error);
                await scenario.ArrangeAsync(repository);
                return scenario;
            }
            catch
            {
                await scenario.DisposeAsync();
                throw;
            }
        }

        /// <summary>
        ///     Starts another browser session, not yet signed in.
        /// </summary>
        /// <param name="name">A name for the session.</param>
        /// <returns>The browser; disposing it stops it.</returns>
        public async Task<Browser> StartBrowserAsync(string name)
        {
            var browser = new Browser(Path.Combine(_directory.FullName, $"control-{name}.json"), _cancellationToken);
            var start = await PuppetEndToEndTests.StartPuppetAsync(args => browser.RunAsync(args), _server.Url);
            Assert.True(start.Code == 0, start.Error);
            return browser;
        }

        /// <summary>
        ///     Starts another browser session and signs a user in.
        /// </summary>
        /// <param name="name">A name for the session.</param>
        /// <param name="userName">The user name.</param>
        /// <returns>The browser; disposing it stops it.</returns>
        public async Task<Browser> StartSignedInBrowserAsync(string name, string userName)
        {
            var browser = await StartBrowserAsync(name);
            await browser.SignInAsync(userName);
            return browser;
        }

        /// <summary>
        ///     Opens the page of the planned review.
        /// </summary>
        /// <param name="browser">The browser to use, or <c>null</c> for the one of the administrator.</param>
        /// <returns>A task that completes when the page is interactive.</returns>
        public Task OpenReviewAsync(Browser? browser = null)
        {
            return (browser ?? Browser).GotoAsync($"/projects/1/reviews/{ReviewId}", "review-page");
        }

        /// <summary>
        ///     Signs in a slot with the right password and waits for the review to be approved.
        /// </summary>
        /// <param name="browser">The browser of the reviewer, showing the review.</param>
        /// <param name="slot">The identifier of the slot.</param>
        /// <returns>A task that completes when the review shows as approved.</returns>
        public static async Task SignAsync(Browser browser, string slot)
        {
            await browser.OkAsync("fill", $"sign-password-{slot}", PuppetEndToEndTests.UserPassword);
            await browser.OkAsync("click", $"sign-button-{slot}");
            await browser.WaitUntilAsync(
                async () => (await browser.TextOfAsync("review-approval")).Contains("Approved", StringComparison.Ordinal),
                "the approval");
        }

        /// <summary>
        ///     Gets the people that the form of a new slot offers as primary reviewer.
        /// </summary>
        /// <param name="browser">The browser, showing the review.</param>
        /// <returns>The labels of the options, separated by a bar.</returns>
        public static Task<string> OfferedReviewersAsync(Browser browser)
        {
            return browser.EvalAsync(
                "Array.from(document.querySelector('[data-testid=new-slot-primary-select]').options).map(o => o.textContent).join('|')");
        }

        /// <summary>
        ///     Switches the "require independent reviewers" rule of the project on or off on the project page.
        /// </summary>
        /// <param name="required">The new value.</param>
        /// <returns>The confirmation that the page shows.</returns>
        public async Task<string> SetIndependentReviewersAsync(bool required)
        {
            await Browser.GotoAsync("/projects/1", "project-page");
            await Browser.OkAsync(required ? "check" : "uncheck", "independent-reviewers-switch");
            await Browser.OkAsync("click", "independent-reviewers-save");
            await Browser.OkAsync("wait", "review-rules-status");
            return await Browser.TextOfAsync("review-rules-status");
        }

        /// <summary>
        ///     Adds a reviewer slot and waits for it.
        /// </summary>
        /// <param name="primary">The label of the primary reviewer in the list.</param>
        /// <param name="alternate">The label of the alternate reviewer, or <c>null</c> for none.</param>
        /// <returns>The identifier of the first slot of the review.</returns>
        public async Task<string> AddSlotAsync(string primary, string? alternate)
        {
            var before = await Browser.CountAsync("slot-status-");
            await FillSlotFormAsync(primary, alternate);
            await Browser.WaitUntilAsync(async () => await Browser.CountAsync("slot-status-") > before, "the new slot");
            return await Browser.EvalAsync(
                "document.querySelector('[data-testid^=\"slot-status-\"]').getAttribute('data-testid').replace('slot-status-', '')");
        }

        /// <summary>
        ///     Tries to add a reviewer slot that the tool must refuse.
        /// </summary>
        /// <param name="primary">The label of the primary reviewer in the list.</param>
        /// <param name="alternate">The label of the alternate reviewer, or <c>null</c> for none.</param>
        /// <returns>The error that the page shows; the number of slots must not have changed.</returns>
        public async Task<string> TryAddSlotAsync(string primary, string? alternate)
        {
            // Draw the page again, so that the message of an earlier refusal cannot be taken for this one
            await OpenReviewAsync();
            var before = await Browser.CountAsync("slot-status-");
            await FillSlotFormAsync(primary, alternate);

            // A refusal shows its message and leaves the slots as they are
            await Browser.WaitUntilAsync(async () => (await Browser.TextOfAsync("new-slot-error")).Length > 0, "the refusal of the slot");
            Assert.Equal(before, await Browser.CountAsync("slot-status-"));
            return await Browser.TextOfAsync("new-slot-error");
        }

        /// <summary>
        ///     Puts a text into the import box of the checklist template page and validates it.
        /// </summary>
        /// <param name="browser">The browser, showing the checklist template page.</param>
        /// <param name="json">The definition text.</param>
        /// <param name="valid">Whether the text is a valid definition, so which result to wait for.</param>
        /// <returns>A task that completes when the page shows the result of the validation.</returns>
        public static async Task ValidateChecklistTextAsync(Browser browser, string json, bool valid)
        {
            await browser.OkAsync("fill", "checklist-import-text", json);
            await browser.OkAsync("click", "checklist-validate");
            await browser.OkAsync("wait", valid ? "checklist-valid" : "checklist-validation-errors", "--timeout", "30000");
        }

        /// <summary>
        ///     Opens the checklist page of a review and reads which sections it has.
        /// </summary>
        /// <param name="browser">The browser to use.</param>
        /// <param name="reviewId">The identifier of the review.</param>
        /// <returns>The lower-case identifiers of the sections, in the order of the page.</returns>
        public static async Task<string[]> ReviewSectionIdsAsync(Browser browser, string reviewId)
        {
            await browser.GotoAsync($"/projects/1/reviews/{reviewId}/checklist", "checklist-page");
            var ids = await browser.EvalAsync(
                "Array.from(document.querySelectorAll('li[data-testid^=\"section-\"]')).map(e => e.getAttribute('data-testid').replace('section-', '')).join('|')");
            return ids.Length == 0 ? [] : ids.Split('|');
        }

        /// <summary>
        ///     Changes the documentation in the repository and plans a review of it, so that the project has one more review.
        /// </summary>
        /// <param name="dueDate">The due date to give every planned review (<c>yyyy-MM-dd</c>), or <c>null</c> for none.</param>
        /// <returns>The identifier of the new review.</returns>
        public async Task<string> PlanNewReviewAsync(string? dueDate = null)
        {
            await File.AppendAllTextAsync(Path.Combine(Repository, "docs", "guide.md"), "Third line.\n", _cancellationToken);
            await PuppetEndToEndTests.RunGitAsync(Repository, _cancellationToken, "commit", "-a", "-m", "second");
            await Browser.GotoAsync("/projects/1/plan", "plan-page");
            await Browser.OkAsync("select", "plan-repository-select", "1");
            await Browser.OkAsync("fill", "plan-head-input", "main");
            await Browser.OkAsync("click", "plan-load-button");
            await Browser.OkAsync("wait", $"plan-row-{SetId}", "--timeout", "30000");
            if (dueDate is not null)
            {
                await Browser.OkAsync("fill", "plan-default-due-input", dueDate);
            }

            await Browser.OkAsync("click", "plan-create-button");
            await Browser.OkAsync("wait", $"plan-result-link-{SetId}", "--timeout", "30000");
            var link = await Browser.EvalAsync($"document.querySelector('[data-testid=\"plan-result-link-{SetId}\"]').getAttribute('href')");
            return link[(link.LastIndexOf('/') + 1)..];
        }

        /// <summary>
        ///     Marks every section of the checklist as not applicable, with a reason.
        /// </summary>
        /// <param name="browser">The browser to use.</param>
        /// <param name="reason">The reason.</param>
        /// <param name="except">The lower-case identifier of a section to leave alone, or <c>null</c> for none.</param>
        /// <returns>A task that completes when every section is saved.</returns>
        public async Task MarkAllSectionsNotApplicableAsync(Browser browser, string reason, string? except = null)
        {
            await browser.GotoAsync($"/projects/1/reviews/{ReviewId}/checklist", "checklist-page");
            foreach (var section in Sections.Where(section => section != except))
            {
                await OpenSectionAsync(browser, section);
                await browser.OkAsync("uncheck", $"section-{section}-applicable");
                await browser.OkAsync("fill", $"section-{section}-reason", reason);
                await browser.OkAsync("click", $"section-{section}-save");
                await browser.WaitUntilAsync(
                    async () => (await browser.TextOfAsync($"section-{section}-progress")).Contains("Not applicable", StringComparison.Ordinal),
                    $"section {section} to be saved");
            }
        }

        /// <summary>
        ///     Opens a section of the checklist when it is closed.
        /// </summary>
        /// <param name="browser">The browser to use.</param>
        /// <param name="section">The lower-case section identifier.</param>
        /// <returns>A task that completes when the section is open.</returns>
        public static async Task OpenSectionAsync(Browser browser, string section)
        {
            var toggle = $"section-{section}-toggle";
            if (await browser.EvalAsync($"document.querySelector('[data-testid=\"{toggle}\"]').getAttribute('aria-expanded')") != "true")
            {
                await browser.OkAsync("click", toggle);
            }

            await browser.OkAsync("wait", $"section-{section}-applicable");
        }

        /// <summary>
        ///     Creates an API token of a signed-in user.
        /// </summary>
        /// <param name="browser">The browser of the user.</param>
        /// <returns>The value of the token.</returns>
        public static async Task<string> CreateTokenAsync(Browser browser)
        {
            await browser.GotoAsync("/account", "account-page");
            await browser.OkAsync("fill", "token-name-input", "system test");
            await browser.OkAsync("click", "create-token-button");
            await browser.OkAsync("wait", "new-token-value");
            return (await browser.TextOfAsync("new-token-value")).Trim();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync();
            _server.Dispose();
            _folder.Dispose();
        }

        /// <summary>
        ///     Chooses the reviewers in the form of a new slot and submits it.
        /// </summary>
        /// <param name="primary">The label of the primary reviewer in the list.</param>
        /// <param name="alternate">The label of the alternate reviewer, or <c>null</c> for none.</param>
        /// <returns>A task that completes when the form was submitted.</returns>
        private async Task FillSlotFormAsync(string primary, string? alternate)
        {
            await Browser.OkAsync("eval", "document.querySelector('[data-testid=slot-add-form] details').open = true");
            await Browser.OkAsync("select", "new-slot-primary-select", primary);
            await Browser.OkAsync("select", "new-slot-alternate-select", alternate ?? string.Empty);
            await Browser.OkAsync("click", "new-slot-add-button");
        }

        /// <summary>
        ///     Signs in, creates the project with its repository and members, and plans the review.
        /// </summary>
        /// <param name="repository">The location of the git repository.</param>
        /// <returns>A task that completes when the review exists.</returns>
        private async Task ArrangeAsync(string repository)
        {
            await Browser.SignInAsync(PuppetEndToEndTests.UserName);

            await Browser.GotoAsync("/projects/new", "new-project-page");
            await Browser.OkAsync("fill", "project-name-input", "System Project");
            await Browser.OkAsync("click", "create-project-button");
            await Browser.OkAsync("wait", "project-name");

            await Browser.GotoAsync("/projects/1/repositories/new", "new-repository-page");
            await Browser.OkAsync("fill", "new-repository-name-input", "system-repository");
            await Browser.OkAsync("fill", "new-repository-location-input", repository);
            await Browser.OkAsync("click", "create-repository-button");
            await Browser.OkAsync("wait", "repository-row-1");

            foreach (var member in new[] { PuppetEndToEndTests.UserName, "second", "third" })
            {
                await Browser.GotoAsync("/projects/1/members/new", "new-member-page");
                await Browser.OkAsync("fill", "new-member-username-input", member);
                await Browser.OkAsync("click", "new-member-submit");
                await Browser.OkAsync("wait", $"member-row-{member}");
            }

            await Browser.GotoAsync("/projects/1/plan", "plan-page");
            await Browser.OkAsync("select", "plan-repository-select", "1");
            await Browser.OkAsync("fill", "plan-head-input", "main");
            await Browser.OkAsync("click", "plan-load-button");
            await Browser.OkAsync("wait", $"plan-row-{SetId}", "--timeout", "30000");
            await Browser.OkAsync("click", "plan-create-button");
            await Browser.OkAsync("wait", $"plan-result-link-{SetId}", "--timeout", "30000");
            var link = await Browser.EvalAsync($"document.querySelector('[data-testid=\"plan-result-link-{SetId}\"]').getAttribute('href')");
            ReviewId = link[(link.LastIndexOf('/') + 1)..];
        }
    }
}
