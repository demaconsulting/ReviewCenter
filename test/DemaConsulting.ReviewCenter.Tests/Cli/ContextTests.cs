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
///     Unit tests for the Context class.
/// </summary>
[Collection("Sequential")]
public class ContextTests
{
    /// <summary>
    ///     Test creating a context with no arguments.
    /// </summary>
    [Fact]
    public void Context_Create_NoArguments_ReturnsDefaultContext()
    {
        // Act: execute the operation being tested
        using var context = Context.Create([]);

        // Assert: verify expected behavior
        Assert.False(context.Version);
        Assert.False(context.Help);
        Assert.False(context.Silent);
        Assert.False(context.Validate);
        Assert.Null(context.ResultsFile);
        Assert.Equal(1, context.HeadingDepth);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the version flag.
    /// </summary>
    [Fact]
    public void Context_Create_VersionFlag_SetsVersionTrue()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["--version"]);

        // Assert: verify expected behavior
        Assert.True(context.Version);
        Assert.False(context.Help);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the short version flag.
    /// </summary>
    [Fact]
    public void Context_Create_ShortVersionFlag_SetsVersionTrue()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["-v"]);

        // Assert: verify expected behavior
        Assert.True(context.Version);
        Assert.False(context.Help);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the help flag.
    /// </summary>
    [Fact]
    public void Context_Create_HelpFlag_SetsHelpTrue()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["--help"]);

        // Assert: verify expected behavior
        Assert.False(context.Version);
        Assert.True(context.Help);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the short help flag -h.
    /// </summary>
    [Fact]
    public void Context_Create_ShortHelpFlag_H_SetsHelpTrue()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["-h"]);

        // Assert: verify expected behavior
        Assert.False(context.Version);
        Assert.True(context.Help);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the short help flag -?.
    /// </summary>
    [Fact]
    public void Context_Create_ShortHelpFlag_Question_SetsHelpTrue()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["-?"]);

        // Assert: verify expected behavior
        Assert.False(context.Version);
        Assert.True(context.Help);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the silent flag.
    /// </summary>
    [Fact]
    public void Context_Create_SilentFlag_SetsSilentTrue()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["--silent"]);

        // Assert: verify expected behavior
        Assert.True(context.Silent);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the validate flag.
    /// </summary>
    [Fact]
    public void Context_Create_ValidateFlag_SetsValidateTrue()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["--validate"]);

        // Assert: verify expected behavior
        Assert.True(context.Validate);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the results flag.
    /// </summary>
    [Fact]
    public void Context_Create_ResultsFlag_SetsResultsFile()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["--results", "test.trx"]);

        // Assert: verify expected behavior
        Assert.Equal("test.trx", context.ResultsFile);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with the log flag.
    /// </summary>
    [Fact]
    public void Context_Create_LogFlag_OpensLogFile()
    {
        // Arrange: setup test conditions
        var logFile = Path.GetTempFileName();
        try
        {
            // Act: execute the operation being tested
            using (var context = Context.Create(["--log", logFile]))
            {
                context.WriteLine("Test message");
                Assert.Equal(0, context.ExitCode);
            }

            // Assert: verify expected behavior
            // Verify log file was written
            Assert.True(File.Exists(logFile));
            var logContent = File.ReadAllText(logFile);
            Assert.Contains("Test message", logContent);
        }
        finally
        {
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    /// <summary>
    ///     Test creating a context with the serve verb enables the serve action.
    /// </summary>
    [Fact]
    public void Context_Create_ServeVerb_SetsServe()
    {
        // Act: parse the serve verb
        using var context = Context.Create(["serve"]);

        // Assert: serve is requested and the default URL applies
        Assert.True(context.Serve);
        Assert.Equal(Context.DefaultUrls, context.Urls);
    }

    /// <summary>
    ///     Test that without the serve verb the serve action is off.
    /// </summary>
    [Fact]
    public void Context_Create_NoServeVerb_SetsServeFalse()
    {
        // Act: parse a context with no verb
        using var context = Context.Create([]);

        // Assert: serve is not requested
        Assert.False(context.Serve);
    }

    /// <summary>
    ///     Test creating a context with --urls records the requested URL.
    /// </summary>
    [Fact]
    public void Context_Create_UrlsFlag_SetsUrls()
    {
        // Act: parse serve with an explicit URL
        using var context = Context.Create(["serve", "--urls", "http://127.0.0.1:6001"]);

        // Assert: the URL is recorded
        Assert.Equal("http://127.0.0.1:6001", context.Urls);
    }

    /// <summary>
    ///     Test that --urls without the serve verb is rejected.
    /// </summary>
    [Fact]
    public void Context_Create_UrlsWithoutServe_ThrowsArgumentException()
    {
        // Arrange: no setup required; the argument combination is the input under test

        // Act: parse --urls without serve
        var exception = Record.Exception(() => Context.Create(["--urls", "http://127.0.0.1:6001"]));

        // Assert: --urls only makes sense with serve
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("serve", argumentException.Message);
    }

    /// <summary>
    ///     Test that --urls without a value is rejected.
    /// </summary>
    [Fact]
    public void Context_Create_UrlsFlag_WithoutValue_ThrowsArgumentException()
    {
        // Arrange: no setup required; the arguments are the input under test

        // Act: parse --urls without a value
        var exception = Record.Exception(() => Context.Create(["serve", "--urls"]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--urls", argumentException.Message);
    }

    /// <summary>
    ///     Test that --urls with a value that is not an absolute http or https URL is rejected.
    /// </summary>
    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://localhost:21")]
    public void Context_Create_InvalidUrls_ThrowsArgumentException(string value)
    {
        // Arrange: no setup required; the invalid URL value is the input under test

        // Act: parse --urls with an invalid value
        var exception = Record.Exception(() => Context.Create(["serve", "--urls", value]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--urls", argumentException.Message);
    }

    /// <summary>
    ///     Test creating a context with an unknown argument throws exception.
    /// </summary>
    [Fact]
    public void Context_Create_UnknownArgument_ThrowsArgumentException()
    {
        // Arrange: no setup required; the unknown argument is the input under test

        // Act: parse an unknown argument
        var exception = Record.Exception(() => Context.Create(["--unknown"]));

        // Assert: an ArgumentException reporting the unsupported argument is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("Unsupported argument", argumentException.Message);
    }

    /// <summary>
    ///     Test creating a context with --log flag but no value throws exception.
    /// </summary>
    [Fact]
    public void Context_Create_LogFlag_WithoutValue_ThrowsArgumentException()
    {
        // Arrange: no setup required; the arguments are the input under test

        // Act: parse --log without a value
        var exception = Record.Exception(() => Context.Create(["--log"]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--log", argumentException.Message);
    }

    /// <summary>
    ///     Test creating a context with --results flag but no value throws exception.
    /// </summary>
    [Fact]
    public void Context_Create_ResultsFlag_WithoutValue_ThrowsArgumentException()
    {
        // Arrange: no setup required; the arguments are the input under test

        // Act: parse --results without a value
        var exception = Record.Exception(() => Context.Create(["--results"]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--results", argumentException.Message);
    }

    /// <summary>
    ///     Test creating a context with the --result alias flag (legacy alias for --results).
    /// </summary>
    [Fact]
    public void Context_Create_ResultAliasFlag_SetsResultsFile()
    {
        // Act: execute the operation using the legacy --result alias
        using var context = Context.Create(["--result", "test.trx"]);

        // Assert: verify --result sets ResultsFile identically to --results
        Assert.Equal("test.trx", context.ResultsFile);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with --result flag but no value throws exception.
    /// </summary>
    [Fact]
    public void Context_Create_ResultAliasFlag_WithoutValue_ThrowsArgumentException()
    {
        // Arrange: no setup required; the arguments are the input under test

        // Act: parse --result without a value
        var exception = Record.Exception(() => Context.Create(["--result"]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--result", argumentException.Message);
    }

    /// <summary>
    ///     Test creating a context with the depth flag.
    /// </summary>
    [Fact]
    public void Context_Create_DepthFlag_SetsHeadingDepth()
    {
        // Act: execute the operation being tested
        using var context = Context.Create(["--depth", "3"]);

        // Assert: verify expected behavior
        Assert.Equal(3, context.HeadingDepth);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with no depth flag returns default heading depth of 1.
    /// </summary>
    [Fact]
    public void Context_Create_NoDepthFlag_ReturnsDefaultHeadingDepth()
    {
        // Act: execute the operation being tested
        using var context = Context.Create([]);

        // Assert: verify default depth is 1
        Assert.Equal(1, context.HeadingDepth);
        Assert.Equal(0, context.ExitCode);
    }

    /// <summary>
    ///     Test creating a context with --depth flag but no value throws exception.
    /// </summary>
    [Fact]
    public void Context_Create_DepthFlag_WithoutValue_ThrowsArgumentException()
    {
        // Arrange: no setup required; the arguments are the input under test

        // Act: parse --depth without a value
        var exception = Record.Exception(() => Context.Create(["--depth"]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--depth", argumentException.Message);
    }

    /// <summary>
    ///     Test creating a context with --depth flag and non-integer value throws exception.
    /// </summary>
    [Fact]
    public void Context_Create_DepthFlag_NonIntegerValue_ThrowsArgumentException()
    {
        // Arrange: no setup required; the non-integer value is the input under test

        // Act: parse --depth with a non-integer value
        var exception = Record.Exception(() => Context.Create(["--depth", "abc"]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--depth", argumentException.Message);
    }

    /// <summary>
    ///     Test creating a context with --depth flag and zero value throws exception.
    /// </summary>
    [Fact]
    public void Context_Create_DepthFlag_ZeroValue_ThrowsArgumentException()
    {
        // Arrange: no setup required; the zero value is the input under test

        // Act: parse --depth with a zero value
        var exception = Record.Exception(() => Context.Create(["--depth", "0"]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--depth", argumentException.Message);
    }

    /// <summary>
    ///     Test creating a context with --depth flag and value exceeding maximum throws exception.
    /// </summary>
    [Fact]
    public void Context_Create_DepthFlag_ExceedsMaxValue_ThrowsArgumentException()
    {
        // Arrange: no setup required; the out-of-range value is the input under test

        // Act: parse --depth with a value above the maximum
        var exception = Record.Exception(() => Context.Create(["--depth", "7"]));

        // Assert: an ArgumentException naming the flag is thrown
        var argumentException = Assert.IsType<ArgumentException>(exception);
        Assert.Contains("--depth", argumentException.Message);
    }

    /// <summary>
    ///     Test WriteLine writes to console output when not silent.
    /// </summary>
    [Fact]
    public void Context_WriteLine_NotSilent_WritesToConsole()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create([]);

            // Act: execute the operation being tested
            context.WriteLine("Test message");

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.Contains("Test message", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test WriteLine does not write to console when silent.
    /// </summary>
    [Fact]
    public void Context_WriteLine_Silent_DoesNotWriteToConsole()
    {
        // Arrange: setup test conditions
        var originalOut = Console.Out;
        try
        {
            using var outWriter = new StringWriter();
            Console.SetOut(outWriter);
            using var context = Context.Create(["--silent"]);

            // Act: execute the operation being tested
            context.WriteLine("Test message");

            // Assert: verify expected behavior
            var output = outWriter.ToString();
            Assert.DoesNotContain("Test message", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     Test WriteError does not write to console when silent.
    /// </summary>
    [Fact]
    public void Context_WriteError_Silent_DoesNotWriteToConsole()
    {
        // Arrange: setup test conditions
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);
            using var context = Context.Create(["--silent"]);

            // Act: execute the operation being tested
            context.WriteError("Test error message");

            // Assert - error output should be suppressed in silent mode
            var output = errWriter.ToString();
            Assert.DoesNotContain("Test error message", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test WriteError sets exit code to 1.
    /// </summary>
    [Fact]
    public void Context_WriteError_SetsErrorExitCode()
    {
        // Arrange: setup test conditions
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);
            using var context = Context.Create([]);

            // Act: execute the operation being tested
            context.WriteError("Test error message");

            // Assert: verify expected behavior
            Assert.Equal(1, context.ExitCode);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test WriteError writes message to console when not silent.
    /// </summary>
    [Fact]
    public void Context_WriteError_NotSilent_WritesToConsole()
    {
        // Arrange: setup test conditions
        var originalError = Console.Error;
        try
        {
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);
            using var context = Context.Create([]);

            // Act: execute the operation being tested
            context.WriteError("Test error message");

            // Assert: verify expected behavior
            var output = errWriter.ToString();
            Assert.Contains("Test error message", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    ///     Test WriteError writes message to log file when logging is enabled.
    /// </summary>
    [Fact]
    public void Context_WriteError_WritesToLogFile()
    {
        // Arrange: setup test conditions
        var logFile = Path.GetTempFileName();
        try
        {
            // Act - use silent to avoid console output; verify the error still goes to the log
            using (var context = Context.Create(["--silent", "--log", logFile]))
            {
                context.WriteError("Test error in log");
                Assert.Equal(1, context.ExitCode);
            }

            // Assert - log file should contain the error message
            Assert.True(File.Exists(logFile));
            var logContent = File.ReadAllText(logFile);
            Assert.Contains("Test error in log", logContent);
        }
        finally
        {
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    /// <summary>
    ///     Test creating a context with --log flag pointing to an invalid path throws InvalidOperationException.
    /// </summary>
    [Fact]
    public void Context_Create_LogFlag_InvalidPath_ThrowsInvalidOperationException()
    {
        // Arrange: a path that cannot be opened as a file (directory or invalid characters)
        // Use a directory path so it cannot be opened as a file
        var invalidLogPath = Path.GetTempPath(); // temp directory itself, not a file

        // Act: create a context whose log path cannot be opened
        var exception = Record.Exception(() => Context.Create(["--log", invalidLogPath]));

        // Assert: an InvalidOperationException is thrown
        Assert.IsType<InvalidOperationException>(exception);
    }
}


