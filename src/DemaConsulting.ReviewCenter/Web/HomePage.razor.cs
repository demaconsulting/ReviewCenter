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

using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the hello-world landing page (<c>HomePage.razor</c>), holding its state and
///     event handlers.
/// </summary>
/// <remarks>
///     Keeping behavior in a code-behind file leaves the Razor markup purely presentational and
///     lets every member carry full documentation. Each browser connection (circuit) gets its own
///     page instance, so the state here is per-user and needs no synchronization: Blazor Server
///     dispatches all event handlers of one circuit on a single synchronization context.
/// </remarks>
public partial class HomePage : ComponentBase
{
    /// <summary>
    ///     The text shown after the Hello button is clicked.
    /// </summary>
    private const string GreetingText = "Hello from ReviewCenter!";

    /// <summary>
    ///     Backing field for <see cref="IsInteractive"/>; flips to <c>true</c> after the first
    ///     render on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets the greeting to display, or <c>null</c> while the Hello button has not been clicked.
    /// </summary>
    private string? Greeting { get; set; }

    /// <summary>
    ///     Gets the number of times the counter button has been clicked on this circuit.
    /// </summary>
    private int Count { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the page is running on a live interactive circuit.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and clicks cannot be handled; drivers use
    ///     this flag (through the <c>data-interactive</c> attribute) to know when it is safe to click.
    /// </remarks>
    private bool IsInteractive => _interactive;

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    private string InteractiveMarker => IsInteractive ? "true" : "false";

    /// <summary>
    ///     Shows the greeting message in response to the Hello button.
    /// </summary>
    /// <remarks>
    ///     Idempotent: clicking again leaves the same greeting displayed.
    /// </remarks>
    private void SayHello()
    {
        Greeting = GreetingText;
    }

    /// <summary>
    ///     Adds one to the counter in response to the counter button.
    /// </summary>
    private void IncrementCounter()
    {
        Count++;
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit.
    /// </summary>
    /// <param name="firstRender">
    ///     <c>true</c> for the first render of this component instance; the marker is only set then.
    /// </param>
    /// <remarks>
    ///     <c>OnAfterRender</c> is never invoked during static prerendering, so reaching this method
    ///     proves that the circuit is connected. A second render is requested so the new marker
    ///     value reaches the browser.
    /// </remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }
    }
}
