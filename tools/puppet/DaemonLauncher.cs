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

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     Starts the session daemon as a process that outlives the command that requested it.
/// </summary>
internal interface IDaemonLauncher
{
    /// <summary>
    ///     Starts the daemon for a session.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="serverPath">The server assembly path the daemon should run.</param>
    /// <returns>The identity (process id and start time) of the daemon.</returns>
    /// <exception cref="PuppetException">Thrown when the process cannot be created or has already exited.</exception>
    ProcessIdentity Launch(string sessionId, string serverPath);
}

/// <summary>
///     Re-invokes this executable with the hidden <c>__daemon</c> verb as a detached background process.
/// </summary>
/// <remarks>
///     "Detached" matters: the daemon must keep running after the terminal, script, or tool call that
///     ran <c>start</c> has finished, and must not be killed with it. On Windows that means creating
///     the process without a console and outside the caller's job object (an agent harness commonly
///     wraps each command in a job that kills all children on exit); the breakaway flag is requested
///     first and dropped if the job forbids it. Other platforms use a plain child process with its
///     standard streams closed. No state is kept between calls.
/// </remarks>
internal sealed class DaemonLauncher : IDaemonLauncher
{
    /// <summary>
    ///     Launches the daemon process.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="serverPath">The server assembly path.</param>
    /// <returns>The daemon's process id and start time.</returns>
    /// <exception cref="PuppetException">Thrown when the process cannot be created or has already exited.</exception>
    public ProcessIdentity Launch(string sessionId, string serverPath)
    {
        var arguments = new List<string>();
        var executable = Environment.ProcessPath ?? throw new PuppetException("Cannot determine this executable's path.");

        // When started as 'dotnet Puppet.dll' the process path is the dotnet host and the assembly must be
        // passed along; when started through the apphost the executable is self-sufficient
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add(typeof(DaemonLauncher).Assembly.Location);
        }

        arguments.AddRange([CommandLine.DaemonVerb, "--session", sessionId, "--server", serverPath]);

