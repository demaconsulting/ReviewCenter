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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks that the audit trail is a hash chain that notices a change made behind the application's back.
/// </summary>
/// <remarks>
///     The check appends entries through the production <see cref="AuditService"/> on an embedded SQLite database
///     in a temporary folder, verifies that the chain is intact, then changes one stored entry with a direct SQL
///     statement, as an attacker with database access would, and verifies that the production verifier reports the
///     changed entry. The class is stateless and thread-safe.
/// </remarks>
internal static class AuditChainCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_AuditChain";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A short description of the verification before and after the change.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the intact chain is rejected or the change is not noticed.</exception>
    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        using var folder = new TemporaryDirectory();
        await using var provider = SelfTestServices.Create(folder);
        var audit = provider.GetRequiredService<AuditService>();

        // Append three entries and expect an intact chain
        var actor = Actor.CommandLine;
        await audit.RecordAsync(AuditDraft.By(actor, "selftest.one", "first entry"), cancellationToken);
        var second = await audit.RecordAsync(AuditDraft.By(actor, "selftest.two", "second entry"), cancellationToken);
        await audit.RecordAsync(AuditDraft.By(actor, "selftest.three", "third entry"), cancellationToken);
        var intact = await audit.VerifyAsync(cancellationToken);
        ValidationFailedException.ThrowUnless(
            intact is { IsIntact: true, Count: 3 }, "The freshly written audit chain was not reported as intact.");

        // Change the second entry directly in the database, bypassing the service
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AuditEntries SET Detail = 'forged' WHERE Sequence = {second.Sequence}", cancellationToken);
        }

        var broken = await audit.VerifyAsync(cancellationToken);
        ValidationFailedException.ThrowUnless(
            broken is { IsIntact: false } && broken.FirstBadSequence == second.Sequence,
            "The verifier did not report the entry that was changed.");

        return $"3 entries intact; change of entry {second.Sequence} detected";
    }
}
