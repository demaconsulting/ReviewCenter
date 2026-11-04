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

using DemaConsulting.ReviewCenter.Cli;

namespace DemaConsulting.ReviewCenter.Tests.Cli;

/// <summary>
///     Subsystem tests for the CLI subsystem covering Context and Program integration.
/// </summary>
[Collection("Sequential")]
public class CliSubsystemTests
{
    /// <summary>
    ///     Test that Context and Program work together to handle version flag workflow.
    /// </summary>
    [Fact]
    public void CliSubsystem_VersionFlow_ContextAndProgram_DisplaysVersionAndExits()
    {
        // Arrange: command line arguments with version flag; capture console output
        var args = new[] { "--version" };
        var originalOut = Console.Out;
        using var capturedOut = new StringWriter();

        try
        {
            Console.SetOut(capturedOut);

            // Act: create context and run program logic
            using var context = Context.Create(args);
            Program.Run(context);

            // Assert: version flag is parsed, version text is displayed, and exit code is success
            Assert.True(context.Version, "Context should parse version flag");
            Assert.Equal(0, context.ExitCode);
            Assert.Contains(Program.Version, capturedOut.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Context and Program work together to handle the -v short version flag.
    /// </summary>
    [Fact]
    public void CliSubsystem_VersionFlow_ContextAndProgram_DisplaysVersionAndExits_WithShortVFlag()
    {
        // Arrange: command line arguments with -v short version flag; capture console output
        var args = new[] { "-v" };
        var originalOut = Console.Out;
        using var capturedOut = new StringWriter();

        try
        {
            Console.SetOut(capturedOut);

            // Act: create context and run program logic
            using var context = Context.Create(args);
            Program.Run(context);

            // Assert: version flag is parsed, version text is displayed, and exit code is success
            Assert.True(context.Version, "Context should parse -v flag as version");
            Assert.Equal(0, context.ExitCode);
            Assert.Contains(Program.Version, capturedOut.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Context and Program work together to handle help flag workflow.
    /// </summary>
    [Fact]
    public void CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits()
    {
        // Arrange: command line arguments with help flag; capture console output
        var args = new[] { "--help" };
        var originalOut = Console.Out;
        using var capturedOut = new StringWriter();

        try
        {
            Console.SetOut(capturedOut);

            // Act: create context and run program logic
            using var context = Context.Create(args);
            Program.Run(context);

            // Assert: help flag is parsed, usage text is displayed, and exit code is success
            Assert.True(context.Help, "Context should parse help flag");
            Assert.Equal(0, context.ExitCode);
            var output = capturedOut.ToString();
            Assert.Contains("Usage:", output);
            Assert.Contains("Options:", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Context and Program work together to handle the -? short help flag.
    /// </summary>
    [Fact]
    public void CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits_WithShortQuestionFlag()
    {
        // Arrange: command line arguments with -? short help flag; capture console output
        var args = new[] { "-?" };
        var originalOut = Console.Out;
        using var capturedOut = new StringWriter();

        try
        {
            Console.SetOut(capturedOut);

            // Act: create context and run program logic
            using var context = Context.Create(args);
            Program.Run(context);

            // Assert: help flag is parsed, usage text is displayed, and exit code is success
            Assert.True(context.Help, "Context should parse -? flag as help");
            Assert.Equal(0, context.ExitCode);
            var output = capturedOut.ToString();
            Assert.Contains("Usage:", output);
            Assert.Contains("Options:", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Context and Program work together to handle the -h short help flag.
    /// </summary>
    [Fact]
    public void CliSubsystem_HelpFlow_ContextAndProgram_DisplaysHelpAndExits_WithShortHFlag()
    {
        // Arrange: command line arguments with -h short help flag; capture console output
        var args = new[] { "-h" };
        var originalOut = Console.Out;
        using var capturedOut = new StringWriter();

        try
        {
            Console.SetOut(capturedOut);

            // Act: create context and run program logic
            using var context = Context.Create(args);
            Program.Run(context);

            // Assert: help flag is parsed, usage text is displayed, and exit code is success
            Assert.True(context.Help, "Context should parse -h flag as help");
            Assert.Equal(0, context.ExitCode);
            var output = capturedOut.ToString();
            Assert.Contains("Usage:", output);
            Assert.Contains("Options:", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Context and Program work together to handle validation flag workflow.
    /// </summary>
    [Fact]
    public void CliSubsystem_ValidateFlow_ContextAndProgram_RunsValidationAndExits()
    {
        // Arrange: the shared real run: command line --validate through Context.Create and Program.Run

        // Act: read the outcome of the real run
        var run = RealValidationRun.Result;

        // Assert: validate flag is parsed, summary is output, and exit code is success
        Assert.True(run.Validate, "Context should parse validate flag");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Total Tests:", run.Log);
    }

    /// <summary>
    ///     Test that Context and Program work together to handle silent flag workflow.
    /// </summary>
    [Fact]
    public void CliSubsystem_SilentFlow_ContextAndProgram_SuppressesOutput()
    {
        // Arrange: command line arguments with version and silent flags; capture console streams
        var args = new[] { "--version", "--silent" };
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var capturedOut = new StringWriter();
        using var capturedError = new StringWriter();

        try
        {
            Console.SetOut(capturedOut);
            Console.SetError(capturedError);

            // Act: create context and run program logic
            using var context = Context.Create(args);
            Program.Run(context);

            // Assert: silent flag is parsed, exit code is success, and no console output is produced
            Assert.True(context.Silent, "Context should parse silent flag");
            Assert.Equal(0, context.ExitCode);
            Assert.Equal(string.Empty, capturedOut.ToString());
            Assert.Equal(string.Empty, capturedError.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test that Context and Program work together to handle results flag workflow.
    /// </summary>
    [Fact]
    public void CliSubsystem_ResultsFlow_ContextAndProgram_WritesResultsFile()
    {
        // Arrange: the shared real run, which asked for a TRX results file with --results

        // Act: read the outcome of the real run
        var run = RealValidationRun.Result;

        // Assert: validation ran with a successful exit code and the results file was written
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("<TestRun", run.Trx);
    }

    /// <summary>
    ///     Test that Context and Program work together to handle log flag workflow.
    /// </summary>
    [Fact]
    public void CliSubsystem_LogFlow_ContextAndProgram_WritesLogFile()
    {
        // Arrange: temporary log file path and version command with log output
        var tempDir = Path.GetTempPath();
        var logFile = Path.Combine(tempDir, $"cli_test_{Guid.NewGuid()}.log");
        var args = new[] { "--version", "--log", logFile };

        try
        {
            // Act: create context and run program logic
            using (var context = Context.Create(args))
            {
                Program.Run(context);

                // Assert: version flag is parsed and exit code is success
                Assert.True(context.Version, "Context should parse version flag");
                Assert.Equal(0, context.ExitCode);
            }

            // Assert: log file is written with version output
            Assert.True(File.Exists(logFile), "Log file should be created at specified path");
            var logContent = File.ReadAllText(logFile);
            Assert.False(string.IsNullOrWhiteSpace(logContent), "Log file should contain version output");
        }
        finally
        {
            // Cleanup
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    /// <summary>
    ///     Test that Program rejects unknown arguments, writes an error to stderr, and exits non-zero.
    /// </summary>
    [Fact]
    public void CliSubsystem_InvalidArgs_ContextAndProgram_RejectsUnknownArgumentsAndExitsNonZero()
    {
        // Arrange: unknown command-line argument
        var args = new[] { "--unknown-flag" };

        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);

            // Act: invoke the actual CLI entry point with an unknown flag
            var result = Program.Main(args);

            // Assert: invalid arguments produce a non-zero exit code and an error on stderr
            Assert.Equal(1, result);
            var errorOutput = errWriter.ToString();
            Assert.False(string.IsNullOrWhiteSpace(errorOutput), "Program should write an error to stderr for unknown arguments");
            Assert.Contains("--unknown-flag", errorOutput);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test that Program rejects a malformed option value, writes an error to stderr, and exits non-zero.
    /// </summary>
    /// <param name="arguments">The arguments, joined by a space.</param>
    /// <param name="option">The option that the error message must name.</param>
    [Theory]
    [InlineData("--depth abc", "--depth")]
    [InlineData("--depth 0", "--depth")]
    [InlineData("--depth 7", "--depth")]
    [InlineData("serve --urls not-a-url", "--urls")]
    [InlineData("serve --urls ftp://localhost:21", "--urls")]
    public void CliSubsystem_InvalidArgs_ContextAndProgram_RejectsMalformedValueAndExitsNonZero(
        string arguments,
        string option)
    {
        // Arrange: a malformed option value
        var args = arguments.Split(' ');

        // Act: invoke the actual CLI entry point with the malformed value
        var (result, errorOutput) = RunMainCapturingError(args);

        // Assert: the malformed value produces a non-zero exit code and an error on stderr that names the option
        Assert.Equal(1, result);
        Assert.Contains(option, errorOutput);
    }

    /// <summary>
    ///     Test that Program rejects an option whose value is missing, writes an error to stderr, and exits non-zero.
    /// </summary>
    /// <param name="arguments">The arguments, joined by a space; the last one is the option without its value.</param>
    /// <param name="option">The option that the error message must name.</param>
    [Theory]
    [InlineData("--log", "--log")]
    [InlineData("--results", "--results")]
    [InlineData("--result", "--result")]
    [InlineData("--depth", "--depth")]
    [InlineData("serve --urls", "--urls")]
    public void CliSubsystem_InvalidArgs_ContextAndProgram_RejectsMissingValueAndExitsNonZero(
        string arguments,
        string option)
    {
        // Arrange: an option at the end of the command line
        var args = arguments.Split(' ');

        // Act: invoke the actual CLI entry point without the value
        var (result, errorOutput) = RunMainCapturingError(args);

        // Assert: the missing value produces a non-zero exit code and an error on stderr that names the option
        Assert.Equal(1, result);
        Assert.Contains($"{option} requires", errorOutput);
    }

    /// <summary>
    ///     Runs the CLI entry point with the error stream captured.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code and the text written to the error stream.</returns>
    private static (int ExitCode, string ErrorOutput) RunMainCapturingError(string[] args)
    {
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);
            var result = Program.Main(args);
            return (result, errWriter.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test that Program reports an error and exits non-zero when the log file cannot be opened.
    /// </summary>
    [Fact]
    public void CliSubsystem_LogFlow_ContextAndProgram_UnopenableLogFile_ReportsErrorAndExitsNonZero()
    {
        // Arrange: a log path that is a directory, which cannot be opened as a file
        var args = new[] { "--version", "--log", Path.GetTempPath() };

        // Act: invoke the actual CLI entry point
        var (result, errorOutput) = RunMainCapturingError(args);

        // Assert: the run fails with an error on stderr that says the log file could not be opened
        Assert.Equal(1, result);
        Assert.Contains("Failed to open log file", errorOutput);
    }

    /// <summary>
    ///     Test that Context writes error messages to stderr.
    /// </summary>
    [Fact]
    public void CliSubsystem_ErrorOutput_ContextAndProgram_WritesErrorToStderr()
    {
        // Arrange: redirect stderr to capture error output
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);
            using var context = Context.Create([]);

            // Act: write an error message through the context
            context.WriteError("Test error message");

            // Assert: error is written to stderr and exit code reflects failure
            var errorOutput = errWriter.ToString();
            Assert.Contains("Test error message", errorOutput);
            Assert.Equal(1, context.ExitCode);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test that Context and Program work together to handle the --result legacy alias for results.
    /// </summary>
    [Fact]
    public void CliSubsystem_ResultAliasFlow_ContextAndProgram_ParsesResultsFile()
    {
        // Arrange: a results path given with the legacy --result alias; the alias is a pure parsing concern, and
        // the real process run of the integration tests writes its results file through it
        var resultsFile = Path.Combine(Path.GetTempPath(), $"cli_test_{Guid.NewGuid()}.trx");
        var args = new[] { "--validate", "--silent", "--result", resultsFile };

        // Act: create context from the command line
        using var context = Context.Create(args);

        // Assert: the legacy alias is parsed as the results file and nothing is written by parsing
        Assert.Equal(resultsFile, context.ResultsFile);
        Assert.True(context.Validate);
        Assert.Equal(0, context.ExitCode);
        Assert.False(File.Exists(resultsFile), "Parsing must not write the results file");
    }

    /// <summary>
    ///     Test that Context parses the --check-config option of a validation run when --validate is provided.
    /// </summary>
    [Fact]
    public void CliSubsystem_CheckConfigFlow_Context_WithValidate_IsParsed()
    {
        // Arrange: no setup required; the option together with --validate is the input under test

        // Act: create the context from the command line
        using var context = Context.Create(["--validate", "--check-config", "--silent"]);

        // Assert: both options are recorded
        Assert.True(context.Validate);
        Assert.True(context.CheckConfig);
    }

    /// <summary>
    ///     Test that Context rejects the --check-config option when --validate is not provided.
    /// </summary>
    [Fact]
    public void CliSubsystem_CheckConfigFlow_ContextAndProgram_RequiresValidate()
    {
        // Arrange: no setup required; the option without --validate is the input under test

        // Act: create the context from the command line
        var withoutValidate = Assert.Throws<ArgumentException>(() => Context.Create(["--check-config"]));

        // Assert: the option is rejected with a message that names both options
        Assert.Contains("--check-config requires --validate", withoutValidate.Message);
    }

    /// <summary>
    ///     Test that Context and Program work together to handle depth flag with self-validation.
    /// </summary>
    [Fact]
    public void CliSubsystem_DepthFlow_ContextAndProgram_AdjustsHeadingDepth()
    {
        // Arrange: the shared real run, which asked for heading depth 3 and a log file

        // Act: read the outcome of the real run
        var run = RealValidationRun.Result;

        // Assert: depth is parsed correctly and the log contains the level-3 heading
        Assert.Equal(RealValidationRun.HeadingDepth, run.HeadingDepthParsed);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("### DEMA Consulting", run.Log);
    }

    /// <summary>
    ///     Test that Context and Program work together to handle the serve verb and --urls option.
    /// </summary>
    [Fact]
    public async Task CliSubsystem_ServeFlow_ContextAndProgram_ServesOnRequestedUrlAndStops()
    {
        // Arrange: parse serve with an explicit loopback URL
        var url = $"http://127.0.0.1:{ServerProcess.GetFreePort()}";
        using var context = Context.Create(["serve", "--urls", url, "--silent"]);
        using var cts = new CancellationTokenSource();

        // Assert: parsing recorded the verb and the URL
        Assert.True(context.Serve, "Context should parse the serve verb");
        Assert.Equal(url, context.Urls);

        // Act: run the program on a background thread and wait for the health endpoint
        var serverTask = Task.Run(() => Program.RunWithCancellation(context, cts.Token), TestContext.Current.CancellationToken);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var healthy = false;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!healthy && DateTime.UtcNow < deadline && !serverTask.IsCompleted)
            {
                try
                {
                    healthy = (await client.GetAsync($"{url}/health", TestContext.Current.CancellationToken)).IsSuccessStatusCode;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }
            }

            // Assert: the server is reachable on the requested URL
            Assert.True(healthy, "Server should answer on the URL given to --urls");
        }
        finally
        {
            await cts.CancelAsync();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        // Assert: a requested stop is a clean exit
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test that Context rejects --urls without serve and Program is never reached.
    /// </summary>
    [Fact]
    public void CliSubsystem_UrlsWithoutServe_Context_RejectsArgumentCombination()
    {
        // Arrange: no setup required; the argument combination is the input under test

        // Act: parse --urls without serve
        var exception = Record.Exception(() => Context.Create(["--urls", "http://127.0.0.1:6001"]));

        // Assert: the combination is rejected during parsing with a message that names the serve verb
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("serve", argumentException.Message);
    }

    /// <summary>
    ///     Test that the user add verb passes through Context and Program: the options are recorded and the help
    ///     text documents them.
    /// </summary>
    [Fact]
    public void CliSubsystem_UserAddFlow_ContextAndProgram_ParsesOptionsAndDocumentsThem()
    {
        // Arrange: capture console output for the help text
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);

            // Act: parse the verb and print the help
            using var context = Context.Create(["user", "add", "--username", "ann", "--display-name", "Ann", "--system-admin"]);
            using var help = Context.Create(["--help"]);
            Program.Run(help);

            // Assert: the options are recorded and the help documents them
            Assert.True(context.UserAdd);
            Assert.Equal("ann", context.Username);
            Assert.Equal("Ann", context.DisplayName);
            Assert.True(context.SystemAdmin);
            var output = outWriter.ToString();
            Assert.Contains("user add", output);
            Assert.Contains("REVIEWCENTER_PASSWORD", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that the user add verb is rejected during parsing when a name is missing.
    /// </summary>
    [Fact]
    public void CliSubsystem_UserAddFlow_ContextAndProgram_MissingNames_AreRejected()
    {
        // Arrange: command lines without the user name, without the display name, and without both
        var missingNames = new[]
        {
            new[] { "user", "add" },
            new[] { "user", "add", "--username", "ann" },
            new[] { "user", "add", "--display-name", "Ann" }
        };

        // Act: parse each command line
        var errors = missingNames.Select(args => Record.Exception(() => Context.Create(args))).ToList();

        // Assert: every command line is refused with a message that names the two options
        Assert.Equal(missingNames.Length, errors.Count);
        Assert.All(errors, error =>
        {
            var argumentException = Assert.IsType<ArgumentException>(error);
            Assert.Contains("requires --username and --display-name", argumentException.Message);
        });
    }

    /// <summary>
    ///     Test that the user options are rejected during parsing when the user add verb is not given.
    /// </summary>
    [Fact]
    public void CliSubsystem_UserAddFlow_ContextAndProgram_OptionsWithoutVerb_AreRejected()
    {
        // Arrange: each of the three options without the verb
        var optionsWithoutVerb = new[]
        {
            new[] { "--username", "ann" },
            new[] { "--display-name", "Ann" },
            new[] { "--system-admin" }
        };

        // Act: parse each command line
        var errors = optionsWithoutVerb.Select(args => Record.Exception(() => Context.Create(args))).ToList();

        // Assert: every command line is refused with a message that names the missing verb
        Assert.Equal(optionsWithoutVerb.Length, errors.Count);
        Assert.All(errors, error =>
        {
            var argumentException = Assert.IsType<ArgumentException>(error);
            Assert.Contains("require the user add verb", argumentException.Message);
        });
    }

    /// <summary>
    ///     Test that a user verb without the add action, or with another action, is rejected with an error and a
    ///     non-zero exit code.
    /// </summary>
    /// <param name="arguments">The arguments, joined by a space.</param>
    [Theory]
    [InlineData("user")]
    [InlineData("user remove")]
    public void CliSubsystem_UserAddFlow_ContextAndProgram_WrongAction_RejectsAndExitsNonZero(string arguments)
    {
        // Arrange: capture the error stream
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);

            // Act: invoke the actual CLI entry point with the misused verb
            var result = Program.Main(arguments.Split(' '));

            // Assert: the run fails with an error that names the missing action
            Assert.Equal(1, result);
            Assert.Contains("user requires the add action", errWriter.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test that the user add verb without the system administrator switch requests an ordinary user.
    /// </summary>
    [Fact]
    public void CliSubsystem_UserAddFlow_Context_WithoutSwitch_RequestsOrdinaryUser()
    {
        // Arrange: no setup required; the argument list without the switch is the input under test

        // Act: parse the verb with both names and without the switch
        using var context = Context.Create(["user", "add", "--username", "ann", "--display-name", "Ann"]);

        // Assert: the verb is recorded and the user is not a system administrator
        Assert.True(context.UserAdd);
        Assert.False(context.SystemAdmin);
    }

    /// <summary>
    ///     Test that a password option of the user add verb is rejected and the run fails.
    /// </summary>
    [Fact]
    public void CliSubsystem_UserAddFlow_ContextAndProgram_PasswordOption_IsRejected()
    {
        // Arrange: a user add command line that tries to give the password as an option
        var args = new[] { "user", "add", "--username", "ann", "--display-name", "Ann", "--password", "secret-value" };
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);

            // Act: invoke the actual CLI entry point
            var result = Program.Main(args);

            // Assert: the option is unsupported, the run fails, and the password is never echoed
            Assert.Equal(1, result);
            var errorOutput = errWriter.ToString();
            Assert.Contains("Unsupported argument '--password'", errorOutput);
            Assert.DoesNotContain("secret-value", errorOutput);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test that the audit verify verb passes through Context and Program: it is recorded without any other
    ///     verb and the help text documents it.
    /// </summary>
    [Fact]
    public void CliSubsystem_AuditVerifyFlow_ContextAndProgram_ParsesVerbAndDocumentsIt()
    {
        // Arrange: capture console output for the help text
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);

            // Act: parse the verb and print the help
            using var context = Context.Create(["audit", "verify"]);
            using var help = Context.Create(["--help"]);
            Program.Run(help);

            // Assert: only the audit verify verb is recorded, and the help names it
            Assert.True(context.AuditVerify);
            Assert.False(context.KeysReprotect);
            Assert.False(context.UserAdd);
            Assert.False(context.Serve);
            Assert.Contains("audit verify", outWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that incorrect use of the audit verb is rejected with an error and a non-zero exit code.
    /// </summary>
    /// <param name="arguments">The arguments, joined by a space.</param>
    [Theory]
    [InlineData("audit")]
    [InlineData("audit export")]
    public void CliSubsystem_AuditVerifyFlow_ContextAndProgram_RejectsMisuseAndExitsNonZero(string arguments)
    {
        // Arrange: capture the error stream
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);

            // Act: invoke the actual CLI entry point with the misused verb
            var result = Program.Main(arguments.Split(' '));

            // Assert: the run fails with an error that names the missing action
            Assert.Equal(1, result);
            Assert.Contains("audit requires the verify action", errWriter.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test that the keys reprotect verb passes through Context and Program: it is recorded without any other
    ///     verb and the help text documents it.
    /// </summary>
    [Fact]
    public void CliSubsystem_KeysReprotectFlow_ContextAndProgram_ParsesVerbAndDocumentsIt()
    {
        // Arrange: capture console output for the help text
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);

            // Act: parse the verb and print the help
            using var context = Context.Create(["keys", "reprotect"]);
            using var help = Context.Create(["--help"]);
            Program.Run(help);

            // Assert: only the keys reprotect verb is recorded, and the help names it
            Assert.True(context.KeysReprotect);
            Assert.False(context.AuditVerify);
            Assert.False(context.UserAdd);
            Assert.False(context.Serve);
            Assert.Contains("keys reprotect", outWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that incorrect use of the keys verb is rejected with an error and a non-zero exit code.
    /// </summary>
    /// <param name="arguments">The arguments, joined by a space.</param>
    [Theory]
    [InlineData("keys")]
    [InlineData("keys rotate")]
    public void CliSubsystem_KeysReprotectFlow_ContextAndProgram_RejectsMisuseAndExitsNonZero(string arguments)
    {
        // Arrange: capture the error stream
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);

            // Act: invoke the actual CLI entry point with the misused verb
            var result = Program.Main(arguments.Split(' '));

            // Assert: the run fails with an error that names the missing action
            Assert.Equal(1, result);
            Assert.Contains("keys requires the reprotect action", errWriter.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
