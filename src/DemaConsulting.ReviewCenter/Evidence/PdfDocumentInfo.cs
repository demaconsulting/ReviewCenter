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

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     The document information (metadata) that a PDF reader shows in its document properties.
/// </summary>
/// <remarks>
///     Everything is supplied by the caller, including the creation time, so that the writer never reads a clock
///     and produces identical bytes for identical input. Text may contain any Unicode; it is stored so that a reader
///     shows it faithfully.
/// </remarks>
/// <param name="Title">The document title.</param>
/// <param name="Author">The author; for an attestation, the producing software and version.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Keywords">Keywords; for an attestation, the content fingerprint and review identity.</param>
/// <param name="Creator">The application that created the content.</param>
/// <param name="Producer">The software that produced the PDF file.</param>
/// <param name="Created">The creation (and modification) time.</param>
internal sealed record PdfDocumentInfo(
    string Title,
    string Author,
    string Subject,
    string Keywords,
    string Creator,
    string Producer,
    DateTimeOffset Created);
