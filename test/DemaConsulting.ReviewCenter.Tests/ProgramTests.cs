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

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Unit tests for the Program class.
/// </summary>
[Collection("Sequential")]
public class ProgramTests
{
    /// <summary>
    ///     Test that Run with version flag displays version only.
    /// </summary>
    [Fact]
    public void Program_Run_WithVersionFlag_DisplaysVersionOnly()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["--version"]);

            // Act: execute the operation being tested
            Program.Run(context);

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.Contains(Program.Version, output);
            Assert.DoesNotContain("Copyright", output);
            Assert.DoesNotContain("ReviewCenter version", output);
            Assert.Equal(0, context.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Run with help flag displays usage information.
    /// </summary>
    [Fact]
    public void Program_Run_WithHelpFlag_DisplaysUsageInformation()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["--help"]);

            // Act: execute the operation being tested
            Program.Run(context);

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.Contains("Usage:", output);
            Assert.Contains("Options:", output);
            Assert.Contains("--version", output);
            Assert.Contains("--help", output);
            Assert.Equal(0, context.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Run with validate flag runs validation.
    /// </summary>
    [Fact]
    public void Program_Run_WithValidateFlag_RunsValidation()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["--validate"]);

            // Act: execute the operation being tested
            Program.Run(context);

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.Contains("Total Tests:", output);
            Assert.Equal(0, context.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Run with no arguments displays default behavior.
    /// </summary>
    [Fact]
    public void Program_Run_NoArguments_DisplaysDefaultBehavior()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create([]);

            // Act: execute the operation being tested
            Program.Run(context);

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.Contains("ReviewCenter version", output);
            Assert.Contains("Copyright", output);
            Assert.Equal(0, context.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that version property returns non-empty version string.
    /// </summary>
    [Fact]
    public void Program_Version_ReturnsNonEmptyString()
    {
        // Act: execute the operation being tested
        var version = Program.Version;

        // Assert: verify expected behavior
        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    /// <summary>
    ///     Test that Main with invalid arguments returns non-zero exit code.
    /// </summary>
    [Fact]
    public void Program_Main_WithInvalidArgs_ReturnsNonZeroExitCode()
    {
        // Arrange: redirect stderr to suppress error output during test
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);

            // Act: invoke Main with an invalid argument
            var result = Program.Main(["--invalid-argument"]);

            // Assert: invalid arguments produce a non-zero exit code
            Assert.Equal(1, result);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test that Run with short version flag -v displays version.
    /// </summary>
    [Fact]
    public void Program_Run_WithShortVersionFlag_DisplaysVersion()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["-v"]);

            // Act: execute the operation being tested
            Program.Run(context);

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.Contains(Program.Version, output);
            Assert.Equal(0, context.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Run with short help flag -h displays usage.
    /// </summary>
    [Fact]
    public void Program_Run_WithShortHelpFlag_DisplaysUsage()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["-h"]);

            // Act: execute the operation being tested
            Program.Run(context);

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.Contains("Usage:", output);
            Assert.Contains("Options:", output);
            Assert.Equal(0, context.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Run with short help flag -? displays usage.
    /// </summary>
    [Fact]
    public void Program_Run_WithQuestionMarkFlag_DisplaysUsage()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["-?"]);

            // Act: execute the operation being tested
            Program.Run(context);

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.Contains("Usage:", output);
            Assert.Contains("Options:", output);
            Assert.Equal(0, context.ExitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that Run with the serve verb starts the web server and stops it when cancelled.
    /// </summary>
    [Fact]
    public async Task Program_Run_WithServeVerb_StartsServerAndStopsOnCancellation()
    {
        // Arrange: serve on a free loopback port, silent so no console noise
        var url = $"http://127.0.0.1:{ServerProcess.GetFreePort()}";
        using var context = Context.Create(["serve", "--urls", url, "--silent"]);
        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => Program.RunWithCancellation(context, cts.Token), TestContext.Current.CancellationToken);

        try
        {
            // Act: wait for the health endpoint to answer
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            string? body = null;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (body is null && DateTime.UtcNow < deadline && !serverTask.IsCompleted)
            {
                try
                {
                    body = await client.GetStringAsync($"{url}/health", TestContext.Current.CancellationToken);
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }
            }

            // Assert: the server answered while running
            Assert.Equal("Healthy", body);
        }
        finally
        {
            // Stop the server and wait for Run to return
            await cts.CancelAsync();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        // Assert: a clean stop is not an error
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test that Run with the serve verb reports an unusable address as an error instead of throwing.
    /// </summary>
    [Fact]
    public void Program_Run_WithServeVerb_PortInUse_ReportsError()
    {
        // Arrange: occupy a port so the server cannot bind it
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            using var context = Context.Create(["serve", "--urls", $"http://127.0.0.1:{port}", "--silent"]);

            // Act: try to start the server
            Program.Run(context);

            // Assert: failure is reported through the exit code, not an exception
            Assert.Equal(1, context.ExitCode);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    ///     Test that the help text documents the serve verb and the --urls option.
    /// </summary>
    [Fact]
    public void Program_Run_WithHelpFlag_ListsServeVerbAndUrlsOption()
    {
        // Arrange: capture console output
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["--help"]);

            // Act: print help
            Program.Run(context);

            // Assert: the serve verb and --urls option are documented
            var output = outWriter.ToString();
            Assert.Contains("serve", output);
            Assert.Contains("--urls", output);
            Assert.Contains("http://localhost:5000", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test that the help text documents the user add verb, its options, and where the password comes from.
    /// </summary>
    [Fact]
    public void Program_Run_WithHelpFlag_ListsUserAddVerbAndOptions()
    {
        // Arrange: capture console output
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["--help"]);

            // Act: print help
            Program.Run(context);

            // Assert: the verb, the options and the password sources are documented
            var output = outWriter.ToString();
            Assert.Contains("user add", output);
            Assert.Contains("--username", output);
            Assert.Contains("--display-name", output);
            Assert.Contains("--role", output);
            Assert.Contains("REVIEWCENTER_PASSWORD", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}

