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

using System.Diagnostics;

namespace DemaConsulting.ReviewCenter.Tests.ContentSources;

/// <summary>
///     A temporary folder that is removed (read-only git objects included) when disposed.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TempFolder"/> class and creates the folder.
    /// </summary>
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rc-git-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>
    ///     Gets the full path of the folder.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Removes the folder and everything in it.
    /// </summary>
    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder that cannot be removed is left to the operating system.
        }
        catch (UnauthorizedAccessException)
        {
            // A temporary folder that cannot be removed is left to the operating system.
        }
    }
}

/// <summary>
///     Helpers that create real git repositories for the git-layer tests.
/// </summary>
internal static class GitTestSupport
{
    /// <summary>
    ///     The message of a skipped test when git is not installed.
    /// </summary>
    public const string MissingMessage = "git is not installed (version 2.31 or later on PATH); the git-layer test is skipped.";

    /// <summary>
    ///     Gets a value indicating whether a usable git is installed.
    /// </summary>
    public static bool Available { get; } = ProbeGit();

    /// <summary>
    ///     Runs git in a folder with a fixed identity and no user or system configuration.
    /// </summary>
    /// <param name="folder">The working folder.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The standard output.</returns>
    public static string Git(string folder, params string[] arguments)
    {
        var info = NewStartInfo(folder);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {error.Result}");
        }

        return output.TrimEnd('\r', '\n');
    }

    /// <summary>
    ///     Writes a text file under a folder, creating the parent folders.
    /// </summary>
    /// <param name="folder">The working folder.</param>
    /// <param name="relativePath">The path of the file.</param>
    /// <param name="content">The content.</param>
    public static void Write(string folder, string relativePath, string content)
    {
        var path = System.IO.Path.Combine(folder, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    ///     Stages everything and commits it.
    /// </summary>
    /// <param name="folder">The working folder.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>The commit id.</returns>
    public static string Commit(string folder, string message)
    {
        Git(folder, "add", "-A");
        Git(folder, "commit", "--allow-empty", "-m", message);
        return Git(folder, "rev-parse", "HEAD");
    }

    /// <summary>
    ///     Initializes a repository whose default branch is main.
    /// </summary>
    /// <param name="folder">The working folder.</param>
    public static void Init(string folder) => Git(folder, "init", "-b", "main");

    /// <summary>
    ///     The folder that holds the template repositories of the test run, removed when the process exits.
    /// </summary>
    private static readonly Lazy<TempFolder> _templates = new(() =>
    {
        var folder = new TempFolder();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => folder.Dispose();
        return folder;
    });

    /// <summary>
    ///     Creates a new, empty folder for a template repository: one that is built once and then copied by every
    ///     test that needs a repository of that shape, instead of building it again with a dozen git processes.
    /// </summary>
    /// <returns>The full path of the folder.</returns>
    public static string NewTemplateFolder()
    {
        var folder = System.IO.Path.Combine(_templates.Value.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    ///     Copies a folder with everything in it, git repositories included; the copy is a working repository of its
    ///     own, because git keeps no absolute path in it.
    /// </summary>
    /// <param name="source">The folder to copy.</param>
    /// <param name="destination">The folder to create.</param>
    public static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, file)));
        }
    }

    /// <summary>
    ///     Creates the start information of a git process.
    /// </summary>
    /// <param name="folder">The working folder.</param>
    /// <returns>The start information with the fixed environment.</returns>
    private static ProcessStartInfo NewStartInfo(string folder)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("commit.gpgsign=false");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("core.autocrlf=false");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("protocol.file.allow=always");
        info.Environment["GIT_AUTHOR_NAME"] = "Test";
        info.Environment["GIT_AUTHOR_EMAIL"] = "test@example.com";
        info.Environment["GIT_COMMITTER_NAME"] = "Test";
        info.Environment["GIT_COMMITTER_EMAIL"] = "test@example.com";
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        return info;
    }

    /// <summary>
    ///     Checks whether git 2.31 or later runs.
    /// </summary>
    /// <returns>True when git is usable.</returns>
    private static bool ProbeGit()
    {
        try
        {
            var info = NewStartInfo(System.IO.Path.GetTempPath());
            info.ArgumentList.Add("--version");
            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (process.ExitCode != 0 || parts.Length < 3)
            {
                return false;
            }

            var numbers = parts[2].Split('.');
            return numbers.Length >= 2
                && int.TryParse(numbers[0], out var major)
                && int.TryParse(numbers[1], out var minor)
                && (major > 2 || (major == 2 && minor >= 31));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
