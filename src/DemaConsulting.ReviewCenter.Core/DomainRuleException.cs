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
///     The domain rules that can be violated, carried by <see cref="DomainRuleException"/>.
/// </summary>
/// <remarks>
///     A machine-readable rule lets callers (and tests) tell violations apart without parsing
///     message text.
/// </remarks>
public enum DomainRule
{
    /// <summary>The project name is empty or too long.</summary>
    ProjectNameInvalid,

    /// <summary>The project description is too long.</summary>
    ProjectDescriptionTooLong,

    /// <summary>The referenced project does not exist.</summary>
    ProjectNotFound,

    /// <summary>The review id is empty, too long, or contains characters that are not allowed.</summary>
    ReviewIdInvalid,

    /// <summary>A review with the same id already exists in the project.</summary>
    ReviewIdDuplicate,

    /// <summary>The review title is empty or too long.</summary>
    ReviewTitleInvalid,

    /// <summary>The review has no files.</summary>
    NoFiles,

    /// <summary>Two files of the review have the same path.</summary>
    DuplicatePath,

    /// <summary>A file path is empty or too long.</summary>
    PathInvalid,

    /// <summary>A file is larger than <see cref="ReviewLimits.MaxFileSizeBytes"/>.</summary>
    FileTooLarge,

    /// <summary>The review has more than <see cref="ReviewLimits.MaxFileCount"/> files.</summary>
    TooManyFiles,

    /// <summary>The files together are larger than <see cref="ReviewLimits.MaxTotalSizeBytes"/>.</summary>
    TotalSizeExceeded,

    /// <summary>The comment text is empty or too long.</summary>
    CommentTextInvalid,

    /// <summary>
    ///     The thread anchor is not valid: the review or file does not exist, the line numbers are
    ///     inconsistent or beyond the file, or a line range was given for a binary or empty file.
    /// </summary>
    ThreadAnchorInvalid,

    /// <summary>The referenced thread does not exist in the review.</summary>
    ThreadNotFound,

    /// <summary>The review does not exist or has no checklist.</summary>
    ChecklistNotFound,

    /// <summary>The section or check is not part of the review's checklist.</summary>
    ChecklistItemNotFound,

    /// <summary>
    ///     A justification or reason is missing where it is required (a failed or not-applicable check, a section
    ///     that does not apply) or is longer than <see cref="ReviewLimits.MaxJustificationLength"/>.
    /// </summary>
    ChecklistJustificationInvalid,

    /// <summary>The user name is too short, too long, or contains characters that are not allowed.</summary>
    UserNameInvalid,

    /// <summary>A user with the same user name (compared without regard to case) already exists.</summary>
    UserNameTaken,

    /// <summary>The display name is empty or too long.</summary>
    DisplayNameInvalid,

    /// <summary>The password is shorter or longer than the policy allows.</summary>
    PasswordInvalid,

    /// <summary>The current password given when changing the own password is wrong.</summary>
    CurrentPasswordIncorrect,

    /// <summary>The referenced user does not exist.</summary>
    UserNotFound,

    /// <summary>The change would leave the system without an enabled system administrator.</summary>
    LastAdmin,

    /// <summary>The user already is a member of the project.</summary>
    MembershipExists,

    /// <summary>The user is not a member of the project.</summary>
    MembershipNotFound,

    /// <summary>The referenced review does not exist.</summary>
    ReviewNotFound,

    /// <summary>The referenced reviewer slot does not exist in the review.</summary>
    SlotNotFound,

    /// <summary>
    ///     The users chosen for a slot are not valid: a user does not exist, is disabled or is not a member of the
    ///     project, the primary and alternate are the same user, or a user is already in another slot of the review.
    /// </summary>
    SlotUserInvalid,

    /// <summary>The slot has a signature and can no longer be changed or removed.</summary>
    SlotSigned,

    /// <summary>The signed-in user is not allowed to make the change: the account is gone or disabled, the user is not in the slot, or nobody is signed in any more.</summary>
    ActorNotAllowed,

    /// <summary>The review cannot be signed now: the slot is already signed, or the checklist or threads block it.</summary>
    SigningNotAllowed,

