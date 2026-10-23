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
///     A browser page that records the calls made to it instead of driving a browser.
/// </summary>
internal sealed class FakeBrowserPage : IBrowserPage
{
    /// <summary>
    ///     Gets the calls received, in order, as readable text such as <c>click [data-testid="x"]</c>.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    ///     Gets or sets the text returned by <see cref="TextAsync"/>.
    /// </summary>
    public string TextResult { get; set; } = "fake text";

    /// <summary>
    ///     Gets or sets a selector fragment; a click on a selector containing it fails like a missing element.
    /// </summary>
    public string? FailClickOn { get; set; }

    /// <summary>
    ///     Gets the number of times the page was disposed.
    /// </summary>
    public int DisposeCount { get; private set; }

    /// <inheritdoc />
    public Task GotoAsync(string url, int timeoutMs)
    {
        Calls.Add($"goto {url}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClickAsync(string selector, int timeoutMs)
    {
        Calls.Add($"click {selector}");
        return FailClickOn is not null && selector.Contains(FailClickOn, StringComparison.Ordinal)
            ? Task.FromException(new PuppetException($"No element matching {selector}."))
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FillAsync(string selector, string text, int timeoutMs)
    {
        Calls.Add($"fill {selector} {text}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> TextAsync(string selector, int timeoutMs)
    {
        Calls.Add($"text {selector}");
        return Task.FromResult(TextResult);
    }

    /// <inheritdoc />
    public Task<byte[]> ScreenshotAsync(bool fullPage, int timeoutMs)
    {
        Calls.Add($"screenshot fullPage={fullPage}");
        return Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
///     A server that has a fixed URL and counts how often it was stopped.
/// </summary>
internal sealed class FakeRunningServer : IRunningServer
{
    /// <inheritdoc />
    public string Url => "http://fake-server:1234";

    /// <summary>
    ///     Gets the number of times the server was disposed (stopped).
    /// </summary>
    public int DisposeCount { get; private set; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
///     A temporary directory that is deleted when disposed.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TempDirectory"/> class and creates the directory.
    /// </summary>
    public TempDirectory()
    {
        Path = Directory.CreateTempSubdirectory("puppet-tests-").FullName;
    }

    /// <summary>
    ///     Gets the full path of the directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Writes a file into the directory.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The file text.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteFile(string name, string content)
    {
        var file = System.IO.Path.Combine(Path, name);
        File.WriteAllText(file, content);
        return file;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(Path, recursive: true);
    }
}
