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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Web;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for <see cref="ReviewLinks"/>: the addresses of a review, of a file (at a line) and of a thread.
///     The round trip through the router is tested with the page in <see cref="ReviewDeepLinkTests"/>.
/// </summary>
public class ReviewLinksTests
{
    /// <summary>
    ///     Test that a review address is built from the project and the escaped review id.
    /// </summary>
    [Fact]
    public void ReviewLinks_ForReview_ProjectAndReview_BuildsAddress()
    {
        // Arrange: (none)

        // Act: build the addresses
        var plain = ReviewLinks.ForReview(3, "REV-1");
        var escaped = ReviewLinks.ForReview(3, "a b/c");

        // Assert: the id is escaped as one segment
        Assert.Equal("/projects/3/reviews/REV-1", plain);
        Assert.Equal("/projects/3/reviews/a%20b%2Fc", escaped);
    }

    /// <summary>
    ///     Test that every segment of a file path is escaped on its own and the slashes between them are kept.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="expected">The expected address.</param>
    [Theory]
    [InlineData("a.txt", "/projects/3/reviews/REV-1/files/a.txt")]
    [InlineData("src/a.cs", "/projects/3/reviews/REV-1/files/src/a.cs")]
    [InlineData("a/b/c/d/e.txt", "/projects/3/reviews/REV-1/files/a/b/c/d/e.txt")]
    [InlineData("my docs/read me.txt", "/projects/3/reviews/REV-1/files/my%20docs/read%20me.txt")]
    [InlineData("c#/d#.cs", "/projects/3/reviews/REV-1/files/c%23/d%23.cs")]
    [InlineData("what?/q?.txt", "/projects/3/reviews/REV-1/files/what%3F/q%3F.txt")]
    [InlineData("100%/done%.txt", "/projects/3/reviews/REV-1/files/100%25/done%25.txt")]
    [InlineData("a%2Fb.txt", "/projects/3/reviews/REV-1/files/a%252Fb.txt")]
    [InlineData("a+b&c=d.txt", "/projects/3/reviews/REV-1/files/a%2Bb%26c%3Dd.txt")]
    [InlineData("naïve/日本.txt", "/projects/3/reviews/REV-1/files/na%C3%AFve/%E6%97%A5%E6%9C%AC.txt")]
    public void ReviewLinks_ForFile_Path_EscapesEachSegment(string path, string expected)
    {
        // Arrange: (none)

        // Act: build the address
        var address = ReviewLinks.ForFile(3, "REV-1", path);

        // Assert: escaped per segment, never double escaped
        Assert.Equal(expected, address);
    }

    /// <summary>
    ///     Test that a line link carries the line and the numbering, after the escaped path.
    /// </summary>
    [Fact]
    public void ReviewLinks_ForFile_WithLine_AddsLineAndSide()
    {
        // Arrange: (none)

        // Act: build a head line link and a baseline line link
        var head = ReviewLinks.ForFile(3, "REV-1", "src/a b.cs", 7);
        var baseline = ReviewLinks.ForFile(3, "REV-1", "src/a b.cs", 12, ThreadSide.Baseline);

        // Assert: the query follows the path, the head being the default numbering
        Assert.Equal("/projects/3/reviews/REV-1/files/src/a%20b.cs?line=7&side=head", head);
        Assert.Equal("/projects/3/reviews/REV-1/files/src/a%20b.cs?line=12&side=baseline", baseline);
    }

    /// <summary>
    ///     Test that a side without a line adds no query.
    /// </summary>
    [Fact]
    public void ReviewLinks_ForFile_SideWithoutLine_AddsNoQuery()
    {
        // Arrange: (none)

        // Act: build the address
        var address = ReviewLinks.ForFile(3, "REV-1", "a.txt", null, ThreadSide.Baseline);

        // Assert: no query
        Assert.Equal("/projects/3/reviews/REV-1/files/a.txt", address);
    }

    /// <summary>
    ///     Test that a thread address is built from the project, the review and the thread identity.
    /// </summary>
    [Fact]
    public void ReviewLinks_ForThread_Thread_BuildsAddress()
    {
        // Arrange: (none)

        // Act: build the address
        var address = ReviewLinks.ForThread(3, "REV-1", 12);

        // Assert: the thread follows the review
        Assert.Equal("/projects/3/reviews/REV-1/threads/12", address);
    }

    /// <summary>
    ///     Test that an attestation address is built from the project and the (escaped) review identifier.
    /// </summary>
    [Fact]
    public void ReviewLinks_ForAttestation_ProjectAndReview_BuildsAddress()
    {
        // Arrange: (none)

        // Act: build the addresses
        var plain = ReviewLinks.ForAttestation(3, "REV-1");
        var escaped = ReviewLinks.ForAttestation(3, "a b/c");

        // Assert: the file name of the endpoint follows the review, and the identifier is escaped
        Assert.Equal("/projects/3/reviews/REV-1/attestation.pdf", plain);
        Assert.Equal("/projects/3/reviews/a%20b%2Fc/attestation.pdf", escaped);
    }

    /// <summary>
    ///     Test that every address is a local path that is safe as a sign-in return address.
    /// </summary>
    [Fact]
    public void ReviewLinks_AnyAddress_IsLocalAndSurvivesTheReturnUrlCheck()
    {
        // Arrange: addresses of every kind, with hostile-looking file names
        var addresses = new[]
        {
            ReviewLinks.ForReview(1, "REV-1"),
            ReviewLinks.ForFile(1, "REV-1", "//evil.example/x", 2, ThreadSide.Baseline),
            ReviewLinks.ForFile(1, "REV-1", "\\evil\\x"),
            ReviewLinks.ForThread(1, "REV-1", 4),
            ReviewLinks.ForAttestation(1, "REV-1")
        };

        // Act and Assert: the sign-in check keeps each address as it is
        Assert.All(addresses, address => Assert.Equal(address, DemaConsulting.ReviewCenter.Identity.SignIn.SafeReturnUrl(address)));
    }

    /// <summary>
    ///     Test that missing arguments are refused.
    /// </summary>
    [Fact]
    public void ReviewLinks_NullArguments_Throw()
    {
        // Arrange and Act and Assert: null review ids and paths are refused
        Assert.Throws<ArgumentNullException>(() => ReviewLinks.ForReview(1, null!));
        Assert.Throws<ArgumentNullException>(() => ReviewLinks.ForFile(1, "REV-1", null!));
        Assert.Throws<ArgumentNullException>(() => ReviewLinks.ForFile(1, null!, "a.txt"));
        Assert.Throws<ArgumentNullException>(() => ReviewLinks.ForThread(1, null!, 1));
    }
}
