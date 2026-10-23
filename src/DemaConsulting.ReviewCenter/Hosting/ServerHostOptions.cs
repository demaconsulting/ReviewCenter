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

namespace DemaConsulting.ReviewCenter.Hosting;

/// <summary>
///     Optional knobs that let a caller adjust how <see cref="ServerHost"/> builds the web application.
/// </summary>
/// <remarks>
///     The options exist so that the production path (<c>serve</c>) and the test path share one
///     composition root. Production code leaves every option at its default; tests use them to swap
///     the network server for an in-memory one without duplicating the endpoint wiring, which
///     would let the tested pipeline drift away from the shipped one. The type is immutable after
///     construction and therefore safe to share between threads.
/// </remarks>
internal sealed class ServerHostOptions
{
    /// <summary>
    ///     Gets a value indicating whether the static web asset endpoints (such as the Blazor
    ///     client script) are mapped.
    /// </summary>
    /// <remarks>
    ///     Defaults to <c>true</c>. Static asset mapping reads a manifest generated next to the
    ///     application assembly; an in-memory test host created from a different assembly has no
    ///     such manifest and would throw at start-up, so tests set this to <c>false</c>.
    /// </remarks>
    public bool MapStaticAssets { get; init; } = true;

    /// <summary>
    ///     Gets an optional callback invoked on the web application builder after the standard
    ///     services are registered but before the application is built.
    /// </summary>
    /// <remarks>
    ///     Tests use the callback to select the in-memory test server and to attach a capturing
    ///     logger provider. <c>null</c> (the default) means no extra configuration is applied.
    /// </remarks>
    public Action<WebApplicationBuilder>? ConfigureBuilder { get; init; }
}
