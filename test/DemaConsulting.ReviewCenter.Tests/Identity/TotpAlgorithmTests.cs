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
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Tests of <see cref="TotpAlgorithm"/> against the published vectors of RFC 4226 and RFC 6238 and against the
///     independent reference in <see cref="TotpOracle"/>.
/// </summary>
public class TotpAlgorithmTests
{
    /// <summary>
    ///     The algorithm under test.
    /// </summary>
    private readonly TotpAlgorithm _algorithm = new();

    /// <summary>
    ///     Test that the reference itself reproduces the published HOTP values of RFC 4226 Appendix D, which makes it a
    ///     trustworthy oracle for the other tests.
    /// </summary>
    [Fact]
    public void TotpOracle_Hotp_Rfc4226Values_MatchThePublishedTable()
    {
        // Arrange: the published secret and the ten published values
        var secret = TotpOracle.VectorSecret;

        // Act: compute the values for the counters 0 to 9
        var values = Enumerable.Range(0, 10).Select(counter => TotpOracle.Hotp(secret, counter, 6)).ToList();

        // Assert: they equal the table
        Assert.Equal(TotpOracle.Rfc4226Values, values);
    }

    /// <summary>
    ///     Test that the production code reproduces the published eight digit vectors of RFC 6238 Appendix B.
    /// </summary>
    [Fact]
    public void TotpAlgorithm_ComputeCode_Rfc6238Vectors_MatchThePublishedTable()
    {
        // Arrange: the published secret and vectors
        var secret = TotpOracle.VectorSecret;

        // Act: compute each code with the production algorithm and with the reference
        var actual = TotpOracle.Rfc6238Vectors
            .Select(v => _algorithm.ComputeCode(secret, DateTimeOffset.FromUnixTimeSeconds(v.Time), 8))
            .ToList();
        var reference = TotpOracle.Rfc6238Vectors.Select(v => TotpOracle.Totp(secret, v.Time, 8)).ToList();

        // Assert: both equal the published codes
        var published = TotpOracle.Rfc6238Vectors.Select(v => v.Code).ToList();
        Assert.Equal(published, actual);
        Assert.Equal(published, reference);
    }

    /// <summary>
    ///     Test that the six digit codes of the production code equal the reference for many secrets and instants.
    /// </summary>
    [Fact]
    public void TotpAlgorithm_ComputeCode_ManyRandomSecrets_MatchTheReference()
    {
        // Arrange: a fixed seed so the test is reproducible
        var random = new Random(6238);
        var mismatches = new List<string>();

        // Act: compare 200 random secrets at random instants
        for (var i = 0; i < 200; i++)
        {
            var secret = new byte[20];
            random.NextBytes(secret);
            var time = DateTimeOffset.FromUnixTimeSeconds(random.NextInt64(0, 4_000_000_000));
            var production = _algorithm.ComputeCode(secret, time);
            var reference = TotpOracle.CodeAt(secret, time);
            if (production != reference)
            {
                mismatches.Add($"{time.ToUnixTimeSeconds()}: {production} != {reference}");
            }
        }

        // Assert: no difference
        Assert.Empty(mismatches);
    }

    /// <summary>
    ///     Test that the step the library matches is exactly floor(unix time / 30), for every offset of the window.
    /// </summary>
    /// <param name="offset">The step offset of the code relative to the current step.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void TotpAlgorithm_TryVerify_MatchedStep_IsFloorOfUnixTimeOverThirty(int offset)
    {
        // Arrange: an instant in the middle of a step and the code of the step at the offset, from the reference
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_015);
        var secret = TotpOracle.VectorSecret;
        var code = TotpOracle.CodeAt(secret, now, offset);

        // Act: verify
        var accepted = _algorithm.TryVerify(secret, code, now, out var step);