    /// <summary>The password given to confirm a signature is wrong (or the account is locked, disabled or unknown).</summary>
    ReauthenticationFailed,

    /// <summary>The review record changed after the signer last saw it, so the signature was not stored.</summary>
    RecordChanged,

    /// <summary>The name of a repository is empty, too long or contains characters that are not allowed.</summary>
    RepositoryNameInvalid,

    /// <summary>Another repository already has the name, compared without regard to case.</summary>
    RepositoryNameTaken,

    /// <summary>The location of a repository is empty, too long, not a supported form or carries credentials of its own.</summary>
    RepositoryLocationInvalid,

    /// <summary>The user name or token of a repository is not valid, a token is given for a location that cannot use one, or a token is contained in the repository name, the user name or the location.</summary>
    RepositoryCredentialInvalid,

    /// <summary>The referenced repository does not exist.</summary>
    RepositoryNotFound,

    /// <summary>The repository is disabled and cannot be used as a content source.</summary>
    RepositoryDisabled,

    /// <summary>A revision name is empty, too long or not a plain branch, tag or commit name.</summary>
    RevisionInvalid,

    /// <summary>The revision does not exist in the repository.</summary>
    RevisionNotFound,

    /// <summary>The file selection patterns are empty, too many, too long or use a form that is not allowed.</summary>
    SelectionInvalid,

    /// <summary>The server cannot run git (it is not installed, not on the path or too old).</summary>
    GitUnavailable,

    /// <summary>Git could not read the repository: the location is wrong, access was denied or the transfer failed.</summary>
    GitAccessFailed,

    /// <summary>A git operation did not finish within the configured time and was stopped.</summary>
    GitTimeout,

    /// <summary>The stored token of a repository cannot be decrypted, for example because the key ring was lost.</summary>
    CredentialsUnreadable,

    /// <summary>No file matching the selection changed between the two revisions, so no review is created.</summary>
    NothingToReview,

    /// <summary>The revision has no <c>.reviewmark.yaml</c> in its root, so there are no review-sets to plan from.</summary>
    ReviewSetConfigMissing,

    /// <summary>The <c>.reviewmark.yaml</c> is not valid YAML, is too large, or does not define usable review-sets.</summary>
    ReviewSetConfigInvalid,

    /// <summary>The name of a personal access token is empty, too long or has control characters.</summary>
    TokenNameInvalid,

    /// <summary>The lifetime of a personal access token is not one of the allowed choices.</summary>
    TokenExpiryInvalid,

    /// <summary>The user already has <see cref="ReviewLimits.MaxActiveTokensPerUser"/> active personal access tokens.</summary>
    TokenLimitReached,

    /// <summary>The personal access token does not exist for the user, or it is already revoked.</summary>
    TokenNotFound,

    /// <summary>The change would leave the project without an enabled project administrator.</summary>
    LastProjectAdmin,

    /// <summary>
    ///     The member holds an unsigned or out-of-date reviewer slot on a review that is not approved, so the member
    ///     cannot be made a read-only auditor until the slot is signed, reassigned or the review is approved.
    /// </summary>
    MemberHasOpenReviewWork,

    /// <summary>Too many failed attempts to add or create a member in a short time; try again later.</summary>
    TooManyAttempts,

    /// <summary>
    ///     The account has a temporary password; the password must be changed before anything else is allowed.
    /// </summary>
    PasswordChangeRequired,

    /// <summary>The second factor can not be used: it is switched off, the account is external or has a temporary password, or the request came with an API token.</summary>
    TotpNotAvailable,

    /// <summary>The user already has a confirmed second factor.</summary>
    TotpAlreadyEnrolled,

    /// <summary>The user has no (confirmed or pending) second factor to confirm, use or remove.</summary>
    TotpNotEnrolled,

    /// <summary>The code is wrong, was already used, or has the wrong form.</summary>
    TotpCodeIncorrect,

    /// <summary>The pending enrollment was started too long ago; start it again.</summary>
    TotpEnrollmentExpired,

    /// <summary>The stored secret of a user cannot be decrypted, for example because the key ring was lost; an administrator must reset the second factor.</summary>
    TotpSecretUnreadable,

