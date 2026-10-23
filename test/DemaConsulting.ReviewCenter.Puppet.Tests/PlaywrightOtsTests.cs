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

using Microsoft.Playwright;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Self-validation tests for the Playwright OTS item that need no browser, so they run in every environment.
/// </summary>
/// <remarks>
///     These tests prove what the Puppet tool needs of Playwright and can be proven anywhere: the driver and the
///     browser installation script are delivered with the build output, and a failed browser launch is reported as a
///     <see cref="PlaywrightException"/>. They never skip and never call the Chromium availability probe. Driving a
///     real Chromium is covered separately by the end-to-end test, which CI runs with Chromium installed and which is
///     linked to the Puppet system requirements.
/// </remarks>
public class PlaywrightOtsTests
{
    /// <summary>
    ///     Test that the build output contains the Playwright installation script and the driver.
    /// </summary>
    [Fact]
    public void PlaywrightOts_BuildOutput_Inspected_ContainsInstallScriptAndDriver()
    {
        // Arrange: the folder the test host runs from, which receives the package's content files
        var outputFolder = AppContext.BaseDirectory;

        // Act: locate the installation script and the driver folder
        var script = Path.Combine(outputFolder, "playwright.ps1");
        var driverFolder = Path.Combine(outputFolder, ".playwright");

        // Assert: the script exists and the driver folder is present with content
        Assert.True(File.Exists(script), $"The installation script was not found at {script}.");
        Assert.True(Directory.Exists(driverFolder), $"The driver folder was not found at {driverFolder}.");
        Assert.NotEmpty(Directory.GetFileSystemEntries(Path.Combine(driverFolder, "node"), "*", SearchOption.AllDirectories));
    }

    /// <summary>
    ///     Test that Playwright starts its driver and reports a failed browser launch as a
    ///     <see cref="PlaywrightException"/>.
    /// </summary>
    [Fact]
    public async Task PlaywrightOts_Chromium_MissingExecutable_ThrowsPlaywrightException()
    {
        // Arrange: a browser executable path that cannot exist, so no installed browser is involved
        using var temp = new TempDirectory();
        var missingExecutable = Path.Combine(temp.Path, "no-such-chromium.exe");
        using var playwright = await Playwright.CreateAsync();

        // Act: launch
        var exception = await Record.ExceptionAsync(() => playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true, ExecutablePath = missingExecutable }));

        // Assert: the launch fails with the library's exception type
        Assert.IsAssignableFrom<PlaywrightException>(exception);
    }
}
