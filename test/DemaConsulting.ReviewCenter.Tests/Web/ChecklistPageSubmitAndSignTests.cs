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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.Extensions.DependencyInjection;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the "Submit and sign" flow of the <see cref="ChecklistPage"/>: the primary button of a complete
///     ballot that hands the ballot in and signs it, the failure that leaves the ballot handed in, and the identity
///     provider variant. Rendered in memory with bUnit.
/// </summary>
public class ChecklistPageSubmitAndSignTests : BallotPageTestBase
{
    /// <summary>
    ///     The password of the users created by the page tests.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Gets the status of the signature slot of a user.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name of the primary reviewer of the slot.</param>
    /// <returns>The signature state of the slot.</returns>
    private async Task<SlotSignatureState> SignatureOfAsync(int projectId, string username)
    {
        var approval = await Signing.GetApprovalAsync(projectId, ReviewKey, TestContext.Current.CancellationToken);
        return approval!.Slots.Single(slot => slot.Primary.Username == username).State;
    }

    /// <summary>
    ///     Test that a complete draft of one of several reviewers offers "Submit and sign" as the main action and keeps
    ///     "Submit ballot" as the quieter secondary one.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_CompleteDraft_OffersSubmitAndSignAsPrimaryAction()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.NotNull(cut.Find("[data-testid='submit-and-sign']"));
        Assert.NotNull(cut.Find("[data-testid='submit-sign-password']"));
        Assert.Empty(cut.FindAll("[data-testid='submit-sign-totp']"));
        Assert.Contains("secondary", cut.Find("[data-testid='submit-ballot']").ClassName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an incomplete draft offers neither of the two buttons.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_IncompleteDraft_OffersNoSubmitButtons()
    {
        // Arrange: bob answered nothing
        var projectId = await CreateReviewAsync("ann", "bob");
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='submit-and-sign']"));
        Assert.Empty(cut.FindAll("[data-testid='submit-ballot']"));
    }

    /// <summary>
    ///     Test that "Submit and sign" hands the ballot in and signs it in one go with the typed password, and that
    ///     the password is gone from the page afterwards.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmitAndSign_SubmitsThenSigns()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='submit-sign-password']").InputAsync(Password);

        // Act
        await cut.Find("[data-testid='submit-sign-form']").SubmitAsync();

        // Assert
        Assert.Equal(SlotSignatureState.Signed, await SignatureOfAsync(projectId, "bob"));
        Assert.Contains("you have signed", cut.Find("[data-testid='sign-notice']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='submit-sign-form']"));
        Assert.DoesNotContain(Password, cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a wrong password still leaves the ballot handed in, says that signing did not work and points to
    ///     the review page.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmitAndSign_WrongPassword_KeepsBallotAndExplains()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='submit-sign-password']").InputAsync("not the password at all");

        // Act
        await cut.Find("[data-testid='submit-sign-form']").SubmitAsync();

        // Assert
        var error = cut.Find("[data-testid='sign-error']").TextContent;
        Assert.Contains("Your ballot is in, but signing did not work", error, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid='sign-error-review-link']"));
        Assert.Equal(SlotSignatureState.NotSigned, await SignatureOfAsync(projectId, "bob"));
        Assert.Contains("is submitted", cut.Find("[data-testid='ballot-status']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a viewer who has not seen the other ballots does not sign a record that another reviewer changed
    ///     while the ballot was handed in: nothing is signed, the page says that something changed, and the answers
    ///     of the other reviewer are shown.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmitAndSign_OtherReviewerChangesAnswerAfterHandIn_SignsNothingAndExplains()
    {
        // Arrange: ann's ballot is a draft that agrees with bob's; she hands in a differing answer right after bob does
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann", submit: false);
        await AnswerAsync(projectId, "bob", submit: false);
        Store.AfterSubmitBallot = async () =>
        {
            await AnswerAsync(projectId, "ann", ChecklistOutcome.Fail);
        };
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='submit-sign-password']").InputAsync(Password);

        // Act
        await cut.Find("[data-testid='submit-sign-form']").SubmitAsync();

        // Assert: the ballot is in, no signature is stored, and the message and the answers are shown
        Assert.Equal(SlotSignatureState.NotSigned, await SignatureOfAsync(projectId, "bob"));
        Assert.Empty(cut.FindAll("[data-testid='sign-notice']"));
        var error = cut.Find("[data-testid='sign-error']").TextContent;
        Assert.Contains("something changed", error, StringComparison.Ordinal);
        Assert.Contains("not signed", error, StringComparison.Ordinal);
        Assert.Contains("is submitted", cut.Find("[data-testid='ballot-status']").TextContent, StringComparison.Ordinal);
        if (cut.FindAll($"[data-testid='check-{FirstItemTestId}']").Count == 0)
        {
            var sectionId = DefaultChecklist.Current.Sections[0].Id.ToLowerInvariant();
            await cut.Find($"[data-testid='section-{sectionId}-toggle']").ClickAsync();
        }

        Assert.NotNull(cut.Find($"[data-testid='ballot-others-{FirstItemTestId}']"));
    }

    /// <summary>
    ///     Test that "Submit ballot" alone still hands the ballot in without signing.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmitBallotAlone_DoesNotSign()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);

        // Act
        await cut.Find("[data-testid='submit-ballot']").ClickAsync();

        // Assert
        Assert.Equal(SlotSignatureState.NotSigned, await SignatureOfAsync(projectId, "bob"));
        Assert.Empty(cut.FindAll("[data-testid='submit-ballot']"));
        Assert.Empty(cut.FindAll("[data-testid='ballot-error']"));
    }

    /// <summary>
    ///     Test that a single-reviewer review keeps today's experience: no "Submit and sign" and no hidden notice.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SingleSlot_OffersNoSubmitAndSign()
    {
        // Arrange
        var projectId = await CreateReviewAsync("bob");
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='submit-and-sign']"));
        Assert.Empty(cut.FindAll("[data-testid='ballots-hidden-notice']"));
    }

