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
///     The outcome of asking for an attestation.
/// </summary>
internal enum AttestationStatus
{
    /// <summary>The attestation was created.</summary>
    Ok,

    /// <summary>The project or the review does not exist.</summary>
    NotFound,

    /// <summary>The review is not complete: some slot has no valid signature for the current record.</summary>
    NotApproved,

    /// <summary>The review changed while the attestation was being prepared; asking again may succeed.</summary>
    Changed,

    /// <summary>The signing key is not usable; the message says what to correct.</summary>
    KeyUnavailable
}

/// <summary>
///     The result of <see cref="AttestationService.GenerateAsync"/>.
/// </summary>
/// <param name="Status">The outcome.</param>
/// <param name="Content">The bytes of the signed PDF, or <c>null</c> unless the status is <see cref="AttestationStatus.Ok"/>.</param>
/// <param name="FileName">The suggested file name, or <c>null</c> unless the status is <see cref="AttestationStatus.Ok"/>.</param>
/// <param name="Message">An explanation for the other statuses, or <c>null</c>; it contains no secret.</param>
internal sealed record AttestationResult(AttestationStatus Status, byte[]? Content, string? FileName, string? Message);
