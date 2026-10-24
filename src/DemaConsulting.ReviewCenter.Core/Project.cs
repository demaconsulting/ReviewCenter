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
///     A project: the named container that groups the reviews of one body of work.
/// </summary>
/// <remarks>
///     Project names are not required to be unique; the numeric <see cref="Id"/> is the identity.
///     Instances are created by <see cref="ProjectService"/>, which enforces the rules, and are
///     populated by the storage layer; the properties are therefore initialization-only. The type
///     is not thread-safe.
/// </remarks>
public sealed class Project
{
    /// <summary>
    ///     Gets the identity of the project, assigned by the store when the project is added.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the display name of the project; never empty, at most
    ///     <see cref="ReviewLimits.MaxProjectNameLength"/> characters.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the free-text description of the project; may be empty, at most
    ///     <see cref="ReviewLimits.MaxProjectDescriptionLength"/> characters.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the project was created.
    /// </summary>
    public DateTimeOffset Created { get; init; }
}
