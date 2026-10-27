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

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     The sign-in throttle settings read from the <c>Identity:Throttle</c> configuration section.
/// </summary>
/// <remarks>
///     The settings are bound by the options system, so they can come from <c>appsettings.json</c> or from
///     environment variables such as <c>Identity__Throttle__MaxFailuresPerName</c>. The values are checked
///     at start-up (see <see cref="SignIn.AddSignIn"/>). The type is a plain settings holder and is not
///     thread-safe while it is being bound.
/// </remarks>
internal sealed class SignInThrottleOptions
{
    /// <summary>
    ///     The name of the configuration section that holds <see cref="SignInThrottleOptions"/>.
    /// </summary>
    public const string SectionName = "Identity:Throttle";

    /// <summary>
    ///     The largest accepted value of <see cref="WindowSeconds"/> (one day).
    /// </summary>
    public const int MaximumWindowSeconds = 86400;

    /// <summary>
    ///     Gets or sets how many sign-in attempts one client address may make for one user name within a
    ///     window; must be greater than 0.
    /// </summary>
    public int MaxFailuresPerName { get; set; } = 5;

    /// <summary>
    ///     Gets or sets how many sign-in attempts one client address may make for all user names together
    ///     within a window; must be at least <see cref="MaxFailuresPerName"/>.
    /// </summary>
    public int MaxFailuresPerAddress { get; set; } = 20;

    /// <summary>
    ///     Gets or sets the length of the window in seconds; must be greater than 0 and at most
    ///     <see cref="MaximumWindowSeconds"/>.
    /// </summary>
    public int WindowSeconds { get; set; } = 300;
}
