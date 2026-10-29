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
using DemaConsulting.ReviewCenter.Utilities;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Creates the small local git repository that the git and review-workflow checks read.
/// </summary>
/// <remarks>
///     Every git call goes through the production <see cref="GitRunner"/>, so the repository is built with the
///     same hardened, configuration-free git environment the server uses and the check proves that environment
///     works. The repository lives in the temporary folder of the check. The class is stateless and thread-safe.
/// </remarks>
internal static class SelfTestGit
{
    /// <summary>
    ///     The largest standard output a repository-building command may produce.
    /// </summary>
    private const long MaxOutputBytes = 1024 * 1024;

    /// <summary>
    ///     The review-set configuration committed to the repository: one set that covers the <c>src</c> folder.
    /// </summary>
    public const string ReviewConfiguration = "reviews:\n  - id: core\n    title: Core\n    paths:\n      - \"src/**\"\n";

    /// <summary>
    ///     The two commits of the repository the checks build.
    /// </summary>
    /// <param name="Folder">The working folder of the repository.</param>
    /// <param name="BaseSha">The first commit: the review configuration, a readme and <c>src/a.txt</c> ("one").</param>
    /// <param name="HeadSha">The second commit: <c>src/a.txt</c> changed to "two" and <c>src/b.txt</c> added.</param>
    public sealed record Origin(string Folder, string BaseSha, string HeadSha);

    /// <summary>
    ///     Creates the repository with its two commits.
    /// </summary>
    /// <param name="runner">The production git runner. Must not be null.</param>
    /// <param name="folder">The temporary folder that holds the repository. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="secondCommit">
    ///     <c>true</c> to add the second commit; <c>false</c> to stop after the first one, which saves several
    ///     git processes. Then <see cref="Origin.HeadSha"/> equals <see cref="Origin.BaseSha"/>.
    /// </param>
    /// <returns>The repository folder and its commit ids.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ValidationFailedException">Thrown when a git command fails.</exception>
    public static async Task<Origin> CreateOriginAsync(
        GitRunner runner,
        TemporaryDirectory folder,
        CancellationToken cancellationToken,
        bool secondCommit = true)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(folder);

        // Make sure the working folder exists: git -C needs it
        var work = Path.GetDirectoryName(folder.GetFilePath("origin/placeholder"))!;
        await GitAsync(runner, work, cancellationToken, "init", "-b", "main");

        // First commit
        Write(folder, "origin/.reviewmark.yaml", ReviewConfiguration);
        Write(folder, "origin/README.md", "# Self-test\n");
        Write(folder, "origin/src/a.txt", "one\n");
        await GitAsync(runner, work, cancellationToken, "add", "-A");
        await GitAsync(runner, work, cancellationToken, "commit", "-m", "one");

        // Second commit: one file modified, one added
        if (secondCommit)
        {
            Write(folder, "origin/src/a.txt", "two\n");
            Write(folder, "origin/src/b.txt", "new\n");
            await GitAsync(runner, work, cancellationToken, "add", "-A");
            await GitAsync(runner, work, cancellationToken, "commit", "-m", "two");
        }

        // One call lists the newest commits, newest first
        var ids = (await GitAsync(
                runner, work, cancellationToken, "rev-list", secondCommit ? "--max-count=2" : "--max-count=1", "HEAD"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new Origin(work, ids[^1], ids[0]);
    }

    /// <summary>
    ///     Writes a file with exactly the given text as UTF-8 without a byte-order mark.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <param name="relativePath">The path of the file inside the folder.</param>
    /// <param name="text">The content.</param>
    private static void Write(TemporaryDirectory folder, string relativePath, string text)
    {
        File.WriteAllBytes(folder.GetFilePath(relativePath), new UTF8Encoding(false).GetBytes(text));
    }

    /// <summary>
    ///     Runs one git command in the repository folder with a fixed identity and returns its output.
    /// </summary>
    /// <param name="runner">The git runner.</param>
    /// <param name="work">The repository working folder.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="arguments">The git subcommand and its arguments.</param>
    /// <returns>The standard output without the trailing line break.</returns>
    /// <exception cref="ValidationFailedException">Thrown when git exits with a non-zero code.</exception>
    private static async Task<string> GitAsync(
        GitRunner runner,
        string work,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        // The runner clears the environment and ignores user configuration, so the identity is given here
        var command = new List<string>
        {
            "-C", work,
            "-c", "user.name=ReviewCenter",
            "-c", "user.email=selftest@localhost",
            "-c", "commit.gpgsign=false",
            "-c", "core.autocrlf=false"
        };
        command.AddRange(arguments);

        var result = await runner.RunAsync(command, null, MaxOutputBytes, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new ValidationFailedException($"git {arguments[0]} failed: {result.Error.Trim()}");
        }

        return Encoding.UTF8.GetString(result.Output).TrimEnd('\r', '\n');
    }
}
