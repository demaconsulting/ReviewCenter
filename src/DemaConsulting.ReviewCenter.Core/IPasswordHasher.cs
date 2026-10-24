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
///     Hashes and verifies passwords for <see cref="UserService"/>.
/// </summary>
/// <remarks>
///     The abstraction keeps Core free of any framework: the web project supplies the implementation and
///     unit tests supply a trivial one. Implementations must be safe for concurrent use.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    ///     Hashes a password with a fresh random salt.
    /// </summary>
    /// <param name="password">The password. Must not be null.</param>
    /// <returns>A self-describing hash that <see cref="Verify"/> accepts; never the password itself.</returns>
    string Hash(string password);

    /// <summary>
    ///     Checks a password against a stored hash.
    /// </summary>
    /// <param name="hash">A hash returned by <see cref="Hash"/>.</param>
    /// <param name="password">The password to check.</param>
    /// <returns><c>true</c> when the password matches the hash; otherwise <c>false</c>.</returns>
    bool Verify(string hash, string password);
}
