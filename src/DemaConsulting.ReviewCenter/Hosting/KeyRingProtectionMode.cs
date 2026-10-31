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
///     How the key files of the Data Protection key ring are protected at rest; the values of the
///     <c>DataProtection:Protection</c> setting.
/// </summary>
/// <remarks>
///     <see cref="Auto"/> is only a request: <see cref="KeyRingProtection.ResolveMode"/> turns it into one of the
///     other three, which are the modes that are actually applied.
/// </remarks>
internal enum KeyRingProtectionMode
{
    /// <summary>
    ///     Choose the mode: a certificate when <c>DataProtection:CertificatePath</c> is set, otherwise Windows DPAPI on
    ///     Windows and owner-only permissions elsewhere.
    /// </summary>
    Auto,

    /// <summary>
    ///     Encrypt the key files with Windows DPAPI, bound to the account (or, by choice, the machine) of the server.
    ///     Only available on Windows.
    /// </summary>
    Dpapi,

    /// <summary>
    ///     Encrypt the key files with the public key of a certificate that the operator supplies as a file; the
    ///     private key is needed to read them.
    /// </summary>
    Certificate,

    /// <summary>
    ///     Leave the key files unencrypted and rely on the owner-only permissions of the key folder and files.
    /// </summary>
    PermissionsOnly
}
