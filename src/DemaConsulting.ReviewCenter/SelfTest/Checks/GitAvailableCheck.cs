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

using System.Text;
using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Utilities;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks that the git the server needs is installed and recent enough.
/// </summary>
/// <remarks>
///     The check runs <c>git --version</c> through the production <see cref="GitRunner"/>, which enforces the
///     minimum version, so it proves exactly what the server will find. Its working folders are temporary. The
///     class is stateless and thread-safe.
/// </remarks>
internal static class GitAvailableCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_GitAvailable";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The version text git reported.</returns>
    /// <exception cref="ValidationFailedException">Thrown when git is missing or older than the minimum.</exception>
    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        using var folder = new TemporaryDirectory();
        var runner = new GitRunner(new GitOptions { CachePath = Path.Combine(folder.DirectoryPath, "git-cache") });

        try
        {
            var result = await runner.RunAsync(["--version"], null, 4096, cancellationToken);
            var text = Encoding.UTF8.GetString(result.Output).Trim();
            return $"{text} (minimum {GitRunner.MinimumVersion})";
        }
        catch (DomainRuleException ex)
        {
            // The message already tells the administrator what to install
            throw new ValidationFailedException(ex.Message);
        }
    }
}
