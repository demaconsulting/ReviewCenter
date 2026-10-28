namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Test shorthand for the system-administrator flag of a user: there is no global role any more, but many tests
///     only need "an administrator" or "an ordinary user", and this keeps their set-up readable.
/// </summary>
public enum UserRole
{
    /// <summary>
    ///     An ordinary user (not a system administrator).
    /// </summary>
    Reviewer,

    /// <summary>
    ///     A system administrator.
    /// </summary>
    Admin
}