        return LaunchProcess(executable, arguments);
    }

    /// <summary>
    ///     Starts a detached process and returns its identity, taken from the very process that was created.
    /// </summary>
    /// <remarks>
    ///     The identity (process id and start time) is what every later kill and liveness check is judged by,
    ///     so it must belong to the process that was created and not to whichever process holds the id a
    ///     moment later. It is therefore read from the creation handle (Windows) or from the
    ///     <see cref="Process"/> object that started the child (other platforms), never by looking the id up
    ///     again after the launch.
    /// </remarks>
    /// <param name="executable">The executable path.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The process id with its start time.</returns>
    /// <exception cref="PuppetException">Thrown when the process cannot be created or its identity cannot be read.</exception>
    internal static ProcessIdentity LaunchProcess(string executable, List<string> arguments)
    {
        return OperatingSystem.IsWindows()
            ? LaunchWindows(executable, arguments)
            : LaunchOther(executable, arguments);
    }

    /// <summary>
    ///     Captures the identity of a process that has just been created, ending the process when that fails.
    /// </summary>
    /// <remarks>
    ///     A process that is running without a recorded identity can never be cleaned up by identity, so a
    ///     failure to capture it must not leave the process behind. The termination itself is best effort: a
    ///     process that is already gone, or that cannot be ended, does not replace the original failure. A
    ///     partial tree kill (<see cref="AggregateException"/>) is the exception: descendants may still be
    ///     running with no identity to find them by, so the failure that is reported says so.
    /// </remarks>
    /// <param name="capture">Reads the identity of the created process.</param>
    /// <param name="terminate">Ends the created process.</param>
    /// <returns>The captured identity.</returns>
    internal static ProcessIdentity CaptureIdentity(Func<ProcessIdentity> capture, Action terminate)
    {
        try
        {
            return capture();
        }
        catch (Exception captureFailure)
        {
            // A partial tree kill means part of the process tree may be left running with no identity to
            // clean it up by, so it is reported instead of being treated as a process that is already gone.
            try
            {
                terminate();
            }
            catch (AggregateException partialKill)
            {
                throw new PuppetException(
                    $"{captureFailure.Message} The process was ended, but some of its child processes could not be " +
                    "ended and may still be running.",
                    partialKill);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException
                                           or System.ComponentModel.Win32Exception)
            {
                // The process is already gone or is not ours to end; the capture failure is the story.
            }

            throw;
        }
    }

    /// <summary>
    ///     Builds a Windows command line, quoting each argument.
    /// </summary>
    /// <param name="executable">The executable path.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The command line text.</returns>
    internal static string BuildCommandLine(string executable, IEnumerable<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append('"').Append(executable).Append('"');
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendQuoted(builder, argument);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Appends one argument enclosed in double quotes, escaped by the rules every Windows program
    ///     (including the .NET runtime) uses to split a command line, so it is read back unchanged.
    /// </summary>
    /// <remarks>
    ///     A double quote is escaped with a backslash, and the backslashes directly before a quote
    ///     (including the closing quote) are doubled so that they cannot escape it. Without this a
    ///     path ending in a backslash would swallow the closing quote and merge with the next argument.
    /// </remarks>
    /// <param name="builder">The command line being built.</param>
    /// <param name="argument">The argument to append.</param>
    private static void AppendQuoted(StringBuilder builder, string argument)
    {
        builder.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2).Append('"');
    }

    /// <summary>
    ///     Starts the daemon on Windows without a console and outside the caller's job object.
    /// </summary>
    /// <param name="executable">The executable path.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The daemon's process id and start time.</returns>
    /// <exception cref="PuppetException">Thrown when process creation fails.</exception>
    private static ProcessIdentity LaunchWindows(string executable, List<string> arguments)
    {
        var commandLine = BuildCommandLine(executable, arguments);
        var workingDirectory = AppContext.BaseDirectory;

        // First try to break away from the caller's job so the daemon survives the caller; a job that
        // forbids breakaway rejects the flag with "access denied", in which case start without it
        var baseFlags = NativeMethods.DetachedProcess | NativeMethods.CreateNewProcessGroup;
        if (!TryCreateProcess(commandLine, workingDirectory, baseFlags | NativeMethods.CreateBreakawayFromJob, out var identity, out var error) &&
            !TryCreateProcess(commandLine, workingDirectory, baseFlags, out identity, out error))
        {
            throw new PuppetException($"Could not start the session daemon: {new Win32Exception(error).Message}");
        }

        return identity;
    }

    /// <summary>
    ///     Calls <c>CreateProcessW</c> once.
    /// </summary>
    /// <param name="commandLine">The full command line.</param>
    /// <param name="workingDirectory">The working directory for the new process.</param>
    /// <param name="flags">The creation flags.</param>
    /// <param name="identity">Receives the new process id and start time on success.</param>
    /// <param name="error">Receives the Win32 error code on failure.</param>
    /// <returns><c>true</c> when the process was created.</returns>
    /// <exception cref="PuppetException">Thrown when the process was created but its start time cannot be read.</exception>
    private static bool TryCreateProcess(string commandLine, string workingDirectory, uint flags, out ProcessIdentity identity, out int error)
    {
        var startup = new NativeMethods.StartupInfo { Size = (uint)Marshal.SizeOf<NativeMethods.StartupInfo>() };
        if (NativeMethods.CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero,
                workingDirectory, in startup, out var info))
        {
            try
            {
                // The creation time is read through the handle returned by the creation, which addresses exactly
                // the process that was created even if it has already exited and its id has been reused. When it
                // cannot be read the caller would have no identity to clean up with, so the process is ended
                // through the same handle before the failure propagates.
                identity = CaptureIdentity(
                    () =>
                    {
                        if (!NativeMethods.GetProcessTimes(info.ProcessHandle, out var created, out _, out _, out _))
                        {
                            throw new PuppetException(
                                $"Could not read the start time of the session daemon: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
                        }

                        return new ProcessIdentity((int)info.ProcessId, DateTime.FromFileTimeUtc(created));
                    },
                    () => NativeMethods.TerminateProcess(info.ProcessHandle, 1));
                error = 0;
                return true;
            }
            finally
            {
                // Nothing else is needed from the handles; release them so nothing is held open
                NativeMethods.CloseHandle(info.ProcessHandle);
                NativeMethods.CloseHandle(info.ThreadHandle);
            }
        }

        identity = default;
        error = Marshal.GetLastPInvokeError();
        return false;
    }

    /// <summary>
    ///     Starts the daemon on a non-Windows platform as a child with its standard streams closed.
    /// </summary>
    /// <param name="executable">The executable path.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The daemon's process id and start time.</returns>
    /// <exception cref="PuppetException">Thrown when the process cannot be started or exited immediately.</exception>
    private static ProcessIdentity LaunchOther(string executable, List<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo) ?? throw new PuppetException("Could not start the session daemon.");

            // The start time is read from the object that started the child, which addresses exactly that child.
            // When it cannot be read (or the pipes cannot be closed) there is no identity for the caller to clean
            // up with, so the child is ended through the same object before the failure propagates.
            return CaptureIdentity(
                () =>
                {
                    var identity = new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime());

                    // Closing our ends means the daemon never blocks on, or holds open, the caller's pipes
                    process.StandardInput.Close();
                    process.StandardOutput.Close();
                    process.StandardError.Close();
                    return identity;
                },
                () => process.Kill(true));
        }
        catch (Win32Exception ex)
        {
            throw new PuppetException($"Could not start the session daemon: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new PuppetException("The session daemon exited immediately after it was started.", ex);
        }
    }
}

