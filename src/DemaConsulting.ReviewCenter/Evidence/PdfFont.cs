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
///     The standard PDF fonts that <see cref="PdfDocumentWriter"/> can use.
/// </summary>
/// <remarks>
///     These are among the 14 standard fonts that every PDF reader provides, so no font is embedded. The order of the
///     values defines the resource names (<c>F1</c> to <c>F5</c>) and the object numbers in the file.
/// </remarks>
internal enum PdfFont
{
    /// <summary>Helvetica, the regular proportional font for running text.</summary>
    Helvetica,

    /// <summary>Helvetica-Bold, for headings and labels.</summary>
    HelveticaBold,

    /// <summary>Helvetica-Oblique, for notes.</summary>
    HelveticaOblique,

    /// <summary>Courier, the fixed-width font for hashes and digests.</summary>
    Courier,

    /// <summary>Courier-Bold, the bold fixed-width font.</summary>
    CourierBold
}
