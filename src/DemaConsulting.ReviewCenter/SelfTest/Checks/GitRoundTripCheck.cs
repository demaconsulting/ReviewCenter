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
///     Checks that the server's git layer can commit to a local repository, clone it and read the commits back.
/// </summary>
/// <remarks>
///     A temporary repository is created with two commits. The production <see cref="GitSource"/> then mirrors it
///     into a temporary cache, resolves the branch, lists the changes between the commits and reads a changed file
///     as a raw blob. Nothing leaves the temporary folder and no network is used. The class is stateless and
///     thread-safe.
/// </remarks>
internal static class GitRoundTripCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_GitRoundTrip";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A short description of what was read back.</returns>
    /// <exception cref="ValidationFailedException">Thrown when git fails or the data read back is wrong.</exception>
    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        // Declared first, so it is disposed last: the repository (and its git processes) is closed before the folder
        using var folder = new TemporaryDirectory();
        var options = new GitOptions { CachePath = Path.Combine(folder.DirectoryPath, "git-cache"), TimeoutSeconds = 60 };
        var runner = new GitRunner(options);

        try
        {
            // Commit twice in a local repository, then read it through the production git source
            var origin = await SelfTestGit.CreateOriginAsync(runner, folder, cancellationToken);
            var access = new RepositoryAccess(0, "selftest", origin.Folder, null, null);
            await using var repository = await new GitSource(runner, options).OpenAsync(access, cancellationToken);

            var head = await repository.ResolveRevisionAsync("main", cancellationToken);
            ValidationFailedException.ThrowUnless(
                head == origin.HeadSha, "The branch did not resolve to the commit that was made.");

            var changes = await repository.ListChangesAsync(origin.BaseSha, origin.HeadSha, cancellationToken);
            var modified = changes.Changes.SingleOrDefault(c => c.Path == "src/a.txt" && c.Kind == ChangeKind.Modified);
            var added = changes.Changes.SingleOrDefault(c => c.Path == "src/b.txt" && c.Kind == ChangeKind.Added);
            ValidationFailedException.ThrowUnless(
                modified != null && added != null && changes.Changes.Count == 2,
                "The changes between the two commits were not the modified and the added file.");

            var content = Encoding.UTF8.GetString(
                await repository.ReadBlobAsync(modified!.HeadBlobSha!, 1024, cancellationToken));
            ValidationFailedException.ThrowUnless(
                content == "two\n", "The file read back from the repository does not match what was committed.");

            return $"2 commits, {changes.Changes.Count} changes, file read back unchanged";
        }
        catch (DomainRuleException ex)
        {
            // The git layer reports its failures as domain rules, with a message that is safe to show
            throw new ValidationFailedException(ex.Message);
        }
    }
}