    /// <summary>
    ///     Test that a person who signs in with an identity provider gets no password field: the ballot is handed in
    ///     and the page asks them to confirm their identity with the provider before signing.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmitAndSign_ProviderUser_SubmitsThenAsksToConfirmIdentity()
    {
        // Arrange
        Services.Configure<OidcOptions>(options =>
        {
            options.Enabled = true;
            options.DisplayName = "Provider";
        });
        var external = (await Users.SignInExternalAsync(
            "https://idp.example.com/", "sub-1", "zoe", "Zoe", TestContext.Current.CancellationToken)).User!;
        var projectId = await CreateReviewAsync("ann", external.Username);
        await AnswerAsync(projectId, external.Username, submit: false);
        SignInAs(external.Username, "Zoe", false);
        var cut = RenderPage(projectId);
        Assert.Empty(cut.FindAll("[data-testid='submit-sign-password']"));
        Assert.NotNull(cut.Find("[data-testid='submit-sign-oidc-hint']"));

        // Act
        await cut.Find("[data-testid='submit-sign-form']").SubmitAsync();

        // Assert: the ballot is in, nothing is signed yet, and the re-authentication form is ready
        Assert.Equal(SlotSignatureState.NotSigned, await SignatureOfAsync(projectId, external.Username));
        var form = cut.Find("[data-testid='submit-sign-reauth-form']");
        Assert.Equal("/auth/oidc/reauth", form.GetAttribute("action"));
        Assert.Contains("Provider", cut.Find("[data-testid='submit-sign-reauth']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Your ballot is in", cut.Find("[data-testid='submit-sign-reauth-hint']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a person who signs in with an identity provider is not sent to confirm their identity when the
    ///     answers revealed by the hand-in differ: the ballot is in, nothing is signed, the page says that something
    ///     changed, and no re-authentication form is offered.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmitAndSign_ProviderUser_DifferingBallotRevealed_ExplainsAndSkipsConfirmation()
    {
        // Arrange: the other reviewer hands in a differing answer right after the provider user's hand-in
        Services.Configure<OidcOptions>(options =>
        {
            options.Enabled = true;
            options.DisplayName = "Provider";
        });
        var external = (await Users.SignInExternalAsync(
            "https://idp.example.com/", "sub-2", "yan", "Yan", TestContext.Current.CancellationToken)).User!;
        var projectId = await CreateReviewAsync("ann", external.Username);
        await AnswerAsync(projectId, "ann", submit: false);
        await AnswerAsync(projectId, external.Username, submit: false);
        Store.AfterSubmitBallot = async () =>
        {
            await AnswerAsync(projectId, "ann", ChecklistOutcome.Fail);
        };
        SignInAs(external.Username, "Yan", false);
        var cut = RenderPage(projectId);

        // Act
        await cut.Find("[data-testid='submit-sign-form']").SubmitAsync();

        // Assert
        Assert.Equal(SlotSignatureState.NotSigned, await SignatureOfAsync(projectId, external.Username));
        Assert.Empty(cut.FindAll("[data-testid='submit-sign-reauth-form']"));
        var error = cut.Find("[data-testid='sign-error']").TextContent;
        Assert.Contains("something changed", error, StringComparison.Ordinal);
        Assert.Contains("not signed", error, StringComparison.Ordinal);
    }
}
