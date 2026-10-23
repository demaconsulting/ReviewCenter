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
///     Unit test for <see cref="ServerProcess"/> that starts the real ReviewCenter server; needs no browser.
/// </summary>
[Collection("Sequential")]
public class ServerProcessTests
{
    /// <summary>
    ///     Test that the real server becomes healthy on a free port and stops when disposed.
    /// </summary>
    [Fact]
    public async Task ServerProcess_Start_RealServer_BecomesHealthyAndStopsOnDispose()
    {
        // Arrange: the server assembly built next to the Puppet
        var serverPath = PuppetProgram.ResolveServerPath(null);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Act: start it and ask for its health
        var server = await ServerProcess.StartAsync(serverPath, TestContext.Current.CancellationToken);
        var healthy = await http.GetStringAsync($"{server.Url}/health", TestContext.Current.CancellationToken);
        await server.DisposeAsync();

        // Assert: it answered while running and no longer answers once disposed
        Assert.Equal("Healthy", healthy);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => http.GetStringAsync($"{server.Url}/health", TestContext.Current.CancellationToken));
    }
}
