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

using System.Runtime.InteropServices;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the <see cref="DaemonLauncher"/>.
/// </summary>
[Collection("Sequential")]
public class DaemonLauncherTests
{
    /// <summary>
    ///     Test that the identity a launch reports is the identity of the process that was actually created:
    ///     the process store accepts it as alive, and the store's identity-checked kill ends exactly that process.
    /// </summary>
    [Fact]
    public async Task DaemonLauncher_LaunchProcess_ReportsIdentityOfTheCreatedProcess()
    {
        // Arrange: a real long-running executable to launch
        var (executable, arguments) = OperatingSystem.IsWindows()
            ? (Path.Combine(Environment.SystemDirectory, "ping.exe"), new List<string> { "-n", "60", "127.0.0.1" })
            : ("/bin/sleep", new List<string> { "60" });
        var identity = default(ProcessIdentity);
        try
        {
            // Act: launch it, then check and kill it using only the reported identity
            identity = DaemonLauncher.LaunchProcess(executable, arguments);
            var aliveAfterLaunch = SessionStore.IsProcessAlive(identity.ProcessId, identity.StartTimeUtc);
            SessionStore.KillProcess(identity.ProcessId, identity.StartTimeUtc);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (SessionStore.IsProcessAlive(identity.ProcessId, identity.StartTimeUtc) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, Xunit.TestContext.Current.CancellationToken);
            }

            // Assert: the identity matched the live process, and the kill by identity ended it
            Assert.True(aliveAfterLaunch);
            Assert.False(SessionStore.IsProcessAlive(identity.ProcessId, identity.StartTimeUtc));
        }
        finally
        {
            if (identity.ProcessId != 0)
            {
                SessionStore.KillProcess(identity.ProcessId, identity.StartTimeUtc);
            }
        }
    }

    /// <summary>
    ///     Test that a launch whose identity cannot be read ends the process it just created and still reports
    ///     the original failure: a running daemon without a recorded identity could never be cleaned up.
    /// </summary>
    [Fact]
    public void DaemonLauncher_CaptureIdentity_IdentityCannotBeRead_EndsCreatedProcessAndRethrows()
    {
        // Arrange: a capture that fails, and a terminate action that records that it was used
        var terminated = false;

        // Act: capture the identity
        var thrown = Assert.Throws<PuppetException>(() => DaemonLauncher.CaptureIdentity(
            () => throw new PuppetException("start time unreadable"),
            () => terminated = true));

        // Assert: the process was ended and the caller sees the original failure
        Assert.True(terminated);
        Assert.Equal("start time unreadable", thrown.Message);
    }

    /// <summary>
    ///     Test that a failing termination (the process is already gone) does not replace the original failure,
    ///     and that a successful capture does not end the process.
    /// </summary>
    [Fact]
    public void DaemonLauncher_CaptureIdentity_TerminationFailsOrNotNeeded_OriginalOutcomeIsKept()
    {
        // Arrange: a process that is already gone, so ending it fails the way a vanished process does
        var identity = new ProcessIdentity(1234, DateTime.UtcNow);
        var terminatedOnSuccess = false;

        // Act: a failing capture with a failing termination, then a successful capture
        var thrown = Assert.Throws<PuppetException>(() => DaemonLauncher.CaptureIdentity(
            () => throw new PuppetException("start time unreadable"),
            () => throw new InvalidOperationException("already exited")));
        var captured = DaemonLauncher.CaptureIdentity(() => identity, () => terminatedOnSuccess = true);

        // Assert: the capture failure survived, and a good capture left the process alone
        Assert.Equal("start time unreadable", thrown.Message);
        Assert.Equal(identity, captured);
        Assert.False(terminatedOnSuccess);
    }

    /// <summary>
    ///     Test that a partial tree kill during identity capture is reported, because descendants may still be
    ///     running with no identity to clean them up by.
    /// </summary>
    [Fact]
    public void DaemonLauncher_CaptureIdentity_PartialTreeKill_IsReportedWithTheOriginalReason()
    {
        // Arrange: a capture that fails, and a termination that ends the root but not a descendant
        var partial = new AggregateException(new InvalidOperationException("child could not be ended"));

        // Act: capture the identity
        var thrown = Assert.Throws<PuppetException>(() => DaemonLauncher.CaptureIdentity(
            () => throw new PuppetException("start time unreadable"),
            () => throw partial));

        // Assert: the report keeps the original reason and says that child processes may remain
        Assert.Contains("start time unreadable", thrown.Message);
        Assert.Contains("child processes", thrown.Message);
        Assert.Same(partial, thrown.InnerException);
    }

    /// <summary>
    ///     Test that every argument is quoted in the Windows command line.
    /// </summary>
    [Fact]
    public void DaemonLauncher_BuildCommandLine_PathsWithSpaces_AreQuoted()
    {
        // Arrange: an executable and arguments containing spaces
        const string executable = "C:\\Program Files\\Puppet\\puppet.exe";
        string[] arguments = ["__daemon", "--session", "demo", "--server", "C:\\a b\\server.dll"];

        // Act: build the command line
        var commandLine = DaemonLauncher.BuildCommandLine(executable, arguments);

        // Assert: each part is quoted
        Assert.Equal(
            "\"C:\\Program Files\\Puppet\\puppet.exe\" \"__daemon\" \"--session\" \"demo\" \"--server\" \"C:\\a b\\server.dll\"",
            commandLine);
    }

    /// <summary>
    ///     Test that an embedded quote cannot split an argument in two.
    /// </summary>
    [Fact]
    public void DaemonLauncher_BuildCommandLine_EmbeddedQuote_IsEscaped()
    {
        // Arrange: an argument with a quote and an injected option
        string[] arguments = ["a\" --evil \"b"];

        // Act: build the command line
        var commandLine = DaemonLauncher.BuildCommandLine("p.exe", arguments);

        // Assert: the quote is escaped
        Assert.Equal("\"p.exe\" \"a\\\" --evil \\\"b\"", commandLine);
    }

    /// <summary>
    ///     Test that arguments with spaces, quotes, and trailing backslashes are read back unchanged when Windows
    ///     splits the command line into arguments, which is what the daemon process does when it starts.
    /// </summary>
    /// <param name="argument">The argument that must survive the round trip.</param>
    [Theory]
    [InlineData("C:\\a b\\server.dll")]
    [InlineData("C:\\folder with spaces\\")]
    [InlineData("C:\\folder\\\\")]
    [InlineData("say \"hello\" there")]
    [InlineData("quote then backslash \\\"")]
    [InlineData("a\" --evil \"b")]
    [InlineData("")]
    public void DaemonLauncher_BuildCommandLine_SpecialArguments_RoundTripThroughWindowsParsing(string argument)
    {
        // Arrange: a command line built for an executable and the argument (Windows rules do not apply elsewhere)
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows command-line parsing rules only exist on Windows.");
        var commandLine = DaemonLauncher.BuildCommandLine("C:\\Program Files\\Puppet\\puppet.exe", ["__daemon", argument, "last"]);

        // Act: split it the way Windows splits the command line of a starting process
        var parsed = WindowsCommandLine.Split(commandLine);

        // Assert: executable, verb, the argument itself, and the following argument all arrive unchanged
        Assert.Equal(["C:\\Program Files\\Puppet\\puppet.exe", "__daemon", argument, "last"], parsed);
    }
}

/// <summary>
///     Splits a command line into arguments with the operating system's own parser.
/// </summary>
internal static partial class WindowsCommandLine
{
    /// <summary>
    ///     Splits a command line into arguments exactly as Windows does.
    /// </summary>
    /// <param name="commandLine">The command line text.</param>
    /// <returns>The arguments, starting with the program name.</returns>
    public static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero)
        {
            throw new InvalidOperationException("The command line could not be split.");
        }

        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? string.Empty;
            }

            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    /// <summary>
    ///     Windows' command-line splitter.
    /// </summary>
    /// <param name="commandLine">The command line text.</param>
    /// <param name="argumentCount">Receives the number of arguments.</param>
    /// <returns>A pointer to the array of argument pointers; free it with <see cref="LocalFree"/>.</returns>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    /// <summary>
    ///     Frees memory allocated by the operating system.
    /// </summary>
    /// <param name="memory">The memory to free.</param>
    /// <returns>Null on success.</returns>
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
