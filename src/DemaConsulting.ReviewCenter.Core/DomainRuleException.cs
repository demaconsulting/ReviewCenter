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

    /// <summary>The change would leave the system without an enabled administrator.</summary>
    LastAdmin,

    /// <summary>The referenced review does not exist.</summary>
    ReviewNotFound,

    /// <summary>The referenced reviewer slot does not exist in the review.</summary>
    SlotNotFound,

    /// <summary>
    ///     The users chosen for a slot are not valid: a user does not exist or is disabled, the primary and
    ///     alternate are the same user, or a user is already in another slot of the review.
    /// </summary>
    SlotUserInvalid,

    /// <summary>The slot has a signature and can no longer be changed or removed.</summary>
    SlotSigned,

    /// <summary>The signed-in user is not allowed to make the change: the account is gone or disabled, or the user is not in the slot.</summary>
    ActorNotAllowed,

    /// <summary>The review cannot be signed now: the slot is already signed, or the checklist or threads block it.</summary>
    SigningNotAllowed,

    /// <summary>The password given to confirm a signature is wrong (or the account is locked, disabled or unknown).</summary>
    ReauthenticationFailed,

    /// <summary>The review record changed after the signer last saw it, so the signature was not stored.</summary>
    RecordChanged
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
    public DomainRuleException(DomainRule rule, string message)
        : base(message)
    {
        Rule = rule;
    }

    /// <summary>
    ///     Gets the rule that was violated.
    /// </summary>
    public DomainRule Rule { get; }
}