        // Assert: accepted, and the step is the independently computed one
        Assert.True(accepted);
        Assert.Equal((1_700_000_015 / 30) + offset, step);
        Assert.Equal(1_700_000_015 / 30, _algorithm.StepOf(now));
    }

    /// <summary>
    ///     Test that codes two steps away and codes of other secrets are refused, and nothing is reported as matched.
    /// </summary>
    [Fact]
    public void TotpAlgorithm_TryVerify_OutsideTheWindow_IsRefused()
    {
        // Arrange: the codes two steps away on both sides
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_015);
        var secret = TotpOracle.VectorSecret;
        var older = TotpOracle.CodeAt(secret, now, -2);
        var newer = TotpOracle.CodeAt(secret, now, 2);
        var current = TotpOracle.CodeAt(secret, now);
        var previous = TotpOracle.CodeAt(secret, now, -1);
        var next = TotpOracle.CodeAt(secret, now, 1);

        // Act: verify them (a six digit collision with a window code is possible but not for these fixed values)
        var results = new[] { older, newer }
            .Where(code => code != current && code != previous && code != next)
            .Select(code => _algorithm.TryVerify(secret, code, now, out _))
            .ToList();

        // Assert: both are refused
        Assert.Equal(2, results.Count);
        Assert.All(results, accepted => Assert.False(accepted));
    }

    /// <summary>
    ///     Test that malformed codes are refused.
    /// </summary>
    /// <param name="code">The code to try.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData(" 12345")]
    [InlineData("١٢٣٤٥٦")]
    public void TotpAlgorithm_TryVerify_MalformedCode_IsRefused(string? code)
    {
        // Arrange: a valid secret and instant
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_015);

        // Act: verify
        var accepted = _algorithm.TryVerify(TotpOracle.VectorSecret, code, now, out var step);

        // Assert: refused and the step is zero
        Assert.False(accepted);
        Assert.Equal(0, step);
    }

    /// <summary>
    ///     Test that a generated secret has 160 bits, differs between calls, and survives the Base32 round trip that
    ///     matches the independent reader.
    /// </summary>
    [Fact]
    public void TotpAlgorithm_GenerateSecret_Is160BitsAndRoundTripsThroughBase32()
    {
        // Arrange: two secrets
        var first = _algorithm.GenerateSecret();
        var second = _algorithm.GenerateSecret();

        // Act: encode and decode with the production code and with the reference
        var text = _algorithm.ToBase32(first);
        var viaProduction = _algorithm.TryFromBase32(text);
        var viaReference = TotpOracle.FromBase32(text);

        // Assert: 20 bytes, different, 32 symbols without padding, and every way round gives the same bytes
        Assert.Equal(20, first.Length);
        Assert.NotEqual(first, second);
        Assert.Equal(32, text.Length);
        Assert.DoesNotContain('=', text);
        Assert.Equal(first, viaProduction);
        Assert.Equal(first, viaReference);
        Assert.Equal(TotpOracle.ToBase32(first), text);
    }

    /// <summary>
    ///     Test that Base32 text typed by a person is accepted with spaces, hyphens and lower case, and nonsense is not.
    /// </summary>
    [Fact]
    public void TotpAlgorithm_TryFromBase32_TypedText_IsToleratedAndNonsenseIsRefused()
    {
        // Arrange: the Base32 of the vector secret, written the way an app shows it
        const string Canonical = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        const string Typed = "gezd gnbv-gy3t qojq gezd gnbv gy3t qojq";

        // Act: read both and some nonsense
        var canonical = _algorithm.TryFromBase32(Canonical);
        var typed = _algorithm.TryFromBase32(Typed);

        // Assert: both give the vector secret; nonsense gives nothing
        Assert.Equal(TotpOracle.VectorSecret, canonical);
        Assert.Equal(TotpOracle.VectorSecret, typed);
        Assert.Null(_algorithm.TryFromBase32(null));
        Assert.Null(_algorithm.TryFromBase32(string.Empty));
        Assert.Null(_algorithm.TryFromBase32("not base32!"));
        Assert.Null(_algorithm.TryFromBase32("018"));
    }

    /// <summary>
    ///     Test that the provisioning address has the form authenticator apps read.
    /// </summary>
    [Fact]
    public void TotpAlgorithm_BuildUri_HasTheOtpauthForm()
    {
        // Arrange: the vector secret, an issuer with a space, and an account name with an at sign
        var secret = TotpOracle.VectorSecret;

        // Act: build the address
        var uri = _algorithm.BuildUri(secret, "Review Center", "ann@example.org");

        // Assert: type, label with issuer prefix, secret, issuer, algorithm, digits and period
        Assert.StartsWith("otpauth://totp/", uri, StringComparison.Ordinal);
        Assert.Contains("Review%20Center", uri, StringComparison.Ordinal);
        Assert.Contains("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=Review%20Center", uri, StringComparison.Ordinal);
        Assert.Contains("algorithm=SHA1", uri, StringComparison.Ordinal);
        Assert.Contains("digits=6", uri, StringComparison.Ordinal);
        Assert.Contains("period=30", uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that missing arguments and out-of-range digit counts are refused.
    /// </summary>
    [Fact]
    public void TotpAlgorithm_InvalidArguments_Throw()
    {
        // Arrange: a valid secret
        var secret = TotpOracle.VectorSecret;
        var now = DateTimeOffset.UnixEpoch;

        // Act and Assert: each misuse is refused
        Assert.Throws<ArgumentNullException>(() => _algorithm.ToBase32(null!));
        Assert.Throws<ArgumentNullException>(() => _algorithm.BuildUri(null!, "i", "a"));
        Assert.Throws<ArgumentNullException>(() => _algorithm.BuildUri(secret, null!, "a"));
        Assert.Throws<ArgumentNullException>(() => _algorithm.BuildUri(secret, "i", null!));
        Assert.Throws<ArgumentNullException>(() => _algorithm.ComputeCode(null!, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => _algorithm.ComputeCode(secret, now, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => _algorithm.ComputeCode(secret, now, 9));
        Assert.Throws<ArgumentNullException>(() => _algorithm.TryVerify(null!, "123456", now, out _));
    }
}
