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
///     Unit tests for <see cref="PersonalAccessTokenService"/> using in-memory stores and a clock the tests can move.
/// </summary>
public class PersonalAccessTokenServiceTests
{
    /// <summary>
    ///     The instant at which each test's clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Test that a new token has the recognizable prefix, 256 bits of base64url secret and is different each time.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Create_NewToken_HasPrefixAndHighEntropyValue()
    {
        // Arrange: a service with a user
        var (service, _, _, _, _) = await CreateAsync();

        // Act: create two tokens
        var first = await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);
        var second = await service.CreateAsync(TestActors.Ann, "laptop", 90, TestContext.Current.CancellationToken);

        // Assert: prefix, then 43 base64url characters (32 bytes), and the two values differ
        Assert.StartsWith("rcpat_", first.Value, StringComparison.Ordinal);
        var secret = first.Value["rcpat_".Length..];
        Assert.Equal(43, secret.Length);
        Assert.All(secret, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        Assert.Equal(32, Convert.FromBase64String(secret.Replace('-', '+').Replace('_', '/') + "=").Length);
        Assert.NotEqual(first.Value, second.Value);
    }

    /// <summary>
    ///     Test that the store holds only the SHA-256 hash of the value, never the value itself.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Create_NewToken_StoresOnlyTheSha256Hash()
    {
        // Arrange: a service with a user
        var (service, tokens, _, _, _) = await CreateAsync();

        // Act: create a token
        var created = await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);

