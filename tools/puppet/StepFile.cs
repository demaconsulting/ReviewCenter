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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     One validated step of a steps file.
/// </summary>
/// <param name="Number">The 1-based position of the step in the file.</param>
/// <param name="Kind">The step kind: <c>goto</c>, <c>click</c>, <c>fill</c>, <c>text</c>, or <c>screenshot</c>.</param>
/// <param name="Url">The path to open (goto only); starts with <c>/</c>.</param>
/// <param name="Selector">The element selector text (click, fill, text).</param>
/// <param name="Value">The text to enter (fill only).</param>
/// <param name="File">The screenshot file name, ending in <c>.png</c> (screenshot only).</param>
/// <param name="FullPage">Whether the screenshot covers the whole scrollable page (screenshot only).</param>
internal sealed record Step(int Number, string Kind, string? Url, string? Selector, string? Value, string? File, bool FullPage);

/// <summary>
///     Reads and validates a steps file.
/// </summary>
/// <remarks>
///     A steps file is a JSON array of step objects. Validation is strict (unknown properties are an
///     error) so a misspelled property name is reported instead of silently ignored. Stateless.
/// </remarks>
internal static class StepFile
{
    /// <summary>
    ///     The reader settings: comments and trailing commas are tolerated, names are case-insensitive,
    ///     and unknown properties are errors.
    /// </summary>
    private static readonly JsonSerializerOptions ReaderOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    ///     Parses and validates the text of a steps file.
    /// </summary>
    /// <param name="json">The file text.</param>
    /// <param name="fileName">The file name, used in messages.</param>
    /// <returns>The steps, in order.</returns>
    /// <exception cref="PuppetException">Thrown when the text is not valid JSON or a step is invalid.</exception>
    public static IReadOnlyList<Step> Parse(string json, string fileName)
    {
        List<RawStep?>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<RawStep?>>(json, ReaderOptions);
        }
        catch (JsonException ex)
        {
            throw new PuppetException($"Steps file '{fileName}' is not valid JSON: {ex.Message}", ex);
        }

        if (raw is null || raw.Count == 0)
        {
            throw new PuppetException($"Steps file '{fileName}' must contain a JSON array with at least one step.");
        }

        var steps = new List<Step>();
        for (var i = 0; i < raw.Count; i++)
        {
            try
            {
                steps.Add(Validate(i + 1, raw[i]));
            }
            catch (ArgumentException ex)
            {
                var kind = raw[i]?.Kind is { } k ? $" ({k})" : string.Empty;
                throw new PuppetException($"Steps file '{fileName}': step {i + 1}{kind}: {ex.Message}");
            }
        }

        return steps;
    }

    /// <summary>
    ///     Validates one step.
    /// </summary>
    /// <param name="number">The 1-based step number.</param>
    /// <param name="raw">The step as read.</param>
    /// <returns>The validated step.</returns>
    /// <exception cref="ArgumentException">Thrown when the step is invalid; the message names the problem.</exception>
    private static Step Validate(int number, RawStep? raw)
    {
        if (raw?.Kind is null)
        {
            throw new ArgumentException("'kind' is required.");
        }

        switch (raw.Kind)
        {
            case "goto":
                if (raw.Url is null || !raw.Url.StartsWith('/'))
                {
                    throw new ArgumentException("'url' is required and must start with '/'.");
                }

                return new Step(number, raw.Kind, raw.Url, null, null, null, false);

            case "click":
            case "text":
                return new Step(number, raw.Kind, null, RequireSelector(raw), null, null, false);

            case "fill":
                return new Step(number, raw.Kind, null, RequireSelector(raw), raw.Value ?? throw new ArgumentException("'value' is required."), null, false);

            case "screenshot":
                return new Step(number, raw.Kind, null, null, null, ScreenshotFileName(raw.File, number), raw.FullPage);

            default:
                throw new ArgumentException($"unknown kind '{raw.Kind}' (use goto, click, fill, text, or screenshot).");
        }
    }

    /// <summary>
    ///     Gets the selector of a step that needs one.
    /// </summary>
    /// <param name="raw">The step as read.</param>
    /// <returns>The selector text.</returns>
    /// <exception cref="ArgumentException">Thrown when the selector is missing or blank.</exception>
    private static string RequireSelector(RawStep raw)
    {
        return string.IsNullOrWhiteSpace(raw.Selector) ? throw new ArgumentException("'selector' is required.") : raw.Selector;
    }

    /// <summary>
    ///     Validates a screenshot file name and gives it the <c>.png</c> extension.
    /// </summary>
    /// <param name="file">The name from the file, or <c>null</c> for the default.</param>
    /// <param name="number">The step number, used for the default name.</param>
    /// <returns>The file name.</returns>
    /// <exception cref="ArgumentException">Thrown when the name contains a path.</exception>
    private static string ScreenshotFileName(string? file, int number)
    {
        if (file is null)
        {
            return $"step-{number}.png";
        }

        if (string.IsNullOrWhiteSpace(file) || file.Contains('/') || file.Contains('\\') || file.Contains(".."))
        {
            throw new ArgumentException("'file' must be a plain file name without '/', '\\' or '..'.");
        }

        return file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? file : file + ".png";
    }

    /// <summary>
    ///     A step exactly as it appears in the JSON file, before validation.
    /// </summary>
    /// <param name="Kind">The step kind.</param>
    /// <param name="Url">The path to open.</param>
    /// <param name="Selector">The selector text.</param>
    /// <param name="Value">The text to enter.</param>
    /// <param name="File">The screenshot file name.</param>
    /// <param name="FullPage">Whether the screenshot covers the whole page.</param>
    private sealed record RawStep(string? Kind, string? Url, string? Selector, string? Value, string? File, bool FullPage);
}
