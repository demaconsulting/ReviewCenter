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
///     Adds a digital signature to a PDF.
/// </summary>
internal interface IAttestationSigner
{
    /// <summary>
    ///     Signs a PDF written by <see cref="PdfDocumentWriter"/>.
    /// </summary>
    /// <param name="pdf">The unsigned PDF. Must not be null.</param>
    /// <param name="key">The signing key. Must not be null.</param>
    /// <param name="signedAt">The signing time recorded in the signature.</param>
    /// <param name="reason">The reason shown by PDF readers beside the signature.</param>
    /// <returns>
    ///     The signed PDF: the original bytes unchanged, followed by an incremental update that holds the signature.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the PDF is not one that the signer can extend.</exception>
    /// <exception cref="AttestationKeyException">Thrown when the key cannot sign.</exception>
    byte[] Sign(byte[] pdf, AttestationSigningKey key, DateTimeOffset signedAt, string reason);
}
