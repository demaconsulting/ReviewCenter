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
///     The settings of the optional time-based one-time password second factor.
/// </summary>
/// <remarks>
///     Read from the <c>Identity:Totp</c> configuration section by the web server and validated at start-up. When
///     <see cref="Enabled"/> is off nothing offers enrollment and nothing asks for a code at sign-in or signing;
///     existing enrollments stay in the database and apply again when the switch is turned back on. The record is
///     immutable and thread-safe.
/// </remarks>
/// <param name="Enabled"><c>true</c> when the second factor is available and enforced for enrolled accounts.</param>
/// <param name="Issuer">The service name shown in the authenticator app; not secret.</param>
public sealed record TotpSettings(bool Enabled, string Issuer)
{
    /// <summary>
    ///     The issuer used when none is configured.
    /// </summary>
    public const string DefaultIssuer = "ReviewCenter";
}

/// <summary>
///     The secret of a started enrollment, shown to the user once so it can be entered into an authenticator app.
/// </summary>
/// <param name="SecretBase32">The secret as Base32 text.</param>
/// <param name="OtpauthUri">The <c>otpauth://</c> address that contains the same secret.</param>
public sealed record TotpEnrollment(string SecretBase32, string OtpauthUri);

/// <summary>
///     What a page needs to know to show the second factor of the signed-in user.
/// </summary>
/// <param name="FeatureEnabled"><c>true</c> when the second factor is switched on for the server.</param>
/// <param name="Available"><c>true</c> when this user can use it (a local account without a temporary password, not an API token).</param>
/// <param name="Enrolled"><c>true</c> when the user has a confirmed enrollment.</param>
/// <param name="RecoveryCodesLeft">The number of unused recovery codes; 0 when not enrolled.</param>
public sealed record TotpStatus(bool FeatureEnabled, bool Available, bool Enrolled, int RecoveryCodesLeft);

/// <summary>
///     The outcome of the second step of a sign-in.
/// </summary>
public enum TotpSignInOutcome
{
    /// <summary>The code was right; the user is signed in.</summary>
    Success,

    /// <summary>The code was wrong, already used or malformed; the challenge stays for another try.</summary>
    Failed,

    /// <summary>The challenge is unknown, expired, used up, for another client or the account can no longer use it.</summary>
    ChallengeInvalid,

    /// <summary>The account is locked out; nothing was checked or counted.</summary>
    LockedOut
}

/// <summary>
///     The answer to the second step of a sign-in.
/// </summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="User">The user to sign in; <c>null</c> unless the outcome is <see cref="TotpSignInOutcome.Success"/>.</param>
/// <param name="UsedRecoveryCode"><c>true</c> when a recovery code was spent instead of an authenticator code.</param>
/// <param name="RecoveryCodesLeft">The unused recovery codes after the sign-in; only set when a recovery code was used.</param>
public sealed record TotpSignInResult(
    TotpSignInOutcome Outcome,
    User? User,
    bool UsedRecoveryCode,
    int RecoveryCodesLeft);

/// <summary>
///     The answer to the password step of a sign-in or a signature confirmation.
/// </summary>
/// <param name="User">The user whose password was right (enabled, not locked out); <c>null</c> for every failure.</param>
/// <param name="SecondFactorRequired">
///     <c>true</c> when the user has a confirmed enrollment that applies: the password alone is not enough.
/// </param>
public sealed record PasswordStep(User? User, bool SecondFactorRequired);
