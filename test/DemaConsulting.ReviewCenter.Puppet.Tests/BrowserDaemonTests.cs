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
///     Unit tests for the start-up failure path of <see cref="BrowserDaemon"/> (the browser-free part of the daemon).
/// </summary>
public class BrowserDaemonTests
{
    /// <summary>
    ///     Test that a daemon that cannot start publishes the failure in the control file without a port and exits
    ///     with 1.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task BrowserDaemon_RunAsync_BadViewport_PublishesStartFailureWithoutPort()
    {
        // Arrange: a private control file; the malformed viewport is rejected before any browser is started
        var controlFile = Path.Combine(Path.GetTempPath(), $"puppet-daemon-{Guid.NewGuid():N}.json");
        try
        {
            // Act: run the daemon with the malformed viewport
            var exitCode = await BrowserDaemon.RunAsync(controlFile, null, "abc", headed: false);
            var info = ControlChannel.Read(controlFile);

            // Assert: exit code 1, and the control file holds the error and no port
            Assert.Equal(1, exitCode);
            Assert.NotNull(info);
            Assert.Null(info.Port);
            Assert.Contains("viewport must look like", info.Error, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(controlFile);
        }
    }
}
