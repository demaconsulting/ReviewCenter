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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Base class for page tests: a bUnit context wired to the real domain services over an in-memory store.
/// </summary>
/// <remarks>
///     Using the real services (not mocks of them) means the pages are tested against the real validation
///     rules, so what a test sees is what the operator would see.
/// </remarks>
public abstract class PageTestBase : BunitContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PageTestBase"/> class and registers the services.
    /// </summary>
    protected PageTestBase()
    {
        var store = new FakeReviewStore();
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        Projects = new ProjectService(store, clock);
        Reviews = new ReviewService(store, clock);
        Services.AddSingleton(Projects);
        Services.AddSingleton(Reviews);
    }

    /// <summary>
    ///     Gets the project service the pages use.
    /// </summary>
    protected ProjectService Projects { get; }

    /// <summary>
    ///     Gets the review service the pages use.
    /// </summary>
    protected ReviewService Reviews { get; }
}
