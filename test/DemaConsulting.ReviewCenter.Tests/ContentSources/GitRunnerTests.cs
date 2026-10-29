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
using System.Net;
using System.Net.Sockets;
using System.Text;
using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.ContentSources;

/// <summary>
///     Tests for <see cref="GitRunner"/>: how git is started (no shell, curated environment, the token only in the
///     environment), redaction, time limits and the clear errors for a missing or unusable git.
/// </summary>
public class GitRunnerTests
{
    /// <summary>
    ///     A token used to check that it never leaks.
    /// </summary>
    private const string Secret = "tkn-S3cr3t.value";

    /// <summary>
    ///     A user name used with the token.
    /// </summary>
    private const string User = "svc-user";

    /// <summary>
    ///     Creates the options with a cache folder and a short time limit.
    /// </summary>
    /// <param name="cache">The cache folder.</param>
    /// <param name="timeoutSeconds">The time limit.</param>
    /// <returns>The options.</returns>
    private static GitOptions Options(string cache, int timeoutSeconds = 30) =>
        new() { CachePath = cache, TimeoutSeconds = timeoutSeconds };

    /// <summary>
    ///     Test that the argument list is used (no shell string), the credentials are not in the arguments and the
    ///     token reaches git only through the environment of the child process.
    /// </summary>
    [Fact]
    public void GitRunner_BuildStartInfo_TokenOnlyInEnvironment()
    {
        // Arrange: a runner and access with credentials
        using var cache = new TempFolder();
        var runner = new GitRunner(Options(cache.Path));
        var access = new RepositoryAccess(1, "repo", "https://example.com/org/repo.git", User, Secret);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:{Secret}"));

        // Act: build the process description of a fetch
        var info = runner.BuildStartInfo(["fetch", "--prune", "origin"], access);

        // Assert: no shell, no prompt, arguments in a list, and the secrets only in the environment
        Assert.False(info.UseShellExecute);
        Assert.Equal(string.Empty, info.Arguments);
        var arguments = string.Join(' ', info.ArgumentList);
        Assert.DoesNotContain(Secret, arguments, StringComparison.Ordinal);
        Assert.DoesNotContain(base64, arguments, StringComparison.Ordinal);
        Assert.DoesNotContain($"{User}:{Secret}", arguments, StringComparison.Ordinal);
        Assert.Contains("fetch", info.ArgumentList);
        Assert.Equal("0", info.Environment["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", info.Environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("http.https://example.com/org/repo.git.extraHeader", info.Environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal("Authorization: Basic " + base64, info.Environment["GIT_CONFIG_VALUE_0"]);
        Assert.DoesNotContain(info.Environment, pair => pair.Key.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal) && pair.Value!.Contains(Secret, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that no credential variables are set for anonymous access and that the environment is curated: git
    ///     cannot read the system or user configuration and no askpass program runs.
    /// </summary>
    [Fact]
    public void GitRunner_BuildStartInfo_Anonymous_HasCuratedEnvironment()
    {
        // Arrange: a runner and anonymous access
        using var cache = new TempFolder();
        var runner = new GitRunner(Options(cache.Path));
        var access = new RepositoryAccess(1, "repo", "https://example.com/org/repo.git", null, null);

        // Act: build the process description
        var info = runner.BuildStartInfo(["ls-remote", "origin"], access);

        // Assert: no credentials, and the curated settings are present
        Assert.False(info.Environment.ContainsKey("GIT_CONFIG_COUNT"));
        Assert.DoesNotContain(info.Environment, pair => pair.Value?.Contains("Authorization", StringComparison.Ordinal) == true);
        Assert.Equal("0", info.Environment["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", info.Environment["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal(string.Empty, info.Environment["GIT_ASKPASS"]);
        Assert.Equal("file:http:https", info.Environment["GIT_ALLOW_PROTOCOL"]);
        Assert.StartsWith(Path.GetFullPath(cache.Path), info.Environment["HOME"], StringComparison.Ordinal);
        Assert.Contains("-c", info.ArgumentList);
        Assert.Contains("protocol.ext.allow=never", info.ArgumentList);
        Assert.Contains("credential.helper=", info.ArgumentList);
        Assert.DoesNotContain(info.Environment, pair => pair.Key.StartsWith("GIT_DIR", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a secret is removed from text in every form git might print it: the token, the user and
    ///     token, the base64 header value and the user information of a URL.
    /// </summary>
    /// <param name="text">The text git printed.</param>
    [Theory]
    [InlineData("fatal: bad token tkn-S3cr3t.value here")]
    [InlineData("remote: svc-user:tkn-S3cr3t.value rejected")]
    [InlineData("Authorization: Basic c3ZjLXVzZXI6dGtuLVMzY3IzdC52YWx1ZQ==")]
    [InlineData("fatal: unable to access 'https://svc-user:tkn-S3cr3t.value@example.com/r.git/'")]
    [InlineData("fatal: unable to access 'https://other:pw@example.com/r.git/'")]
    public void GitRunner_Redact_RemovesEverySecretForm(string text)
    {
        // Arrange: nothing beyond the text

        // Act: redact it
        var redacted = GitRunner.Redact(text, User, Secret);

        // Assert: no secret remains
        Assert.DoesNotContain(Secret, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("c3ZjLXVzZXI6dGtu", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("pw@", redacted, StringComparison.Ordinal);
        Assert.Contains("***", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a URL-escaped token is redacted too, and that missing text or secrets are handled.
    /// </summary>
    [Fact]
    public void GitRunner_Redact_EdgeCases()
    {
        // Arrange: a token with characters that a URL escapes
        const string token = "a b/c+d";

        // Act: redact the escaped and raw forms, null text and no credentials
        var escaped = GitRunner.Redact($"url {Uri.EscapeDataString(token)} end", User, token);
        var raw = GitRunner.Redact($"url {token} end", User, token);
        var none = GitRunner.Redact(null, User, token);
        var anonymous = GitRunner.Redact("plain text", null, null);

        // Assert: secrets are masked, null becomes empty and plain text is untouched
        Assert.DoesNotContain("a%20b", escaped, StringComparison.Ordinal);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
        Assert.Equal(string.Empty, none);
        Assert.Equal("plain text", anonymous);
    }

    /// <summary>
    ///     Test that a secret that crosses the 8 KiB cut of the standard error is redacted before the cut, so no part
    ///     of it (neither the part before the cut nor the part after it) is left in the result.
    /// </summary>
    [Fact]
    public void GitRunner_BuildError_SecretAcrossTheCut_LeavesNoFragment()
    {
        // Arrange: more than 8 KiB of text with the token starting before the cut and ending after it
        const string token = "Zq7Rk9Wm2Xv4Bn8Lp3Hd";
        var text = new string('a', 3000) + token + new string('b', 8192 - token.Length / 2);
        var captured = new CapturedError(Encoding.UTF8.GetBytes(text), false);

        // Act: build the error text
        var error = GitRunner.BuildError(captured, User, token, null, null);

        // Assert: the cut is inside the token, the result is at most 8 KiB, and no fragment of 4 or more characters remains
        Assert.True(Encoding.UTF8.GetByteCount(error) <= 8192);
        Assert.Contains("***", error, StringComparison.Ordinal);
        for (var length = 4; length <= token.Length; length++)
        {
            for (var start = 0; start + length <= token.Length; start++)
            {
                Assert.DoesNotContain(token.Substring(start, length), error, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    ///     Test that a long standard error, read through the bounded buffer, never leaves a fragment of a secret that
    ///     was cut at the start of the buffer, even when redaction makes the text much shorter than the cap.
    /// </summary>
    [Fact]
    public async Task GitRunner_ReadTailAsync_SecretCutAtBufferStart_LeavesNoFragment()
    {
        // Arrange: about 200 KiB made of a 1000-character token repeated, so the buffer start lands inside one
        var random = new Random(7);
        var token = new string(Enumerable.Range(0, 1000).Select(_ => (char)('A' + random.Next(26))).ToArray());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join(' ', Enumerable.Repeat(token, 200))));

        // Act: read the tail and build the error text
        var captured = await GitRunner.ReadTailAsync(stream, TestContext.Current.CancellationToken);
        var error = GitRunner.BuildError(captured, User, token, null, null);

        // Assert: earlier text was dropped, and only masks and separators are left
        Assert.True(captured.Dropped);
        Assert.NotEmpty(error);
        Assert.All(error, c => Assert.True(c is '*' or ' '));
    }

    /// <summary>
    ///     Test that failures are described without secrets and authentication failures get a clear message.
    /// </summary>
    [Fact]
    public void GitRunner_FailureFor_DescribesFailuresClearly()
    {
        // Arrange: access and two failed results
        var access = new RepositoryAccess(1, "my-repo", "https://example.com/r.git", User, Secret);
        var denied = new GitResult(128, [], "fatal: Authentication failed for 'https://example.com/r.git/'");
        var notFound = new GitResult(128, [], "fatal: repository 'https://example.com/r.git/' not found\nmore");
        var empty = new GitResult(3, [], string.Empty);
        var longText = new GitResult(1, [], new string('x', 1000));

        // Act: describe them
        var deniedError = GitRunner.FailureFor(denied, access);
        var notFoundError = GitRunner.FailureFor(notFound, access);
        var emptyError = GitRunner.FailureFor(empty, access);
        var longError = GitRunner.FailureFor(longText, access);

        // Assert: the repository is named, the first line is used and the length is limited
        Assert.Equal(DomainRule.GitAccessFailed, deniedError.Rule);
        Assert.Contains("'my-repo'", deniedError.Message, StringComparison.Ordinal);
        Assert.Contains("denied", deniedError.Message, StringComparison.Ordinal);
        Assert.Contains("not found", notFoundError.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("more", notFoundError.Message, StringComparison.Ordinal);
        Assert.Contains("code 3", emptyError.Message, StringComparison.Ordinal);
        Assert.True(longError.Message.Length < 400);
    }

    /// <summary>
    ///     Test that the location of the repository is replaced by its name in every form git prints it: as
    ///     registered, with a trailing slash or <c>.git</c> added or removed, with either kind of slash for a local
    ///     path, inside quotes, with user information, and as the host name in a connection error.
    /// </summary>
    /// <param name="location">The registered location.</param>
    /// <param name="text">The text git printed.</param>
    /// <param name="leaked">A part of the location that must not remain.</param>
    [Theory]
    [InlineData("https://example.com/org/repo.git", "fatal: repository 'https://example.com/org/repo.git/' not found", "example.com")]
    [InlineData("https://example.com/org/repo.git", "fatal: unable to access 'https://example.com/org/repo/': error: 500", "org/repo")]
    [InlineData("https://example.com/org/repo", "fatal: repository \"https://example.com/org/repo.git\" not found", "org/repo")]
    [InlineData("https://example.com/org/repo.git", "fatal: unable to access 'https://example.com/org/repo.git/': Could not resolve host: example.com", "example.com")]
    [InlineData("HTTPS://Example.COM/Org/Repo.git", "fatal: repository 'https://example.com/org/repo.git/' not found", "example.com")]
    [InlineData("http://localhost:8080/r.git", "fatal: unable to access 'http://localhost:8080/r.git/': Failed to connect to localhost port 8080", "localhost")]
    [InlineData("https://example.com/org/repo.git", "fatal: unable to access 'https://***@example.com/org/repo.git/': x", "example.com")]
    [InlineData("https://bob:pw@example.com/org/repo.git", "fatal: unable to access 'https://bob:pw@example.com/org/repo.git/': x", "example.com")]
    [InlineData("https://bob:pw@example.com/org/repo.git", "fatal: unable to access 'https://bob:<redacted>@example.com/org/repo.git/': x", "example.com")]
    [InlineData("https://bob:pw@example.com/org/repo.git", "fatal: unable to access 'https://example.com/org/repo/': x", "example.com")]
    [InlineData(@"C:\srv\git\repo.git", @"fatal: 'C:\srv\git\repo.git' does not appear to be a git repository", "srv")]
    [InlineData(@"C:\srv\git\repo.git", "fatal: 'C:/srv/git/repo.git' does not appear to be a git repository", "srv")]
    [InlineData(@"C:\srv\git\repo.git", "fatal: 'C:/srv/git/repo' does not appear to be a git repository", "srv")]
    [InlineData("/srv/git/repo.git", "fatal: '/srv/git/repo.git/' does not appear to be a git repository", "srv")]
    [InlineData("/srv/git/repo.git", "fatal: repository '/srv/git/repo' does not exist", "srv")]
    [InlineData("file:///srv/git/repo.git", "fatal: repository 'file:///srv/git/repo.git/' does not exist", "srv")]
    [InlineData("file:///C:/srv/git/repo.git", "fatal: repository 'file:///C:/srv/git/repo.git/' x C:\\srv\\git\\repo.git", "srv")]
    public void GitRunner_Redact_ReplacesLocationWithName(string location, string text, string leaked)
    {
        // Arrange: nothing beyond the registered location and the text

        // Act: redact the text for a repository named "my-repo"
        var redacted = GitRunner.Redact(text, null, null, location, "my-repo");

        // Assert: the name stands where the location was, and nothing of the location remains
        Assert.Contains("my-repo", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(leaked, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pw@", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("<redacted>", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("bob", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a location is left alone when none is given, and that text without the location is unchanged.
    /// </summary>
    [Fact]
    public void GitRunner_Redact_NoLocation_LeavesTextAlone()
    {
        // Arrange: text that names a location

        // Act: redact it without and with an unrelated location
        var without = GitRunner.Redact("fatal: repository 'https://example.com/r.git/' not found", null, null);
        var unrelated = GitRunner.Redact("fatal: something else", null, null, "https://example.com/r.git", "my-repo");

        // Assert: nothing was replaced
        Assert.Contains("https://example.com/r.git", without, StringComparison.Ordinal);
        Assert.Equal("fatal: something else", unrelated);
    }

    /// <summary>
    ///     Test that a failure message names the repository and never shows its location, for a URL, a local path
    ///     and the denied-access message, even when the result was not redacted by the runner.
    /// </summary>
    /// <param name="location">The registered location.</param>
    /// <param name="error">The raw error text of git.</param>
    [Theory]
    [InlineData("https://example.com/org/r.git", "fatal: repository 'https://example.com/org/r.git/' not found")]
    [InlineData("https://example.com/org/r.git", "fatal: unable to access 'https://example.com/org/r.git/': Could not resolve host: example.com")]
    [InlineData("https://example.com/org/r.git", "fatal: Authentication failed for 'https://example.com/org/r.git/'")]
    [InlineData(@"C:\srv\git\r.git", "fatal: 'C:/srv/git/r.git' does not appear to be a git repository")]
    [InlineData("/srv/git/r.git", "fatal: '/srv/git/r.git' does not appear to be a git repository")]
    public void GitRunner_FailureFor_NeverContainsLocation(string location, string error)
    {
        // Arrange: access to a named repository and a failed result with the raw error
        var access = new RepositoryAccess(1, "my-repo", location, User, Secret);
        var result = new GitResult(128, [], error);

        // Act: describe the failure
        var exception = GitRunner.FailureFor(result, access);

        // Assert: the repository is named and no part of the location is in the message
        Assert.Contains("'my-repo'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("srv", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("org/r", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a real git call on an unknown local repository gives a message with the repository name and
    ///     no part of the local path, whichever way git prints the path.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunCheckedAsync_UnknownLocalRepository_MessageHasNameNotPath()
    {
        // Arrange: a runner and access to a local path that does not exist
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var cache = new TempFolder();
        var runner = new GitRunner(Options(cache.Path));
        var location = Path.Combine(cache.Path, "no-such-folder", "repo.git");
        var access = new RepositoryAccess(1, "unknown-repo", location, null, null);

        // Act: list the remote refs of the missing repository
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunCheckedAsync(["ls-remote", location], access, 1024, TestContext.Current.CancellationToken));

        // Assert: the message names the repository and has no part of the path
        Assert.Equal(DomainRule.GitAccessFailed, exception.Rule);
        Assert.Contains("'unknown-repo'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(cache.Path), exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no-such-folder", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a real git call that cannot connect to a web address gives a message with the repository name
    ///     and neither the address nor the host name.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunCheckedAsync_UnreachableUrl_MessageHasNameNotAddress()
    {
        // Arrange: a runner and access to a loopback address where nothing listens
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var cache = new TempFolder();
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        var runner = new GitRunner(Options(cache.Path));
        var location = $"http://127.0.0.1:{port}/secret-org/secret-repo.git";
        var access = new RepositoryAccess(1, "unreachable-repo", location, null, null);

        // Act: list the remote refs of the unreachable address
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunCheckedAsync(["ls-remote", location], access, 1024, TestContext.Current.CancellationToken));

        // Assert: the message names the repository and has no part of the address
        Assert.Equal(DomainRule.GitAccessFailed, exception.Rule);
        Assert.Contains("'unreachable-repo'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a missing git executable gives the clear "git is not available" error.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunAsync_MissingGit_ThrowsGitUnavailable()
    {
        // Arrange: a runner that starts an executable that does not exist
        using var cache = new TempFolder();
        var runner = new GitRunner(Options(cache.Path), "rc-no-such-git-executable");

        // Act: run any command
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunAsync(["status"], null, 1024, TestContext.Current.CancellationToken));

        // Assert: the rule is named and the message tells the administrator what to do
        Assert.Equal(DomainRule.GitUnavailable, exception.Rule);
        Assert.Contains("git", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a git older than the minimum version is refused as unavailable.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunAsync_OldGit_ThrowsGitUnavailable()
    {
        // Arrange: a fake git executable that reports an old version
        using var cache = new TempFolder();
        var script = OperatingSystem.IsWindows()
            ? Path.Combine(cache.Path, "old-git.cmd")
            : Path.Combine(cache.Path, "old-git.sh");
        await File.WriteAllTextAsync(
            script,
            OperatingSystem.IsWindows() ? "@echo git version 2.30.0\r\n" : "#!/bin/sh\necho git version 2.30.0\n",
            TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var runner = new GitRunner(Options(cache.Path), script);

        // Act: run any command
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunAsync(["status"], null, 1024, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.GitUnavailable, exception.Rule);
    }

    /// <summary>
    ///     Test that checking the version returns the version text and makes the first command skip its own check.
    /// </summary>
    [Fact]
    public async Task GitRunner_CheckVersionAsync_SupportedGit_ReturnsVersionAndIsNotRepeatedByRunAsync()
    {
        // Arrange: a fake git executable that reports a supported version and counts its invocations
        using var cache = new TempFolder();
        var counter = Path.Combine(cache.Path, "count.txt");
        var script = OperatingSystem.IsWindows()
            ? Path.Combine(cache.Path, "counting-git.cmd")
            : Path.Combine(cache.Path, "counting-git.sh");
        await File.WriteAllTextAsync(
            script,
            OperatingSystem.IsWindows()
                ? "@echo x>>\"%~dp0count.txt\"\r\n@echo git version 2.40.0\r\n"
                : "#!/bin/sh\necho x >> \"$(dirname \"$0\")/count.txt\"\necho git version 2.40.0\n",
            TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var runner = new GitRunner(Options(cache.Path), script);

        // Act: check the version, then run one command
        var version = await runner.CheckVersionAsync(TestContext.Current.CancellationToken);
        await runner.RunAsync(["status"], null, 1024, TestContext.Current.CancellationToken);

        // Assert: the text is returned and git ran twice (the check and the command), not three times
        Assert.Equal("git version 2.40.0", version);
        Assert.Equal(2, (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken)).Length);
    }

    /// <summary>
    ///     Test that checking the version refuses a git older than the minimum.
    /// </summary>
    [Fact]
    public async Task GitRunner_CheckVersionAsync_OldGit_ThrowsGitUnavailable()
    {
        // Arrange: a fake git executable that reports an old version
        using var cache = new TempFolder();
        var script = OperatingSystem.IsWindows()
            ? Path.Combine(cache.Path, "old-git2.cmd")
            : Path.Combine(cache.Path, "old-git2.sh");
        await File.WriteAllTextAsync(
            script,
            OperatingSystem.IsWindows() ? "@echo git version 2.30.0\r\n" : "#!/bin/sh\necho git version 2.30.0\n",
            TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var runner = new GitRunner(Options(cache.Path), script);

        // Act and assert: the check itself fails with the rule that names git as unavailable
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.CheckVersionAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DomainRule.GitUnavailable, exception.Rule);
    }

    /// <summary>
    ///     Test that a real git call returns its output and a failing call returns its exit code and error text.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunAsync_RealGit_ReturnsOutputAndExitCode()
    {
        // Arrange: a runner over the installed git
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var cache = new TempFolder();
        var runner = new GitRunner(Options(cache.Path));
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: ask for the version and run a command that fails
        var version = await runner.RunAsync(["--version"], null, 1024, cancellationToken);
        var failure = await runner.RunAsync(["rev-parse", "--git-dir", Path.Combine(cache.Path, "missing")], null, 1024, cancellationToken);

        // Assert: output on success, a non-zero code and text on failure
        Assert.Equal(0, version.ExitCode);
        Assert.StartsWith("git version", Encoding.UTF8.GetString(version.Output), StringComparison.Ordinal);
        Assert.NotEqual(0, failure.ExitCode);
        Assert.NotEmpty(failure.Error);
    }

    /// <summary>
    ///     Test that output beyond the cap stops the call with an error instead of filling memory.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunAsync_OutputOverCap_ThrowsGitAccessFailed()
    {
        // Arrange: a runner over the installed git and a cap smaller than its version text
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var cache = new TempFolder();
        var runner = new GitRunner(Options(cache.Path));

        // Act: run a command whose output is larger than the cap
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunAsync(["help", "-a"], null, 10, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.GitAccessFailed, exception.Rule);
    }

    /// <summary>
    ///     Test that a home folder that cannot be created gives the administrator-level error without the path.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunAsync_HomeFolderNotWritable_ThrowsGitUnavailable()
    {
        // Arrange: a cache path below a regular file, which can never be created
        using var root = new TempFolder();
        var blocker = Path.Combine(root.Path, "blocker");
        await File.WriteAllTextAsync(blocker, "not a folder", TestContext.Current.CancellationToken);
        var cache = Path.Combine(blocker, "cache");
        var runner = new GitRunner(Options(cache));

        // Act: run any git call
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunAsync(["--version"], null, 4096, TestContext.Current.CancellationToken));

        // Assert: the rule and the setting are named, the path is not and no inner exception is chained
        Assert.Equal(DomainRule.GitUnavailable, exception.Rule);
        Assert.Contains("Git:CachePath", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cache, exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    /// <summary>
    ///     Test that a cache path that is malformed (a null character) or too long for the file system gives the
    ///     administrator-level error without the path, not an unhandled exception.
    /// </summary>
    /// <param name="kind">Which malformed path to use.</param>
    [Theory]
    [InlineData("null-character")]
    [InlineData("too-long-name")]
    public async Task GitRunner_RunAsync_MalformedCachePath_ThrowsGitUnavailable(string kind)
    {
        // Arrange: a cache path that no platform accepts
        using var root = new TempFolder();
        var cache = kind == "null-character"
            ? Path.Combine(root.Path, "bad\0cache")
            : Path.Combine(root.Path, new string('x', 2000));
        var runner = new GitRunner(Options(cache));

        // Act: run any git call
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunAsync(["--version"], null, 4096, TestContext.Current.CancellationToken));

        // Assert: the rule and the setting are named, the path is not and no inner exception is chained
        Assert.Equal(DomainRule.GitUnavailable, exception.Rule);
        Assert.Contains("Git:CachePath", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cache, exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    /// <summary>
    ///     Test that a call that never finishes is stopped at the time limit with the timeout error.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunAsync_ServerThatNeverAnswers_ThrowsGitTimeoutAndStopsGit()
    {
        // Arrange: a server that accepts connections and never answers, and a one-second limit
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var cache = new TempFolder();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = new List<TcpClient>();
        using var stop = new CancellationTokenSource();
        var acceptLoop = Task.Run(
            async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        accepted.Add(await listener.AcceptTcpClientAsync(stop.Token));
                    }
                }
                catch (OperationCanceledException)
                {
                    // The test is over
                }
            },
            CancellationToken.None);
        var runner = new GitRunner(Options(cache.Path, 1));
        var access = new RepositoryAccess(1, "slow", $"http://127.0.0.1:{port}/r.git", null, null);
        var timer = Stopwatch.StartNew();

        // Act: list the remote refs
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunAsync(["ls-remote", access.Location], access, 1024, TestContext.Current.CancellationToken));
        timer.Stop();
        await stop.CancelAsync();
        listener.Stop();
        await acceptLoop;
        foreach (var client in accepted)
        {
            client.Dispose();
        }

        // Assert: the timeout rule is named and the call ended soon after the limit
        Assert.Equal(DomainRule.GitTimeout, exception.Rule);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), $"The call took {timer.Elapsed}.");
    }

    /// <summary>
    ///     Test that git sends the token as a basic authorization header to the server, that the server's refusal
    ///     is reported as a denied access, and that neither the token nor its encodings appear in the error.
    /// </summary>
    [Fact]
    public async Task GitRunner_RunCheckedAsync_RefusedCredentials_SendsHeaderAndHidesSecret()
    {
        // Arrange: a server that records the request and answers 401, and access with credentials
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var cache = new TempFolder();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = new List<string>();
        using var stop = new CancellationTokenSource();
        var serveLoop = Task.Run(
            async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        using var client = await listener.AcceptTcpClientAsync(stop.Token);
                        var stream = client.GetStream();
                        var buffer = new byte[8192];
                        var read = await stream.ReadAsync(buffer, stop.Token);
                        lock (requests)
                        {
                            requests.Add(Encoding.ASCII.GetString(buffer, 0, read));
                        }

                        var response = "HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), stop.Token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
                {
                    // The test is over
                }
            },
            CancellationToken.None);
        var runner = new GitRunner(Options(cache.Path));
        var location = $"http://127.0.0.1:{port}/org/r.git";
        var access = new RepositoryAccess(1, "private", location, User, Secret);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:{Secret}"));

        // Act: list the remote refs with the credentials
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => runner.RunCheckedAsync(["ls-remote", location], access, 1024, TestContext.Current.CancellationToken));
        await stop.CancelAsync();
        listener.Stop();
        await serveLoop;

        // Assert: git sent the header, the refusal is a denied access and no secret is in the message
        string[] seen;
        lock (requests)
        {
            seen = [.. requests];
        }

        Assert.Contains(seen, request => request.Contains("Authorization: Basic " + base64, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(DomainRule.GitAccessFailed, exception.Rule);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(base64, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.ToString(), StringComparison.Ordinal);
    }
}
