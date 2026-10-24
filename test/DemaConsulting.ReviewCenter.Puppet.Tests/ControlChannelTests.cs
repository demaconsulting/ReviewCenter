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
///     Unit tests for the <see cref="ControlChannel"/> protocol.
/// </summary>
public class ControlChannelTests
{
    /// <summary>
    ///     Test that one request and one response, including a multi-line result, make the round trip.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ControlChannel_SendAsync_RoundTrip_DeliversRequestAndResponse()
    {
        // Arrange: a real loopback listener whose handler echoes the command and arguments
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource();
        var serving = ControlChannel.ServeAsync(
            listener,
            request => Task.FromResult(new PuppetResponse(true, $"{request.Command}\n{string.Join(',', request.Args)}", "http://x/", "T", ["e1"])),
            stop.Token);

        // Act: send one request
        var response = await ControlChannel.SendAsync(
            port,
            new PuppetRequest("click", ["a", "b"], new Dictionary<string, string> { ["full"] = "true" }),
            TestContext.Current.CancellationToken);

        // Assert: the response arrived intact; then stop the server loop
        Assert.True(response.Ok);
        Assert.Equal("click\na,b", response.Output);
        Assert.Equal(("http://x/", "T"), (response.Url, response.Title));
        Assert.Equal(["e1"], response.Events);
        await stop.CancelAsync();
        await serving;
        listener.Stop();
    }

    /// <summary>
    ///     Test that a port nobody listens on is not answering, which marks its control file as stale.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ControlChannel_IsAnsweringAsync_StaleControlFile_ReturnsFalse()
    {
        // Arrange: a port that was free a moment ago
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        // Act and assert: nothing answers
        Assert.False(await ControlChannel.IsAnsweringAsync(port));
    }
}
