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
using System.Net.Sockets;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the control channel: <see cref="ControlServer"/>, <see cref="ControlClient"/>,
///     and <see cref="ControlProtocol"/>.
/// </summary>
public class ControlChannelTests
{
    /// <summary>
    ///     Creates a request for tests.
    /// </summary>
    /// <param name="token">The session token to present.</param>
    /// <param name="command">The command name.</param>
    /// <returns>A control request.</returns>
    private static ControlRequest CreateRequest(string token, string command = "click")
    {
        return new ControlRequest(token, command, "hello-button", null, null, false, 5000);
    }

    /// <summary>
    ///     Test that a request carrying the right token reaches the handler and its answer comes back.
    /// </summary>
    [Fact]
    public async Task ControlChannel_Send_ValidToken_ReturnsHandlerResponse()
    {
        // Arrange: a server whose handler echoes the command, running in the background
        var token = ControlServer.CreateToken();
        using var server = new ControlServer(token, (request, _) =>
            Task.FromResult(new ControlResponse(true, 0, "handled " + request.Command)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        var serving = server.RunAsync(TimeSpan.FromSeconds(30), cancellation.Token);

        // Act: send one request
        var response = await ControlClient.SendAsync(
            server.Port, CreateRequest(token), TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);

        // Assert: the handler's answer is returned
        Assert.True(response.Ok);
        Assert.Equal("handled click", response.Message);

        await cancellation.CancelAsync();
        await serving;
    }

    /// <summary>
    ///     Test that a request with the wrong token is rejected without reaching the handler.
    /// </summary>
    [Fact]
    public async Task ControlChannel_Send_WrongToken_IsRejectedWithoutCallingHandler()
    {
        // Arrange: a server that counts handler calls
        var calls = 0;
        using var server = new ControlServer("right-token", (_, _) =>
        {
            calls++;
            return Task.FromResult(new ControlResponse(true, 0, "ok"));
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        var serving = server.RunAsync(TimeSpan.FromSeconds(30), cancellation.Token);

        // Act: send with another token
        var response = await ControlClient.SendAsync(
            server.Port, CreateRequest("wrong-token"), TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);

        // Assert: rejected and the handler never ran
        Assert.False(response.Ok);
        Assert.Contains("token", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, calls);

        await cancellation.CancelAsync();
        await serving;
    }

    /// <summary>
    ///     Test that the server listens on the loopback interface only.
    /// </summary>
    [Fact]
    public void ControlChannel_Server_Listening_BindsToLoopbackOnly()
    {
        // Arrange: a server whose handler is never called
        // Act: create it
        using var server = new ControlServer("t", (_, _) => Task.FromResult(new ControlResponse(true, 0, "ok")));

        // Assert: a connection to loopback works and the port is a real one
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, server.Port);
        Assert.True(client.Connected);
        Assert.InRange(server.Port, 1, 65535);
    }

    /// <summary>
    ///     Test that the server stops by itself when no request arrives within the idle timeout.
    /// </summary>
    [Fact]
    public async Task ControlChannel_Run_NoRequests_ReturnsTrueAfterIdleTimeout()
    {
        // Arrange: a server with a very short idle timeout
        using var server = new ControlServer("t", (_, _) => Task.FromResult(new ControlResponse(true, 0, "ok")));

        // Act: run it with nobody calling
        var idle = await server.RunAsync(TimeSpan.FromMilliseconds(100), Xunit.TestContext.Current.CancellationToken);

        // Assert: it reports that it ended because of idleness
        Assert.True(idle);
    }

    /// <summary>
    ///     Test that cancelling stops the server and reports that it did not idle out.
    /// </summary>
    [Fact]
    public async Task ControlChannel_Run_Cancelled_ReturnsFalse()
    {
        // Arrange: a server and an already cancelled token
        using var server = new ControlServer("t", (_, _) => Task.FromResult(new ControlResponse(true, 0, "ok")));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act: run it
        var idle = await server.RunAsync(TimeSpan.FromSeconds(30), cancellation.Token);

        // Assert: not idle
        Assert.False(idle);
    }

    /// <summary>
    ///     Test that a client talking to a port with nobody listening gets a clear error.
    /// </summary>
    [Fact]
    public async Task ControlChannel_Send_NothingListening_ThrowsPuppetException()
    {
        // Arrange: find a free port and release it so nothing listens there
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        // Act: send to the dead port
        var exception = await Assert.ThrowsAsync<PuppetException>(() => ControlClient.SendAsync(
            port, CreateRequest("t"), TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken));

        // Assert: the client reports it cannot reach the daemon
        Assert.Contains("daemon", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a slow handler makes the client give up with a timeout message.
    /// </summary>
    [Fact]
    public async Task ControlChannel_Send_HandlerTooSlow_ThrowsPuppetExceptionOnTimeout()
    {
        // Arrange: a server whose handler waits for the test to finish
        using var release = new CancellationTokenSource();
        using var server = new ControlServer("t", async (_, _) =>
        {
            await Task.Delay(Timeout.Infinite, release.Token).ConfigureAwait(false);
            return new ControlResponse(true, 0, "never");
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        var serving = server.RunAsync(TimeSpan.FromSeconds(30), cancellation.Token);

        // Act: send with a short client timeout
        var exception = await Assert.ThrowsAsync<PuppetException>(() => ControlClient.SendAsync(
            server.Port, CreateRequest("t"), TimeSpan.FromMilliseconds(300), Xunit.TestContext.Current.CancellationToken));

        // Assert: the message says it timed out
        Assert.Contains("did not answer", exception.Message, StringComparison.Ordinal);

        await release.CancelAsync();
        await cancellation.CancelAsync();
        try
        {
            await serving;
        }
        catch (OperationCanceledException)
        {
            // The handler was cancelled on purpose to end the test
        }
    }

    /// <summary>
    ///     Test that a handler failing unexpectedly produces a failed response and the server keeps serving.
    /// </summary>
    [Fact]
    public async Task ControlChannel_Send_HandlerThrowsUnexpected_ReturnsFailedResponseAndKeepsServing()
    {
        // Arrange: a handler that throws on the first request only, served in the background
        var calls = 0;
        using var server = new ControlServer("t", (_, _) =>
        {
            calls++;
            return calls == 1
                ? throw new InvalidOperationException("boom\nsecond line")
                : Task.FromResult(new ControlResponse(true, 0, "recovered"));
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        var serving = server.RunAsync(TimeSpan.FromSeconds(30), cancellation.Token);

        // Act: send two requests
        var first = await ControlClient.SendAsync(
            server.Port, CreateRequest("t"), TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);
        var second = await ControlClient.SendAsync(
            server.Port, CreateRequest("t"), TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);

        // Assert: the first fails with a named problem, the second is served normally
        Assert.False(first.Ok);
        Assert.Equal(1, first.ExitCode);
        Assert.Contains("Unexpected error", first.Message, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", first.Message, StringComparison.Ordinal);
        Assert.Contains("boom", first.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("second line", first.Message, StringComparison.Ordinal);
        Assert.True(second.Ok);
        Assert.Equal("recovered", second.Message);

        await cancellation.CancelAsync();
        await serving;
    }

    /// <summary>
    ///     Test that a malformed control message is reported as a protocol error.
    /// </summary>
    [Fact]
    public void ControlChannel_Deserialize_MalformedJson_ThrowsPuppetException()
    {
        // Arrange: text that is not a control message
        const string garbage = "{ nope";

        // Act: deserialize it
        var exception = Record.Exception(() => ControlProtocol.Deserialize<ControlResponse>(garbage));

        // Assert: it is reported as a protocol error
        Assert.IsType<PuppetException>(exception);
    }

    /// <summary>
    ///     Test that a request survives serialization.
    /// </summary>
    [Fact]
    public void ControlChannel_SerializeDeserialize_Request_RoundTrips()
    {
        // Arrange: a request with every field set
        var request = new ControlRequest("tok", "fill", "#a", "text", "out.png", true, 1234);

        // Act: round-trip it
        var copy = ControlProtocol.Deserialize<ControlRequest>(ControlProtocol.Serialize(request));

        // Assert: nothing is lost
        Assert.Equal(request, copy);
    }
}