    /// <summary>
    ///     The review is approved (every reviewer slot has a signature of the current record) and therefore locked:
    ///     its threads, checklist answers and reviewer slots can not change until an administrator reopens it.
    /// </summary>
    ReviewLocked,

    /// <summary>The review is not approved, so it is not locked and there is nothing to reopen.</summary>
    ReviewNotLocked,

    /// <summary>The reason given for reopening a review is missing or longer than <see cref="ReviewLimits.MaxReopenReasonLength"/>.</summary>
    ReopenReasonInvalid,

    /// <summary>
    ///     The user created the review and the project requires independent reviewers, so the user can neither be
    ///     chosen as a reviewer of it nor sign it.
    /// </summary>
    ReviewerNotIndependent,

    /// <summary>
    ///     A checklist definition is not valid: it is not well-formed JSON, breaks a limit, or has missing, malformed
    ///     or duplicate identifiers, titles or wording, or a changed definition keeps the version label of the
    ///     current one. The message lists the errors with their paths.
    /// </summary>
    ChecklistDefinitionInvalid,

    /// <summary>
    ///     A review due date is not valid: it is not an ISO calendar date, a new review's date is before today (UTC) or
    ///     more than two years ahead.
    /// </summary>
    DueDateInvalid,

    /// <summary>
    ///     A follow-up review cannot be created from the chosen earlier review: the earlier review does not exist in the
    ///     project, it does not fit the new review (another review-set, another repository, an upload against a
    ///     repository, or no file in common), or open threads of the earlier review would be dropped because their file
    ///     is not in the new review and the creator did not acknowledge that.
    /// </summary>
    FollowUpInvalid,

    /// <summary>
    ///     An audit filter is not valid: its first day is after its last day, or a text is longer than the field it is
    ///     matched against.
    /// </summary>
    AuditFilterInvalid,

    /// <summary>
    ///     The change would make one or more current signatures out of date and the person making it did not
    ///     acknowledge them; <see cref="DomainRuleException.AffectedSlots"/> lists the signatures not acknowledged.
    /// </summary>
    SignaturesWouldBecomeOutOfDate,

    /// <summary>
    ///     The user created the review and the project does not allow the author of a review to answer its checklist,
    ///     so the user can not change the checklist answers or section applicability of it.
    /// </summary>
    AuthorCannotAnswer,

    /// <summary>
    ///     The user reopened the review and the project does not allow the person who reopened a review to sign it
    ///     again, so the user can not sign it.
    /// </summary>
    ReopenerCannotSign,

    /// <summary>
    ///     The reopen would leave a reviewer slot in which nobody may sign again (the primary and the alternate, if
    ///     any, are the person reopening or people who reopened it before), so the review could never be approved again.
    /// </summary>
    ReopenWouldStrandSlot
}

/// <summary>
///     The exception raised when a request violates a domain rule.
/// </summary>
/// <remarks>
///     Rule violations are expected user errors, not faults: the message is written for the
///     operator and may be shown as it is, while <see cref="Rule"/> identifies the violation for
///     code. The exception is immutable and thread-safe.
/// </remarks>
public sealed class DomainRuleException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DomainRuleException"/> class.
    /// </summary>
    /// <param name="rule">The rule that was violated.</param>
    /// <param name="message">An operator-readable explanation of the violation.</param>
    /// <param name="affectedSlots">
    ///     The signatures the change would make out of date, for <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/>;
    ///     <c>null</c> (the default) for every other rule. The list is copied, so a later change to the caller's list does
    ///     not change the exception.
    /// </param>
    public DomainRuleException(
        DomainRule rule,
        string message,
        IReadOnlyList<AffectedSignature>? affectedSlots = null)
        : base(message)
    {
        Rule = rule;
        AffectedSlots = affectedSlots is null ? [] : [.. affectedSlots];
    }

    /// <summary>
    ///     Gets the rule that was violated.
    /// </summary>
    public DomainRule Rule { get; }

    /// <summary>
    ///     Gets the signatures the change would make out of date; empty unless <see cref="Rule"/> is
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/>.
    /// </summary>
    public IReadOnlyList<AffectedSignature> AffectedSlots { get; }
}
