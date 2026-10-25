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
///     The sign-in settings read from the <c>Auth</c> configuration section.
/// </summary>
/// <remarks>
///     The settings are bound by the options system, so they can come from <c>appsettings.json</c>
///     or from environment variables such as <c>Auth__SessionHours</c> and <c>Auth__RevalidationSeconds</c>.
///     The type is a plain settings holder and is not thread-safe while it is being bound.
/// </remarks>
internal sealed class AuthOptions
{
    /// <summary>
    ///     The name of the configuration section that holds <see cref="AuthOptions"/>.
    /// </summary>
    public const string SectionName = "Auth";

    /// <summary>
    ///     Gets or sets how many hours a sign-in lasts without activity (a sliding window); must be greater than 0.
    /// </summary>
    public int SessionHours { get; set; } = 8;

    /// <summary>
    ///     The smallest accepted value of <see cref="RevalidationSeconds"/>.
    /// </summary>
    /// <remarks>
    ///     A shorter interval would turn the re-check into constant database load for no practical gain.
    /// </remarks>
    public const int MinimumRevalidationSeconds = 5;

    /// <summary>
    ///     Gets or sets how many seconds pass between two checks of the signed-in user by an open page; must be
    ///     at least <see cref="MinimumRevalidationSeconds"/>.
    /// </summary>
    /// <remarks>
    ///     An open page cannot see changes made to the user after the page was loaded until the next check, so
    ///     this is also the longest time a disabled, removed or changed user can keep an open page.
    /// </remarks>
    public int RevalidationSeconds { get; set; } = 30;
}
