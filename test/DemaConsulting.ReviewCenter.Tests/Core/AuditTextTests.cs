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

using System.Security.Cryptography;
using System.Text;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Tests for <see cref="AuditText"/> and <see cref="AuditDraft"/>.
/// </summary>
public class AuditTextTests
{
    /// <summary>
    ///     Test that plain text is kept as it is.
    /// </summary>
    [Fact]
    public void AuditText_Clean_PlainText_IsUnchanged()
    {
        // Arrange / Act / Assert
        Assert.Equal("project 'Apollo'", AuditText.Clean("project 'Apollo'", 100));
    }

    /// <summary>
    ///     Test that null becomes empty and outer white space is removed.
    /// </summary>
    [Fact]
    public void AuditText_Clean_NullAndPadded_AreEmptyAndTrimmed()
    {
        // Arrange / Act / Assert
        Assert.Equal(string.Empty, AuditText.Clean(null, 10));
        Assert.Equal("a b", AuditText.Clean("  a b \t", 10));
    }

    /// <summary>
    ///     Test that control characters and line separators become spaces so the text stays on one line.
    /// </summary>
    [Fact]
    public void AuditText_Clean_ControlCharactersAndLineBreaks_BecomeSpaces()
    {
        // Arrange / Act
        var cleaned = AuditText.Clean("a\r\nb\u0000c\u2028d\u2029e", 100);

        // Assert
        Assert.Equal("a  b c d e", cleaned);
        Assert.DoesNotContain('\n', cleaned);
    }

    /// <summary>
    ///     Test that a valid surrogate pair is kept and a lone surrogate becomes the replacement character.
    /// </summary>
    [Fact]
    public void AuditText_Clean_Surrogates_PairKeptLoneReplaced()
    {
        // Arrange / Act / Assert
        Assert.Equal("a\U0001F600b", AuditText.Clean("a\U0001F600b", 100));
        Assert.Equal("a\uFFFDb", AuditText.Clean("a\uD800b", 100));
        Assert.Equal("a\uFFFDb", AuditText.Clean("a\uDC00b", 100));
        Assert.Equal("a" + (char)0xFFFD, AuditText.Clean("a\uD800", 100));
    }

    /// <summary>
    ///     Test that text within the limit is kept and longer text is cut to exactly the limit with an ellipsis.
    /// </summary>
    [Fact]
    public void AuditText_Clean_LongText_IsCutWithEllipsisWithinLimit()
    {
        // Arrange
        var exact = new string('x', 20);
        var longer = new string('x', 21);

        // Act
        var kept = AuditText.Clean(exact, 20);
        var cut = AuditText.Clean(longer, 20);

        // Assert
        Assert.Equal(exact, kept);
        Assert.Equal(20, cut.Length);
        Assert.EndsWith("...", cut, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a cut never splits a surrogate pair.
    /// </summary>
    [Fact]
    public void AuditText_Clean_CutInsideSurrogatePair_DoesNotSplitThePair()
    {
        // Arrange: the emoji would start at the last kept position
        var text = new string('x', 6) + "\U0001F600" + new string('y', 10);

        // Act
        var cut = AuditText.Clean(text, 10);

        // Assert
        Assert.True(cut.Length <= 10);
        Assert.EndsWith("...", cut, StringComparison.Ordinal);
        Assert.DoesNotContain(cut, c => char.IsSurrogate(c));
    }

    /// <summary>
    ///     Test that a limit that cannot hold the ellipsis is refused.
    /// </summary>
    [Fact]
    public void AuditText_Clean_LimitBelowFour_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => AuditText.Clean("abc", 3));
    }

    /// <summary>
    ///     Test that the short hash is the first eight hex characters of the SHA-256 of the trimmed UTF-8 text.
    /// </summary>
    [Fact]
    public void AuditText_ShortHash_Text_IsPrefixOfSha256OfTrimmedText()
    {
        // Arrange
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("hello")))[..8];

        // Act
        var hash = AuditText.ShortHash("  hello \n");

        // Assert
        Assert.Equal(expected, hash);
        Assert.Equal("2cf24dba", hash);
    }

    /// <summary>
    ///     Test that the short hash of null equals that of empty text and that different text differs.
    /// </summary>
    [Fact]
    public void AuditText_ShortHash_NullAndDifferentText_BehavesAsEmptyAndDiffers()
    {
        // Arrange / Act / Assert
        Assert.Equal(AuditText.ShortHash(string.Empty), AuditText.ShortHash(null));
        Assert.NotEqual(AuditText.ShortHash("a"), AuditText.ShortHash("b"));
        Assert.Equal("e3b0c442", AuditText.ShortHash(string.Empty));
    }

    /// <summary>
    ///     Test that a draft by a signed-in actor names the display name and user name.
    /// </summary>
    [Fact]
    public void AuditDraft_By_SignedInActor_NamesDisplayNameAndUserName()
    {
        // Arrange / Act
        var draft = AuditDraft.By(new Actor("ann", "Ann Lee"), "x.y", "detail", 3, "rev");

        // Assert
        Assert.Equal("Ann Lee (ann)", draft.ActorName);
        Assert.Equal("ann", draft.ActorUsername);
        Assert.Equal("x.y", draft.Action);
        Assert.Equal("detail", draft.Detail);
        Assert.Equal(3, draft.ProjectId);
        Assert.Equal("rev", draft.ReviewId);
    }

    /// <summary>
    ///     Test that a draft by the command line actor names only its display name.
    /// </summary>
    [Fact]
    public void AuditDraft_By_CommandLineActor_NamesOnlyTheDisplayName()
    {
        // Arrange / Act
        var draft = AuditDraft.By(Actor.CommandLine, "user.created", "user 'bob'");

        // Assert
        Assert.Equal("command line", draft.ActorName);
        Assert.Null(draft.ProjectId);
        Assert.Null(draft.ReviewId);
    }
}
