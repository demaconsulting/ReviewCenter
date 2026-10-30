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
///     binding, record immutability, evidence and attestation requirements of the system. Like the other Puppet tests
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
        // Arrange: a planned review in a project whose members are puppet and second; outsider is not a member
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await scenario.OpenReviewAsync();

        // Act: read who is offered, and add slots
        var offered = await browser.EvalAsync(
            "Array.from(document.querySelector('[data-testid=new-slot-primary-select]').options).map(o => o.textContent).join('|')");
        await scenario.AddSlotAsync("Puppet Admin (puppet)", alternate: null);
        var samePerson = await scenario.TryAddSlotAsync("Puppet Admin (puppet)", alternate: null);
        var alternateInOtherSlot = await scenario.TryAddSlotAsync("Second Reviewer (second)", "Puppet Admin (puppet)");
        var slotsBefore = await browser.CountAsync("slot-status-");
        await scenario.AddSlotAsync("Second Reviewer (second)", alternate: null);
        var slotsAfter = await browser.CountAsync("slot-status-");

        // Assert: a member can be named, a non-member is not offered, and nobody fills two slots
        Assert.Contains("(puppet)", offered, StringComparison.Ordinal);
        Assert.Contains("(second)", offered, StringComparison.Ordinal);
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
        // Arrange: a planned review with a slot for puppet, whose checklist is not answered yet, and a second
        // browser session of the same person
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await scenario.OpenReviewAsync();
        var slot = await scenario.AddSlotAsync("Puppet Admin (puppet)", alternate: null);
        var blockedText = await browser.TextOfAsync($"sign-blockers-{slot}");
        var blockedForm = await browser.ExistsAsync($"sign-form-{slot}");

        // Act: answer the checklist, then try a wrong password
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");
        await scenario.OpenReviewAsync();
        var formWhenReady = await browser.ExistsAsync($"sign-form-{slot}");
        await browser.OkAsync("fill", $"sign-password-{slot}", "this is not the password");
        await browser.OkAsync("click", $"sign-button-{slot}");
        await browser.WaitUntilAsync(() => browser.ExistsAsync($"sign-error-{slot}"), "the error of the wrong password");
        var wrongError = await browser.TextOfAsync($"sign-error-{slot}");
        var approvalAfterWrong = await browser.TextOfAsync("review-approval");
        var statusAfterWrong = await browser.TextOfAsync($"slot-status-{slot}");

        // Act: the same person changes the record in another session while this page still shows the old one
        await using var other = await scenario.StartBrowserAsync("other");
        await other.SignInAsync(PuppetEndToEndTests.UserName);
        await other.GotoAsync($"/projects/1/reviews/{scenario.ReviewId}/checklist", "checklist-page");
        await Scenario.OpenSectionAsync(other, "req");
        await other.OkAsync("fill", "section-req-reason", "Changed while the other page was open.");
        await other.OkAsync("click", "section-req-save");
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        await other.WaitUntilAsync(
            async () =>
            {
                await other.GotoAsync($"/projects/1/reviews/{scenario.ReviewId}/checklist", "checklist-page");
                await Scenario.OpenSectionAsync(other, "req");
                return (await other.EvalAsync("document.querySelector('[data-testid=section-req-reason]').value"))
                    .Contains("Changed while", StringComparison.Ordinal);
            },
            "the changed reason to be stored");

        // Act: sign on the stale page with the right password
        await browser.OkAsync("fill", $"sign-password-{slot}", PuppetEndToEndTests.UserPassword);
        await browser.OkAsync("click", $"sign-button-{slot}");
        await browser.WaitUntilAsync(
            async () => (await browser.TextOfAsync($"sign-error-{slot}")).Contains("changed", StringComparison.Ordinal),
            "the refusal of the stale page");
        var approvalAfterStale = await browser.TextOfAsync("review-approval");

        // Act: reload the page, so that it shows the record as it is now, and sign with the right password
        await scenario.OpenReviewAsync();
        await browser.OkAsync("fill", $"sign-password-{slot}", PuppetEndToEndTests.UserPassword);
        await browser.OkAsync("click", $"sign-button-{slot}");
        await browser.WaitUntilAsync(
            async () => (await browser.TextOfAsync("review-approval")).Contains("Approved", StringComparison.Ordinal),
            "the approval");

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
    ///     later comment makes the approval out of date without removing the signature or changing the reviewed
    ///     content, that no posted comment can be edited, and that a link to a thread survives the sign-in.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_ApprovedReview_EvidenceTokenAndLaterChange_BehaveAsPromised()
    {
        // Arrange: an approved review, and a personal API token of the signer
        await using var scenario = await Scenario.CreateAsync(TestContext.Current.CancellationToken);
        var browser = scenario.Browser;
        await scenario.OpenReviewAsync();
        var slot = await scenario.AddSlotAsync("Puppet Admin (puppet)", alternate: null);
        await scenario.MarkAllSectionsNotApplicableAsync(browser, "Not relevant to this system test.");
        var token = await scenario.CreateTokenAsync();
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
        await scenario.OpenReviewAsync();
        await browser.OkAsync("fill", $"sign-password-{slot}", PuppetEndToEndTests.UserPassword);
        await browser.OkAsync("click", $"sign-button-{slot}");
        await browser.WaitUntilAsync(
            async () => (await browser.TextOfAsync("review-approval")).Contains("Approved", StringComparison.Ordinal),
            "the approval");
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
        var slotCanChange = await browser.ExistsAsync($"slot-remove-{slot}") || await browser.ExistsAsync($"slot-save-{slot}");

        // Act: add a comment after the approval
        await browser.OkAsync("click", "file-select-0");
        await browser.OkAsync("wait", "new-thread-form");
        await browser.OkAsync("fill", "thread-text-input", "A comment after the approval.");
        await browser.OkAsync("click", "start-thread-button");
        await browser.OkAsync("wait", "css=[data-testid^=thread-state-]");
        await browser.WaitUntilAsync(
            async () => (await browser.TextOfAsync("review-approval")).Contains("In review", StringComparison.Ordinal),
            "the approval to go out of date");
        var changed = await GetJsonAsync(http, reviewUrl);
        var attestationAfter = await http.GetAsync(attestationUrl, TestContext.Current.CancellationToken);
        var indexAfter = await GetJsonAsync(http, indexUrl);
        var commentControls = await browser.EvalAsync("document.querySelectorAll('.comment button, .comment textarea, .comment input').length");
        var slotCanChangeAfter = await browser.ExistsAsync($"slot-remove-{slot}") || await browser.ExistsAsync($"slot-save-{slot}");
        var threadPath = await browser.EvalAsync(
            "document.querySelector('[data-testid^=\"thread-state-\"]').getAttribute('data-testid').replace('thread-state-', '')");

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
        Assert.Equal("Puppet Admin", signed.GetProperty("slots")[0].GetProperty("signedBy").GetString());
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

        // Assert: a later comment ends the approval, keeps the signature as history and the content as it was
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
                extraUsers: [("second", "Second Reviewer"), ("outsider", "Outside Person")]);
            var browser = new Browser(Path.Combine(directory.FullName, "control-main.json"), cancellationToken);
            var scenario = new Scenario(server, directory, browser, cancellationToken);
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
        ///     Opens the page of the planned review.
        /// </summary>
        /// <returns>A task that completes when the page is interactive.</returns>
        public Task OpenReviewAsync()
        {
            return Browser.GotoAsync($"/projects/1/reviews/{ReviewId}", "review-page");
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
            var before = await Browser.CountAsync("slot-status-");
            await FillSlotFormAsync(primary, alternate);

            // A refusal leaves the page as it is, so give the server a moment and then check that nothing was added
            await Task.Delay(1000, _cancellationToken);
            Assert.Equal(before, await Browser.CountAsync("slot-status-"));
            return await Browser.TextOfAsync("new-slot-error");
        }

        /// <summary>
        ///     Marks every section of the checklist as not applicable, with a reason.
        /// </summary>
        /// <param name="browser">The browser to use.</param>
        /// <param name="reason">The reason.</param>
        /// <returns>A task that completes when every section is saved.</returns>
        public async Task MarkAllSectionsNotApplicableAsync(Browser browser, string reason)
        {
            await browser.GotoAsync($"/projects/1/reviews/{ReviewId}/checklist", "checklist-page");
            foreach (var section in Sections)
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
        ///     Creates an API token of the signed-in user.
        /// </summary>
        /// <returns>The value of the token.</returns>
        public async Task<string> CreateTokenAsync()
        {
            await Browser.GotoAsync("/account", "account-page");
            await Browser.OkAsync("fill", "token-name-input", "system test");
            await Browser.OkAsync("click", "create-token-button");
            await Browser.OkAsync("wait", "new-token-value");
            return (await Browser.TextOfAsync("new-token-value")).Trim();
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

            foreach (var member in new[] { PuppetEndToEndTests.UserName, "second" })
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
