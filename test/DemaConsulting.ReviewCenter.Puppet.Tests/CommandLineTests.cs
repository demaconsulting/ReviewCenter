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

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the <see cref="CommandLine"/> parser.
/// </summary>
public class CommandLineTests
{
    /// <summary>
    ///     Test that a command without options uses the 10 second default timeout.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_NoTimeout_UsesTenSecondDefault()
    {
        // Arrange: a click command with no options
        string[] args = ["click", "hello-button"];

        // Act: parse it
        var command = CommandLine.Parse(args);

        // Assert: the verb, argument, and default timeout are set
        Assert.Equal("click", command.Verb);
        Assert.Equal(["hello-button"], command.Positionals);
        Assert.Equal(10, command.TimeoutSeconds);
        Assert.Equal(10_000, command.TimeoutMilliseconds);
    }

    /// <summary>
    ///     Test that --timeout overrides the default.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_Timeout_OverridesDefault()
    {
        // Arrange: a click with an explicit timeout
        string[] args = ["click", "hello-button", "--timeout", "42"];

        // Act: parse it
        var command = CommandLine.Parse(args);

        // Assert: the timeout is the requested value
        Assert.Equal(42, command.TimeoutSeconds);
    }

    /// <summary>
    ///     Test that an out-of-range or non-numeric timeout is rejected.
    /// </summary>
    /// <param name="value">The invalid timeout text.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("601")]
    [InlineData("-5")]
    [InlineData("abc")]
    public void CommandLine_Parse_InvalidTimeout_ThrowsArgumentException(string value)
    {
        // Arrange: a command with an invalid timeout
        string[] args = ["text", "x", "--timeout", value];

        // Act: parse it
        var exception = Record.Exception(() => CommandLine.Parse(args));

        // Assert: parsing fails
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that --session is captured and validated.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_Session_IsCaptured()
    {
        // Arrange: a start command naming a session
        string[] args = ["start", "--session", "demo-1"];

        // Act: parse it
        var command = CommandLine.Parse(args);

        // Assert: the session id is captured
        Assert.Equal("demo-1", command.SessionId);
    }

    /// <summary>
    ///     Test that session ids with path characters are rejected so they cannot escape the session folder.
    /// </summary>
    /// <param name="id">The invalid session id.</param>
    [Theory]
    [InlineData("..\\evil")]
    [InlineData("a/b")]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("0123456789012345678901234567890123")]
    public void CommandLine_Parse_InvalidSessionId_ThrowsArgumentException(string id)
    {
        // Arrange: a command with an invalid session id
        string[] args = ["start", "--session", id];

        // Act: parse it
        var exception = Record.Exception(() => CommandLine.Parse(args));

        // Assert: parsing fails
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that screenshot accepts --out and --full-page.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_ScreenshotOptions_AreCaptured()
    {
        // Arrange: a screenshot with both options
        string[] args = ["screenshot", "--out", "shot.png", "--full-page"];

        // Act: parse it
        var command = CommandLine.Parse(args);

        // Assert: both options are captured
        Assert.Equal("shot.png", command.OutputPath);
        Assert.True(command.FullPage);
    }

    /// <summary>
    ///     Test that screenshot-only options are rejected on other commands.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_OutOnNonScreenshot_ThrowsArgumentException()
    {
        // Arrange: a click with --out
        string[] args = ["click", "x", "--out", "a.png"];

        // Act: parse it
        var exception = Record.Exception(() => CommandLine.Parse(args));

        // Assert: parsing fails
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that an unknown command is rejected with a helpful message.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_UnknownCommand_ThrowsArgumentException()
    {
        // Arrange: a command that does not exist
        string[] args = ["dance"];

        // Act: parse it
        var exception = Assert.Throws<ArgumentException>(() => CommandLine.Parse(args));

        // Assert: the message names the command
        Assert.Contains("dance", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that no arguments is an error.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_NoArguments_ThrowsArgumentException()
    {
        // Arrange: no arguments at all
        string[] args = [];

        // Act: parse them
        var exception = Record.Exception(() => CommandLine.Parse(args));

        // Assert: parsing fails
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that a missing or surplus positional argument is rejected.
    /// </summary>
    /// <param name="argument">The argument list, space separated.</param>
    [Theory]
    [InlineData("click")]
    [InlineData("click a b")]
    [InlineData("fill only-selector")]
    [InlineData("stop extra")]
    public void CommandLine_Parse_WrongArgumentCount_ThrowsArgumentException(string argument)
    {
        // Arrange: split the arguments
        var args = argument.Split(' ');

        // Act: parse them
        var exception = Record.Exception(() => CommandLine.Parse(args));

        // Assert: parsing fails
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that an unknown option is rejected.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_UnknownOption_ThrowsArgumentException()
    {
        // Arrange: a list command with an unknown option
        string[] args = ["list", "--bogus"];

        // Act: parse it
        var exception = Record.Exception(() => CommandLine.Parse(args));

        // Assert: parsing fails
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that an option missing its value is rejected.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_OptionWithoutValue_ThrowsArgumentException()
    {
        // Arrange: --session without a value
        string[] args = ["stop", "--session"];

        // Act: parse it
        var exception = Record.Exception(() => CommandLine.Parse(args));

        // Assert: parsing fails
        Assert.IsType<ArgumentException>(exception);
    }

    /// <summary>
    ///     Test that --server is accepted only for start.
    /// </summary>
    [Fact]
    public void CommandLine_Parse_ServerOnStart_IsCaptured()
    {
        // Arrange: start and list commands that both carry --server
        string[] startArgs = ["start", "--server", "x.dll"];
        string[] listArgs = ["list", "--server", "x.dll"];

        // Act: parse start, and try to parse list
        var command = CommandLine.Parse(startArgs);
        var exception = Record.Exception(() => CommandLine.Parse(listArgs));

        // Assert: the path is captured for start; and a different verb rejects it
        Assert.Equal("x.dll", command.ServerPath);
        Assert.IsType<ArgumentException>(exception);
    }
}
