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

using System.Net;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Utilities;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks that the real web host starts, answers requests and stops cleanly, on a loopback port chosen by the
///     operating system and over a temporary configuration and database.
/// </summary>
/// <remarks>
///     The host is the production <see cref="ServerHost.CreateApplication"/>, built fully isolated
///     (<see cref="ServerHostOptions.IsolatedSettings"/>): no <c>appsettings.json</c>, no <c>ASPNETCORE_</c> or
///     <c>DOTNET_</c> variable and no other environment variable is read, so a broken or hostile configuration of
///     the machine can neither break the check nor move it to another address. The only settings are the temporary
///     ones, the configured database is not opened and the configured port is not bound; the single address is
///     <c>http://127.0.0.1:0</c>, which makes the operating system pick a free loopback port, and the check fails
///     if the host listens anywhere else. It requests the login page and an embedded static asset and then stops
///     the host. Nothing but loopback is used. The class is stateless and thread-safe.
/// </remarks>
internal static class HostStartCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_HostStart";

    /// <summary>
    ///     The embedded static asset that is requested; the Blazor client script ships inside the framework.
    /// </summary>
    private const string AssetPath = "/_framework/blazor.web.js";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The loopback port the host listened on and the paths that answered.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the host does not start, answers wrongly or does not stop.</exception>
    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        // Disposal order matters: the host (and its database connections) is stopped and disposed first, the
        // temporary folder is deleted last
        using var folder = new TemporaryDirectory();

        // Port 0: the operating system picks a free loopback port, never the configured one
        using var context = Context.Create(["serve", "--silent", "--urls", "http://127.0.0.1:0"]);
        var app = ServerHost.CreateApplication(
            context,
            new ServerHostOptions { IsolatedSettings = SelfTestServices.CreateSettings(folder) });

        try
        {
            await app.StartAsync(cancellationToken);
            var address = ExpectOneLoopbackAddress(app.Urls);

            // A page and an embedded asset, over a client that neither redirects nor uses a proxy
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
            await ExpectOkAsync(client, "/login", cancellationToken);
            await ExpectOkAsync(client, AssetPath, cancellationToken);

            await app.StopAsync(cancellationToken);
            return $"listened on 127.0.0.1:{address.Port}; /login and {AssetPath} returned 200; stopped cleanly";
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    ///     Requires that the host listens on exactly one address and that it is a loopback address with a real port.
    /// </summary>
    /// <param name="urls">The addresses the started host reports.</param>
    /// <returns>The one address.</returns>
    /// <exception cref="ValidationFailedException">
    ///     Thrown when the host listens on no address, on more than one, or on an address that is not loopback.
    /// </exception>
    /// <remarks>
    ///     A configuration that adds a second endpoint (for example one for all interfaces) must fail the check
    ///     instead of being tested through the first address and ignored.
    /// </remarks>
    internal static Uri ExpectOneLoopbackAddress(IEnumerable<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        var addresses = urls.ToList();
        ValidationFailedException.ThrowUnless(
            addresses.Count == 1,
            $"The host listened on {addresses.Count} addresses instead of exactly one loopback address.");

        var address = new Uri(addresses[0]);
        ValidationFailedException.ThrowUnless(
            IPAddress.TryParse(address.Host, out var ip) && IPAddress.IsLoopback(ip) && address.Port > 0,
            "The host did not listen on a loopback address.");
        return address;
    }

    /// <summary>
    ///     Requests a path and requires the answer 200.
    /// </summary>
    /// <param name="client">The client of the host.</param>
    /// <param name="path">The path to request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the answer was checked.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the status code is not 200.</exception>
    private static async Task ExpectOkAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        ValidationFailedException.ThrowUnless(
            response.StatusCode == HttpStatusCode.OK,
            $"GET {path} returned {(int)response.StatusCode} instead of 200.");
    }
}
