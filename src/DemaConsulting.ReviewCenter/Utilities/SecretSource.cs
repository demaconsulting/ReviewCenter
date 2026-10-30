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

using System.Security;
using System.Text;

namespace DemaConsulting.ReviewCenter.Utilities;

/// <summary>
///     Resolves a secret that an operator supplies either inline or through a file, and refuses every
///     misconfiguration with a message that names the setting and never contains the secret.
/// </summary>
/// <remarks>
///     A secret in a configuration file that is checked in, or in the environment of a process, is easy to leak;
///     a file that the operating system or a secret store mounts (a systemd credential, a mounted secret) is the
///     safer way to hand a secret to a service. This unit gives every setting that holds a secret the same two
///     forms and the same rules, so that each consumer needs no file handling of its own: the inline value, or the
///     path of a file that holds the secret. Exactly one form may be used; both at once is refused because it
///     is unclear which one the operator meant. The contents of a file are read as UTF-8 and trimmed of
///     surrounding white space, so the newline that editors and <c>echo</c> add does not become part of the secret,
///     and a file larger than <see cref="MaxFileBytes"/> or without any text is refused so that a wrong file (a
///     binary, a log) is found at start-up. A secret from an environment variable needs no form of its own: the
///     configuration system already maps an environment variable to the inline setting. The messages name the
///     settings, and the path of the file (which is operator configuration and not a secret), but never a
///     value or a part of one; no exception carries the text of the file. The class holds no state and is
///     thread-safe; reading a file is its only side effect.
/// </remarks>
internal static class SecretSource
{
    /// <summary>
    ///     The largest accepted file in bytes; a secret is a short text, so anything larger is the wrong file.
    /// </summary>
    internal const int MaxFileBytes = 8 * 1024;

    /// <summary>
    ///     The decoder for the contents of a file; it refuses bytes that are not UTF-8 instead of replacing them,
    ///     so a binary file cannot become a secret by accident.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    ///     Resolves the secret from the inline setting or from the file setting.
    /// </summary>
    /// <param name="value">
    ///     The inline secret; <c>null</c>, empty and white space all mean "not set". It is returned as it is,
    ///     without trimming.
    /// </param>
    /// <param name="valueSetting">The full name of the inline setting, for messages. Must not be null.</param>
    /// <param name="file">
    ///     The path of the file that holds the secret; <c>null</c>, empty and white space all mean "not set". A
    ///     relative path is relative to the working directory of the process.
    /// </param>
    /// <param name="fileSetting">The full name of the file setting, for messages. Must not be null.</param>
    /// <returns>
    ///     The secret: <paramref name="value"/> when only it is set, the trimmed, non-empty contents of the file
    ///     when only <paramref name="file"/> is set, and <c>null</c> when neither is set, so that the caller
    ///     decides whether a missing secret is an error.
    /// </returns>
    /// <remarks>
    ///     Every problem is reported with an <see cref="InvalidOperationException"/> because the callers are
    ///     start-up checks that turn it into the message of the failed start. No message holds the secret.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when a setting name is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when both settings are set (the message names both), when the file does not exist or cannot be
    ///     read (it names the file setting and the path), when the file is larger than
    ///     <see cref="MaxFileBytes"/> or is not valid UTF-8, or when it holds no text after trimming.
    /// </exception>
    internal static string? Resolve(string? value, string valueSetting, string? file, string fileSetting)
    {
        // The names are only used for messages, but a message without a setting is useless to the operator
        ArgumentNullException.ThrowIfNull(valueSetting);
        ArgumentNullException.ThrowIfNull(fileSetting);

        // Exactly one form may be used: two sources could disagree, and silently preferring one hides a mistake
        var hasValue = !string.IsNullOrWhiteSpace(value);
        var hasFile = !string.IsNullOrWhiteSpace(file);
        if (hasValue && hasFile)
        {
            throw new InvalidOperationException(
                $"{valueSetting} and {fileSetting} are both set; set only one of them.");
        }

        // The inline form is used as it is; the file form is read and trimmed; neither means no secret
        if (hasValue)
        {
            return value;
        }

        return hasFile ? ReadFile(file!, fileSetting) : null;
    }

    /// <summary>
    ///     Reads the secret from a file and trims surrounding white space.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="setting">The full name of the file setting, for messages.</param>
    /// <returns>The trimmed, non-empty contents of the file.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the file does not exist, cannot be read, is too large, is not valid UTF-8, or is empty.
    /// </exception>
    private static string ReadFile(string path, string setting)
    {
        // Read at most one byte more than allowed, so an oversized or endless file is never read completely
        var buffer = new byte[MaxFileBytes + 1];
        int length;
        try
        {
            length = ReadBounded(path, buffer);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidOperationException($"{setting} refers to the file '{path}', which does not exist.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or SecurityException)
        {
            throw new InvalidOperationException($"{setting} refers to the file '{path}', which cannot be read.", ex);
        }

        // A secret is a short text; a larger file is a wrong file (a binary, a log) that must not be used
        if (length > MaxFileBytes)
        {
            throw new InvalidOperationException(
                $"{setting} refers to the file '{path}', which is larger than {MaxFileBytes} bytes.");
        }

        // Decode strictly; the exception of the decoder quotes the offending bytes, so it is not passed on
        string text;
        try
        {
            text = StrictUtf8.GetString(buffer, 0, length);
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException(
                $"{setting} refers to the file '{path}', which is not valid UTF-8 text.");
        }

        // Drop a byte order mark and the surrounding white space (usually the final newline) of the file
        var secret = text.TrimStart('\uFEFF').Trim();
        if (secret.Length == 0)
        {
            throw new InvalidOperationException($"{setting} refers to the file '{path}', which is empty.");
        }

        return secret;
    }

    /// <summary>
    ///     Reads a file into a buffer, stopping when the buffer is full.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="buffer">The buffer; its length is the most that is read.</param>
    /// <returns>The number of bytes read; the length of the buffer when the file is at least that long.</returns>
    /// <remarks>
    ///     The file is opened for sharing so that a secret store that replaces the file in place does not make the
    ///     read fail.
    /// </remarks>
    private static int ReadBounded(string path, byte[] buffer)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
