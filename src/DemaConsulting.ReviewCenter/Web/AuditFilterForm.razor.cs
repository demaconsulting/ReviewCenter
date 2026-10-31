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

using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the audit filter form (<c>AuditFilterForm.razor</c>): the filter bar shared by the
///     whole-trail page and the project audit page.
/// </summary>
/// <remarks>
///     The component only draws what its parent gives it and changes nothing; the form is submitted by the browser
///     as a GET request to the parent page, so the component holds no state of its own and needs no
///     synchronization.
/// </remarks>
public partial class AuditFilterForm : ComponentBase
{
    /// <summary>
    ///     Gets or sets the cleaned filter to show; the parent supplies it.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public AuditFilterState State { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the address of the parent page without a query; the form submits to it and the "Clear
    ///     filters" link leads to it.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public string BasePath { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the projects to offer in a project selector, or <c>null</c> for no selector (the project
    ///     audit page is already limited to its project).
    /// </summary>
    [Parameter]
    public IReadOnlyList<Project>? Projects { get; set; }
}
