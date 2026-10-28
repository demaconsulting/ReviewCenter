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
///     What a user may do inside one project they belong to.
/// </summary>
public enum ProjectRole
{
    /// <summary>A member: sees the project, creates reviews, comments, answers checklists and signs.</summary>
    User,

    /// <summary>A project administrator: a member who also manages the repositories of the project.</summary>
    ProjectAdmin
}

/// <summary>
///     The membership of one user in one project, with the role the user has there.
/// </summary>
/// <remarks>
///     A user has at most one membership per project; the pair (user, project) is the identity. Everything a user may
///     see or do inside a project that is not given by being a system administrator comes from a membership, which is
///     why <see cref="ProjectAccess"/> is the only reader of the rules. The record is immutable and thread-safe.
/// </remarks>
/// <param name="UserId">The identity of the user.</param>
/// <param name="ProjectId">The identity of the project.</param>
/// <param name="Role">The role of the user in the project.</param>
public sealed record ProjectMembership(int UserId, int ProjectId, ProjectRole Role);

/// <summary>
///     A member of a project as it is listed to the people who manage the project.
/// </summary>
/// <param name="UserId">The identity of the user.</param>
/// <param name="Username">The user name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Enabled">Whether the user can sign in.</param>
/// <param name="Role">The role of the user in the project.</param>
public sealed record ProjectMember(int UserId, string Username, string DisplayName, bool Enabled, ProjectRole Role);
