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

namespace DemaConsulting.ReviewCenter.ContentSources;

/// <summary>
///     The settings of the git content source, bound from the <c>Git</c> section.
/// </summary>
/// <remarks>
///     The class is a plain settings holder that is only read after start-up. When no cache path is configured
///     the host fills in <c>git-cache</c> beside the database.
/// </remarks>
public sealed class GitOptions
{
    /// <summary>
    ///     The name of the configuration section the options are bound from.
    /// </summary>
    public const string SectionName = "Git";

    /// <summary>
    ///     The default timeout of one git call, in seconds.
    /// </summary>
    public const int DefaultTimeoutSeconds = 120;

    /// <summary>
    ///     The smallest accepted timeout of one git call, in seconds.
    /// </summary>
    public const int MinimumTimeoutSeconds = 1;

    /// <summary>
    ///     The largest accepted timeout of one git call, in seconds.
    /// </summary>
    public const int MaximumTimeoutSeconds = 3600;

    /// <summary>
    ///     Gets or sets the folder that holds the bare mirror clones, one <c>repo-{id}</c> folder per repository.
    /// </summary>
    public string? CachePath { get; set; }

    /// <summary>
    ///     Gets or sets the timeout of every git call, in seconds. It must be from 1 to 3600; the host checks
    ///     this at start-up.
    /// </summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>
    ///     Gets the effective timeout of one git call.
    /// </summary>
    /// <remarks>A value below one (possible only when the class is built directly) falls back to the default.</remarks>
    internal TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds < 1 ? DefaultTimeoutSeconds : TimeoutSeconds);
}
