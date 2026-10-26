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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Decides whether file bytes are text and defines what a "line" of a text file is.
/// </summary>
/// <remarks>
///     The review page shows the lines and <see cref="ThreadService"/> validates line ranges against
///     them; both use this one definition so they can never disagree about how many lines a file has.
///     The class is stateless and thread-safe.
/// </remarks>
public static class FileText
{
    /// <summary>
    ///     A strict UTF-8 decoder that throws on invalid bytes, used to tell text from binary content.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    ///     Decodes file bytes as text when they are text.
    /// </summary>
    /// <param name="content">The exact file bytes. Must not be null.</param>
    /// <returns>The text, or <c>null</c> when the bytes contain a NUL byte or are not valid UTF-8 (a binary file).</returns>
    public static string? TryDecode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // A NUL byte is never part of a text file, even when the rest happens to be valid UTF-8
        if (Array.IndexOf(content, (byte)0) >= 0)
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Decodes file bytes and splits them into lines, in one step.
    /// </summary>
    /// <remarks>
    ///     This is the single entry point that the thread validation, the line diff and the review page use to
    ///     obtain the lines of a file, so they can never disagree about line numbers (CRLF handling and the
    ///     final line terminator are decided once, in <see cref="SplitLines"/>).
    /// </remarks>
    /// <param name="content">The exact file bytes. Must not be null.</param>
    /// <returns>The lines (see <see cref="SplitLines"/>), or <c>null</c> when the bytes are binary (see <see cref="TryDecode"/>).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    public static IReadOnlyList<string>? TryReadLines(byte[] content)
    {
        var text = TryDecode(content);
        return text is null ? null : SplitLines(text);
    }

    /// <summary>
    ///     Splits text into lines.
    /// </summary>
    /// <param name="text">The text to split. Must not be null.</param>
    /// <returns>
    ///     The lines without their terminators. Lines end at <c>\n</c> and one <c>\r</c> just before it is
    ///     dropped, so CRLF and LF files count alike; a final line terminator does not start another line;
    ///     empty text has no lines; a lone <c>\r</c> not followed by <c>\n</c> (inside a line or at the very
    ///     end of the text) is kept.
    /// </returns>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split('\n').ToList();

        // The text after a final terminator is not a line; every other segment up to the last was ended by '\n'
        var terminated = lines.Count - 1;
        if (lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        // A '\r' belongs to a CRLF terminator only when '\n' follows it, so the last segment keeps its own
        return lines.Select((line, index) => index < terminated && line.EndsWith('\r') ? line[..^1] : line).ToList();
    }
}