        // Assert: the stored hash is the lowercase hex SHA-256 of the value, and no stored text is the value
        var row = Assert.Single(tokens.Rows);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(created.Value))), row.TokenHash);
        Assert.DoesNotContain(created.Value, new[] { row.Name, row.DisplayId, row.TokenHash });
    }

    /// <summary>
    ///     Test that the display id is a short random text that is not taken from the secret, so showing it
    ///     reveals nothing about the secret.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Create_DisplayId_IsShortRandomAndIndependentOfTheSecret()
    {
        // Arrange: a service with a user
        var (service, _, _, _, _) = await CreateAsync();

        // Act: create several tokens
        var created = new List<CreatedPersonalAccessToken>();
        for (var i = 0; i < 5; i++)
        {
            created.Add(await service.CreateAsync(TestActors.Ann, $"token {i}", 90, TestContext.Current.CancellationToken));
        }

        // Assert: eight characters of the display alphabet, never part of the value, different between tokens
        Assert.All(created, c =>
        {
            Assert.Equal(8, c.Token.DisplayId.Length);
            Assert.All(c.Token.DisplayId, ch => Assert.Contains(ch, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
            Assert.DoesNotContain(c.Token.DisplayId, c.Value, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(created.Count, created.Select(c => c.Token.DisplayId).Distinct().Count());
    }

    /// <summary>
    ///     Test that the list of tokens never carries a token value: not in any field, and not recoverable from the hash.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_List_AfterCreate_NeverReturnsTheValue()
    {
        // Arrange: a token has been created
        var (service, _, _, _, _) = await CreateAsync();
        var created = await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);

        // Act: list the tokens
        var list = await service.ListAsync(TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: the list has the token's name and display id, and no text field holds the value
        var token = Assert.Single(list);
        Assert.Equal("build server", token.Name);
        Assert.Equal(created.Token.DisplayId, token.DisplayId);
        Assert.DoesNotContain(created.Value, new[] { token.Name, token.DisplayId, token.TokenHash });
    }

    /// <summary>
    ///     Test that the three expiry choices set the expiry and anything else is refused.
    /// </summary>
    /// <param name="days">The requested lifetime in days.</param>
    /// <param name="accepted">Whether the lifetime is one of the choices.</param>
    [Theory]
    [InlineData(30, true)]
    [InlineData(90, true)]
    [InlineData(365, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(7, false)]
    [InlineData(366, false)]
    public async Task PersonalAccessTokenService_Create_ExpiryChoices_SetsExpiryOrRejectsOthers(int days, bool accepted)
    {
        // Arrange: a service with a user
        var (service, tokens, _, _, _) = await CreateAsync();

        // Act: create a token with the lifetime
        var attempt = () => service.CreateAsync(TestActors.Ann, "build server", days, TestContext.Current.CancellationToken);

        // Assert: accepted lifetimes set the expiry from the clock; others are refused and store nothing
        if (accepted)
        {
            var created = await attempt();
            Assert.Equal(_now, created.Token.Created);
            Assert.Equal(_now.AddDays(days), created.Token.Expires);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<DomainRuleException>(attempt);
            Assert.Equal(DomainRule.TokenExpiryInvalid, exception.Rule);
            Assert.Empty(tokens.Rows);
        }
    }

    /// <summary>
    ///     Test that invalid names are refused and the valid name is trimmed.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="valid">Whether the name is acceptable.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("bad\u0007name", false)]
    [InlineData("  ci server  ", true)]
    public async Task PersonalAccessTokenService_Create_Name_IsValidatedAndTrimmed(string? name, bool valid)
    {
        // Arrange: a service with a user
        var (service, _, _, _, _) = await CreateAsync();

        // Act: create a token with the name
        var attempt = () => service.CreateAsync(TestActors.Ann, name, 90, TestContext.Current.CancellationToken);

        // Assert: a valid name is stored trimmed; an invalid one names the rule
        if (valid)
        {
            Assert.Equal("ci server", (await attempt()).Token.Name);
        }
        else
        {
            Assert.Equal(DomainRule.TokenNameInvalid, (await Assert.ThrowsAsync<DomainRuleException>(attempt)).Rule);
        }
    }

    /// <summary>
    ///     Test that a name over the length limit is refused and one at the limit is accepted.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Create_NameLength_HasInclusiveLimit()
    {
        // Arrange: a service with a user
        var (service, _, _, _, _) = await CreateAsync();

        // Act: create a token with a name at the limit, and one character longer
        var atLimit = await service.CreateAsync(TestActors.Ann, new string('a', ReviewLimits.MaxTokenNameLength), 90, TestContext.Current.CancellationToken);
        var tooLong = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(TestActors.Ann, new string('a', ReviewLimits.MaxTokenNameLength + 1), 90, TestContext.Current.CancellationToken));

        // Assert: the first is stored, the second names the rule
        Assert.Equal(ReviewLimits.MaxTokenNameLength, atLimit.Token.Name.Length);
        Assert.Equal(DomainRule.TokenNameInvalid, tooLong.Rule);
    }

    /// <summary>
    ///     Test that a token stops working when its expiry time has come.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Authenticate_AfterExpiry_IsRejected()
    {
        // Arrange: a token that expires in 30 days
        var (service, _, _, clock, _) = await CreateAsync();
        var created = await service.CreateAsync(TestActors.Ann, "build server", 30, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        var justBefore = await service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken);

        // Act: authenticate at the expiry instant
        clock.Advance(TimeSpan.FromSeconds(1));
        var atExpiry = await service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken);

        // Assert: valid one second before, refused as expired from the expiry on
        Assert.True(justBefore.Succeeded);
        Assert.False(atExpiry.Succeeded);
        Assert.Equal(TokenFailure.Expired, atExpiry.Failure);
    }

    /// <summary>
    ///     Test that the twenty-first active token is refused.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Create_TwentyActive_RejectsTheTwentyFirst()
    {
        // Arrange: twenty active tokens
        var (service, tokens, _, _, _) = await CreateAsync();
        for (var i = 0; i < ReviewLimits.MaxActiveTokensPerUser; i++)
        {
            await service.CreateAsync(TestActors.Ann, $"token {i}", 90, TestContext.Current.CancellationToken);
        }

        // Act: create one more
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(TestActors.Ann, "one too many", 90, TestContext.Current.CancellationToken));

        // Assert: refused with the rule, nothing more is stored
        Assert.Equal(DomainRule.TokenLimitReached, exception.Rule);
        Assert.Equal(ReviewLimits.MaxActiveTokensPerUser, tokens.Rows.Count);
    }

    /// <summary>
    ///     Test that revoked and expired tokens do not count toward the limit of active tokens.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Create_RevokedAndExpiredDoNotCount()
    {
        // Arrange: ten tokens that will expire, then ten active tokens
        var (service, _, _, clock, _) = await CreateAsync();
        for (var i = 0; i < 10; i++)
        {
            await service.CreateAsync(TestActors.Ann, $"short {i}", 30, TestContext.Current.CancellationToken);
        }

        clock.Advance(TimeSpan.FromDays(31));
        var active = new List<CreatedPersonalAccessToken>();
        for (var i = 0; i < 10; i++)
        {
            active.Add(await service.CreateAsync(TestActors.Ann, $"long {i}", 365, TestContext.Current.CancellationToken));
        }

        // Act: revoke one active token (9 are active now) and create eleven more
        await service.RevokeAsync(TestActors.Ann, active[0].Token.Id, TestContext.Current.CancellationToken);
        for (var i = 0; i < 11; i++)
        {
            await service.CreateAsync(TestActors.Ann, $"after {i}", 90, TestContext.Current.CancellationToken);
        }

        // Assert: all eleven were accepted, so exactly the active ones were counted: 20 are active and one more is refused
        var list = await service.ListAsync(TestActors.Ann, TestContext.Current.CancellationToken);
        Assert.Equal(ReviewLimits.MaxActiveTokensPerUser, list.Count(t => t.IsActive(clock.GetUtcNow())));
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(TestActors.Ann, "one too many", 90, TestContext.Current.CancellationToken));
        Assert.Equal(DomainRule.TokenLimitReached, exception.Rule);
    }

    /// <summary>
    ///     Test that revoking an own token stops it working at once and records the revocation.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Revoke_OwnToken_StopsWorkingAndIsAudited()
    {
        // Arrange: a working token
        var (service, tokens, _, _, _) = await CreateAsync();
        var created = await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);
        Assert.True((await service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken)).Succeeded);

        // Act: revoke it
        await service.RevokeAsync(TestActors.Ann, created.Token.Id, TestContext.Current.CancellationToken);
        var after = await service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken);

        // Assert: refused as revoked, and the audit trail has the revocation by the owner
        Assert.False(after.Succeeded);
        Assert.Equal(TokenFailure.Revoked, after.Failure);
        var audit = tokens.Audits.Single(a => a.Action == "token.revoked");
        Assert.Equal("ann", audit.ActorUsername);
        Assert.Contains(created.Token.DisplayId, audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a user cannot revoke the token of somebody else, and that a missing or already revoked token is
    ///     reported the same way.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Revoke_OtherUsersToken_IsRefused()
    {
        // Arrange: ann has a token; bob exists too
        var (service, _, users, _, _) = await CreateAsync();
        await users.CreateAsync("bob", "Bob", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var created = await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);

        // Act: bob revokes ann's token, then ann revokes a token that does not exist, then ann revokes twice
        var byOther = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RevokeAsync(TestActors.Bob, created.Token.Id, TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RevokeAsync(TestActors.Ann, 999, TestContext.Current.CancellationToken));
        await service.RevokeAsync(TestActors.Ann, created.Token.Id, TestContext.Current.CancellationToken);
        var twice = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RevokeAsync(TestActors.Ann, created.Token.Id, TestContext.Current.CancellationToken));

        // Assert: all three are "not found"; ann's token was revoked only by ann
        Assert.Equal(DomainRule.TokenNotFound, byOther.Rule);
        Assert.Equal(DomainRule.TokenNotFound, missing.Rule);
        Assert.Equal(DomainRule.TokenNotFound, twice.Rule);
        Assert.NotNull(Assert.Single(await service.ListAsync(TestActors.Ann, TestContext.Current.CancellationToken)).Revoked);
    }

    /// <summary>
    ///     Test that a token made with an API token cannot create or revoke tokens.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_CreateAndRevoke_ApiTokenActor_IsRefused()
    {
        // Arrange: a token and an actor that arrived through it
        var (service, _, _, _, _) = await CreateAsync();
        var created = await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);
        var viaApi = new Actor("ann", "Ann") { ApiTokenId = created.Token.DisplayId };

        // Act: create and revoke as that actor
        var create = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(viaApi, "renewal", 90, TestContext.Current.CancellationToken));
        var revoke = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.RevokeAsync(viaApi, created.Token.Id, TestContext.Current.CancellationToken));

        // Assert: both are refused as not allowed
        Assert.Equal(DomainRule.ActorNotAllowed, create.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, revoke.Rule);
    }

    /// <summary>
    ///     Test that only the right value authenticates, and that the owner is the one who created the token.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Authenticate_RightAndWrongValue_OnlyRightSucceeds()
    {
        // Arrange: two tokens of two users
        var (service, _, users, _, _) = await CreateAsync();
        await users.CreateAsync("bob", "Bob", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var annToken = await service.CreateAsync(TestActors.Ann, "ann token", 90, TestContext.Current.CancellationToken);
        var bobToken = await service.CreateAsync(TestActors.Bob, "bob token", 90, TestContext.Current.CancellationToken);
        var wrong = "rcpat_" + new string('A', 43);

        // Act: authenticate with each right value and with a well-formed but unknown value
        var ann = await service.AuthenticateAsync(annToken.Value, TestContext.Current.CancellationToken);
        var bob = await service.AuthenticateAsync(bobToken.Value, TestContext.Current.CancellationToken);
        var unknown = await service.AuthenticateAsync(wrong, TestContext.Current.CancellationToken);

        // Assert: each right value gives its owner with the current role; the unknown value gives nothing
        Assert.True(ann.Succeeded);
        Assert.Equal("ann", ann.User!.Username);
        Assert.True(bob.Succeeded);
        Assert.Equal("bob", bob.User!.Username);
        Assert.Equal(UserRole.Admin, bob.User.Role);
        Assert.False(unknown.Succeeded);
        Assert.Equal(TokenFailure.Unknown, unknown.Failure);
        Assert.Null(unknown.User);
    }

    /// <summary>
    ///     Test that a row whose stored hash differs from the hash of the presented value is rejected even when the
    ///     store returns it, because the hash is verified again in constant time.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Authenticate_StoreReturnsRowWithOtherHash_IsRejected()
    {
        // Arrange: a token, and a store that returns that row for every hash
        var (service, tokens, _, _, _) = await CreateAsync();
        await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);
        var row = tokens.Rows[0];
        row.User = new User { Id = row.UserId, Username = "ann", DisplayName = "Ann", Enabled = true };
        tokens.FindOverride = _ => row;

        // Act: authenticate with a well-formed value that is not the token's
        var result = await service.AuthenticateAsync("rcpat_" + new string('B', 43), TestContext.Current.CancellationToken);

        // Assert: refused as unknown, with no owner
        Assert.False(result.Succeeded);
        Assert.Equal(TokenFailure.Unknown, result.Failure);
        Assert.Null(result.User);
    }

    /// <summary>
    ///     Test that a token of a disabled or locked-out owner is rejected, and works again when the owner does.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Authenticate_DisabledOrLockedOwner_IsRejected()
    {
        // Arrange: a token for ann and one for bob
        var (service, _, users, _, _) = await CreateAsync();
        await users.CreateAsync("bob", "Bob", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var annToken = await service.CreateAsync(TestActors.Ann, "ann token", 90, TestContext.Current.CancellationToken);
        var bobToken = await service.CreateAsync(TestActors.Bob, "bob token", 90, TestContext.Current.CancellationToken);

        // Act: disable ann; lock bob out with wrong passwords
        await users.SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await users.AuthenticateAsync("bob", "wrong password here", TestContext.Current.CancellationToken);
        }

        var disabled = await service.AuthenticateAsync(annToken.Value, TestContext.Current.CancellationToken);
        var locked = await service.AuthenticateAsync(bobToken.Value, TestContext.Current.CancellationToken);
        await users.SetEnabledAsync("ann", true, TestContext.Current.CancellationToken);
        var enabledAgain = await service.AuthenticateAsync(annToken.Value, TestContext.Current.CancellationToken);

        // Assert: refused for the reason, and accepted again once the owner is usable
        Assert.Equal(TokenFailure.OwnerDisabled, disabled.Failure);
        Assert.Equal(TokenFailure.OwnerLockedOut, locked.Failure);
        Assert.True(enabledAgain.Succeeded);
    }

    /// <summary>
    ///     Test that values of the wrong shape are refused without asking the store.
    /// </summary>
    /// <param name="presented">The presented value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rcpat_")]
    [InlineData("rcpat_short")]
    [InlineData("Bearer rcpat_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("other_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("rcpat_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    [InlineData("rcpat_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task PersonalAccessTokenService_Authenticate_Malformed_IsRejected(string? presented)
    {
        // Arrange: a service whose store fails the test when it is asked
        var (service, tokens, _, _, _) = await CreateAsync();
        tokens.FindOverride = _ => throw new InvalidOperationException("The store must not be asked.");

        // Act: authenticate
        var result = await service.AuthenticateAsync(presented, TestContext.Current.CancellationToken);

        // Assert: refused as malformed
        Assert.False(result.Succeeded);
        Assert.Equal(TokenFailure.Malformed, result.Failure);
    }

    /// <summary>
    ///     Test that creating and revoking record audit events that never contain the token value or its hash.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_CreateAndRevoke_RecordAuditEventsWithoutTheValue()
    {
        // Arrange: a service with a user
        var (service, tokens, _, _, _) = await CreateAsync();

        // Act: create and revoke a token
        var created = await service.CreateAsync(TestActors.Ann, "build server", 30, TestContext.Current.CancellationToken);
        await service.RevokeAsync(TestActors.Ann, created.Token.Id, TestContext.Current.CancellationToken);

        // Assert: two events by the user, naming the token and its display id, with neither value nor hash
        Assert.Equal(["token.created", "token.revoked"], tokens.Audits.Select(a => a.Action));
        Assert.All(tokens.Audits, a =>
        {
            Assert.Equal("ann", a.ActorUsername);
            Assert.Contains("build server", a.Detail, StringComparison.Ordinal);
            Assert.Contains(created.Token.DisplayId, a.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(created.Value, a.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(created.Token.TokenHash, a.Detail, StringComparison.Ordinal);
        });
        Assert.Contains("expires 2025-04-03T05:06:07Z", tokens.Audits[0].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the last-used time is written at most once per five minutes.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Authenticate_Touch_AtMostOncePerFiveMinutes()
    {
        // Arrange: a token
        var (service, tokens, _, clock, _) = await CreateAsync();
        var created = await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);

        // Act: use it twice at the start, once after four minutes, and once after five
        await service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken);
        await service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken);
        var afterFirst = tokens.TouchCount;
        clock.Advance(TimeSpan.FromMinutes(4));
        await service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken);
        var afterFour = tokens.TouchCount;
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken);

        // Assert: one write for the first two uses, none after four minutes, a second one after five
        Assert.Equal(1, afterFirst);
        Assert.Equal(1, afterFour);
        Assert.Equal(2, tokens.TouchCount);
        Assert.Equal(clock.GetUtcNow(), tokens.Rows[0].LastUsed);
    }

    /// <summary>
    ///     Test that a burst of requests that all read the same stale last-used time writes it only once.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_Authenticate_ParallelBurst_TouchesOnce()
    {
        // Arrange: a token never used, and a store whose lookup returns the same snapshot to every request
        var (service, tokens, _, _, _) = await CreateAsync();
        var created = await service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken);
        var snapshot = await tokens.FindByHashAsync(created.Token.TokenHash, TestContext.Current.CancellationToken);
        tokens.FindOverride = _ => snapshot;

        // Act: four requests that all see a token that was never used
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(
            _ => service.AuthenticateAsync(created.Value, TestContext.Current.CancellationToken)));

        // Assert: all are accepted, and the store was written once
        Assert.All(results, r => Assert.Equal(TokenFailure.None, r.Failure));
        Assert.Equal(1, tokens.TouchCount);
    }

    /// <summary>
    ///     Test that an unknown or disabled owner cannot create or list tokens.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_CreateAndList_UnknownOrDisabledOwner_IsRefused()
    {
        // Arrange: a service with ann, who is then disabled
        var (service, tokens, users, _, _) = await CreateAsync();
        await users.SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);

        // Act: create as the disabled user and as nobody known
        var disabled = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(TestActors.Ann, "build server", 90, TestContext.Current.CancellationToken));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ListAsync(TestActors.Eve, TestContext.Current.CancellationToken));

        // Assert: both are refused as not allowed and nothing is stored
        Assert.Equal(DomainRule.ActorNotAllowed, disabled.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, unknown.Rule);
        Assert.Empty(tokens.Rows);
    }

    /// <summary>
    ///     Creates a service over fresh fakes with the user <c>ann</c>.
    /// </summary>
    /// <returns>The service, the token store, the user service, the clock and the user store.</returns>
    private static async Task<(PersonalAccessTokenService Service, FakePersonalAccessTokenStore Tokens, UserService Users, MovableTimeProvider Clock, FakeUserStore UserStore)> CreateAsync()
    {
        var userStore = new FakeUserStore();
        var clock = new MovableTimeProvider(_now);
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var tokens = new FakePersonalAccessTokenStore(userStore);
        return (new PersonalAccessTokenService(tokens, userStore, clock), tokens, users, clock, userStore);
    }
}
