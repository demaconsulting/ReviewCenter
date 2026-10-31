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
///     The vocabulary of action codes the product records in the audit trail, for the choices of the audit filter.
/// </summary>
/// <remarks>
///     An action code is <c>group.name</c>; a group (for example <c>review.</c>, written with its dot) is what a
///     person means by "all review actions". The list exists so that the audit pages offer the real codes instead
///     of a free text field, and it lives in one place so the pages cannot drift apart. It is not what the audit
///     service accepts or stores (any code can be recorded and any code can be filtered by); a test scans the
///     product sources for action codes and fails when a code is missing here or a code listed here is no longer
///     recorded, so the list cannot silently go stale. The class is stateless and thread-safe.
/// </remarks>
public static class AuditActions
{
    /// <summary>
    ///     Gets every known action code, ordered by group and name; each code has the form <c>group.name</c>.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "account.oidc-created",
        "attestation.generated",
        "audit.events-suppressed",
        "auth.locked-out",
        "auth.oidc-sign-in",
        "auth.oidc-sign-in-failed",
        "auth.sign-in",
        "auth.sign-in-failed",
        "auth.sign-in-locked",
        "auth.sign-in-throttled",
        "auth.sign-out",
        "auth.totp-disabled",
        "auth.totp-enrolled",
        "auth.totp-failed",
        "auth.totp-recovery-regenerated",
        "auth.totp-recovery-used",
        "auth.totp-reset",
        "checklist.answered",
        "checklist.applicability-changed",
        "project.account-created",
        "project.checklist-changed",
        "project.created",
        "project.evidence-settings-changed",
        "project.independent-reviewers-changed",
        "project.member-add-failed",
        "project.member-added",
        "project.member-removed",
        "project.member-role-changed",
        "project.review-rule-changed",
        "repository.created",
        "repository.disabled",
        "repository.enabled",
        "repository.updated",
        "review.created",
        "review.due-date-changed",
        "review.reopened",
        "signature.added",
        "signature.reauth-failed",
        "slot.added",
        "slot.removed",
        "slot.updated",
        "thread.reopened",
        "thread.replied",
        "thread.resolved",
        "thread.started",
        "token.created",
        "token.revoked",
        "token.use-failed",
        "token.use-throttled",
        "user.admin-changed",
        "user.created",
        "user.disabled",
        "user.enabled",
        "user.password-changed",
        "user.password-set-by-admin"
    ];

    /// <summary>
    ///     Gets the action groups, each as its prefix including the dot (for example <c>review.</c>), in order.
    /// </summary>
    public static IReadOnlyList<string> Prefixes { get; } = All.Select(PrefixOf).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    ///     Gets the prefix of an action code.
    /// </summary>
    /// <param name="action">The action code, for example <c>review.created</c>. Must not be null.</param>
    /// <returns>The group with its dot, for example <c>review.</c>; the whole text when it has no dot.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is null.</exception>
    public static string PrefixOf(string action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var dot = action.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? action : action[..(dot + 1)];
    }

    /// <summary>
    ///     Gets the known action codes of one group.
    /// </summary>
    /// <param name="prefix">The group prefix with its dot, for example <c>review.</c>. Must not be null.</param>
    /// <returns>The codes of the group in order; empty for an unknown prefix.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="prefix"/> is null.</exception>
    public static IReadOnlyList<string> InGroup(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return All.Where(action => PrefixOf(action) == prefix).ToList();
    }

    /// <summary>
    ///     Tells whether a code is one of the known action codes.
    /// </summary>
    /// <param name="action">The text to test; may be <c>null</c>.</param>
    /// <returns><c>true</c> when <paramref name="action"/> is exactly one of <see cref="All"/>.</returns>
    public static bool IsKnownAction(string? action)
    {
        return action is not null && All.Contains(action, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Tells whether a text is one of the known group prefixes.
    /// </summary>
    /// <param name="prefix">The text to test; may be <c>null</c>.</param>
    /// <returns><c>true</c> when <paramref name="prefix"/> is exactly one of <see cref="Prefixes"/>.</returns>
    public static bool IsKnownPrefix(string? prefix)
    {
        return prefix is not null && Prefixes.Contains(prefix, StringComparer.Ordinal);
    }
}
