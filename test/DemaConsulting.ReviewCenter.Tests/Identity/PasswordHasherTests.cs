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

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Unit tests for the <see cref="PasswordHasher"/> adapter.
/// </summary>
public class PasswordHasherTests
{
    /// <summary>
    ///     Test that only the right password verifies against a hash.
    /// </summary>
    [Fact]
    public void PasswordHasher_Verify_RightAndWrongPassword_OnlyRightSucceeds()
    {
        // Arrange: a hash of a password
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("correct horse battery");

        // Act: verify the right and a wrong password
        var right = hasher.Verify(hash, "correct horse battery");
        var wrong = hasher.Verify(hash, "Correct horse battery");

        // Assert: only the right one is accepted
        Assert.True(right);
        Assert.False(wrong);
    }

    /// <summary>
    ///     Test that a hash that cannot be read does not verify.
    /// </summary>
    [Fact]
    public void PasswordHasher_Verify_GarbageHash_ReturnsFalse()
    {
        // Arrange: a hasher
        var hasher = new PasswordHasher();

        // Act: verify against something that is not a hash
        var result = hasher.Verify("not a hash", "correct horse battery");

        // Assert: refused
        Assert.False(result);
    }
}
