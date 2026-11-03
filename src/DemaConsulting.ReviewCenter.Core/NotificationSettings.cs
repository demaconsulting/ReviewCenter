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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     The settings of e-mail notifications that the domain services need, handed over by the host.
/// </summary>
/// <remarks>
///     The host reads and validates the <c>Notifications</c> configuration section and passes only what the
///     services use, so Core knows nothing of configuration files or secrets. The record holds no secret and is
///     immutable and thread-safe.
/// </remarks>
/// <param name="Enabled">Whether the administrator switched e-mail notifications on.</param>
/// <param name="InstanceName">The name of the installation as it appears in the mails (the display name of the sender).</param>
/// <param name="MaxMailsPerHour">The most mails the server sends in one hour, over all users and kinds of mail.</param>
public sealed record NotificationSettings(bool Enabled, string InstanceName, int MaxMailsPerHour)
{
    /// <summary>
    ///     The name used for the installation when the administrator does not choose one.
    /// </summary>
    public const string DefaultInstanceName = "ReviewCenter";

    /// <summary>
    ///     The default of <see cref="MaxMailsPerHour"/>.
    /// </summary>
    public const int DefaultMaxMailsPerHour = 200;

    /// <summary>
    ///     Gets the settings of a server that sends no e-mail.
    /// </summary>
    public static NotificationSettings Off { get; } = new(false, DefaultInstanceName, DefaultMaxMailsPerHour);
}