/// <summary>
///     The small part of the Windows API needed to create a detached process.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    ///     <c>DETACHED_PROCESS</c>: the new process has no console.
    /// </summary>
    public const uint DetachedProcess = 0x00000008;

    /// <summary>
    ///     <c>CREATE_NEW_PROCESS_GROUP</c>: Ctrl+C aimed at the caller does not reach the new process.
    /// </summary>
    public const uint CreateNewProcessGroup = 0x00000200;

    /// <summary>
    ///     <c>CREATE_BREAKAWAY_FROM_JOB</c>: the new process leaves the caller's job object.
    /// </summary>
    public const uint CreateBreakawayFromJob = 0x01000000;

    /// <summary>
    ///     The <c>STARTUPINFOW</c> structure; pointer-typed string members are left null.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfo
    {
        /// <summary>The structure size in bytes (<c>cb</c>).</summary>
        public uint Size;

        /// <summary>Reserved (<c>lpReserved</c>).</summary>
        public IntPtr Reserved;

        /// <summary>The desktop name (<c>lpDesktop</c>).</summary>
        public IntPtr Desktop;

        /// <summary>The console window title (<c>lpTitle</c>).</summary>
        public IntPtr Title;

        /// <summary>Window x offset (<c>dwX</c>).</summary>
        public uint X;

        /// <summary>Window y offset (<c>dwY</c>).</summary>
        public uint Y;

        /// <summary>Window width (<c>dwXSize</c>).</summary>
        public uint XSize;

        /// <summary>Window height (<c>dwYSize</c>).</summary>
        public uint YSize;

        /// <summary>Console buffer width (<c>dwXCountChars</c>).</summary>
        public uint XCountChars;

        /// <summary>Console buffer height (<c>dwYCountChars</c>).</summary>
        public uint YCountChars;

        /// <summary>Console text attributes (<c>dwFillAttribute</c>).</summary>
        public uint FillAttribute;

        /// <summary>Which members are used (<c>dwFlags</c>).</summary>
        public uint Flags;

        /// <summary>Window show state (<c>wShowWindow</c>).</summary>
        public ushort ShowWindow;

        /// <summary>Reserved (<c>cbReserved2</c>).</summary>
        public ushort Reserved2Size;

        /// <summary>Reserved (<c>lpReserved2</c>).</summary>
        public IntPtr Reserved2;

        /// <summary>Standard input handle (<c>hStdInput</c>).</summary>
        public IntPtr StdInput;

        /// <summary>Standard output handle (<c>hStdOutput</c>).</summary>
        public IntPtr StdOutput;

        /// <summary>Standard error handle (<c>hStdError</c>).</summary>
        public IntPtr StdError;
    }

    /// <summary>
    ///     The <c>PROCESS_INFORMATION</c> structure returned by process creation.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        /// <summary>Handle of the new process (<c>hProcess</c>).</summary>
        public IntPtr ProcessHandle;

        /// <summary>Handle of the primary thread (<c>hThread</c>).</summary>
        public IntPtr ThreadHandle;

        /// <summary>Id of the new process (<c>dwProcessId</c>).</summary>
        public uint ProcessId;

        /// <summary>Id of the primary thread (<c>dwThreadId</c>).</summary>
        public uint ThreadId;
    }

    /// <summary>
    ///     Creates a process (<c>CreateProcessW</c>).
    /// </summary>
    /// <param name="applicationName">The module to run, or <c>null</c> to take it from the command line.</param>
    /// <param name="commandLine">The full command line.</param>
    /// <param name="processAttributes">Process security attributes; zero for default.</param>
    /// <param name="threadAttributes">Thread security attributes; zero for default.</param>
    /// <param name="inheritHandles">Whether the child inherits inheritable handles.</param>
    /// <param name="creationFlags">The creation flags.</param>
    /// <param name="environment">The environment block; zero to inherit.</param>
    /// <param name="currentDirectory">The child's working directory.</param>
    /// <param name="startupInfo">The startup information.</param>
    /// <param name="processInformation">Receives the process information.</param>
    /// <returns><c>true</c> on success; call <see cref="Marshal.GetLastPInvokeError"/> on failure.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcess(
        string? applicationName,
        string commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        in StartupInfo startupInfo,
        out ProcessInformation processInformation);

    /// <summary>
    ///     Closes an operating system handle (<c>CloseHandle</c>).
    /// </summary>
    /// <param name="handle">The handle to close.</param>
    /// <returns><c>true</c> on success.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    /// <summary>
    ///     Reads the creation, exit, kernel, and user times of a process (<c>GetProcessTimes</c>); each is a
    ///     <c>FileTime</c>, 100 nanosecond ticks since 1601 (UTC), which fits a 64 bit integer.
    /// </summary>
    /// <param name="process">A process handle with query access.</param>
    /// <param name="creation">Receives the creation time.</param>
    /// <param name="exit">Receives the exit time (undefined while the process runs).</param>
    /// <param name="kernel">Receives the kernel mode time.</param>
    /// <param name="user">Receives the user mode time.</param>
    /// <returns><c>true</c> on success; call <see cref="Marshal.GetLastPInvokeError"/> on failure.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    /// <summary>
    ///     Ends a process immediately (<c>TerminateProcess</c>).
    /// </summary>
    /// <param name="process">A process handle with terminate access.</param>
    /// <param name="exitCode">The exit code the process reports.</param>
    /// <returns><c>true</c> on success.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(IntPtr process, uint exitCode);
}
