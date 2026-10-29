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

using DemaConsulting.ReviewCenter.Identity;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks that the password hasher accepts the right password and refuses a wrong one.
/// </summary>
/// <remarks>
///     The check calls the production <see cref="PasswordHasher"/> with a throw-away password. It needs no
///     files. The class is stateless and thread-safe.
/// </remarks>
internal static class PasswordHashCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_PasswordHash";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A short description of what was verified.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the right password fails or a wrong one passes.</exception>
    public static Task<string> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasher = new PasswordHasher();

        var hash = hasher.Hash(SelfTestServices.SelfTestSecret);
        ValidationFailedException.ThrowUnless(
            hash != SelfTestServices.SelfTestSecret && !hash.Contains(SelfTestServices.SelfTestSecret, StringComparison.Ordinal),
            "The password hash contains the password.");
        ValidationFailedException.ThrowUnless(
            hasher.Verify(hash, SelfTestServices.SelfTestSecret), "The right password was refused.");
        ValidationFailedException.ThrowUnless(
            !hasher.Verify(hash, SelfTestServices.SelfTestSecret + "x"), "A wrong password was accepted.");
        ValidationFailedException.ThrowUnless(
            hasher.Hash(SelfTestServices.SelfTestSecret) != hash, "Two hashes of the same password are identical (no salt).");

        return Task.FromResult("right password accepted, wrong password refused, hashes salted");
    }
}
