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

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     TEMPORARY: runs a page action and, when it is refused because it would make signatures out of date, runs it
///     once more acknowledging the signatures named by the refusal.
/// </summary>
/// <remarks>
///     This exists only until the pages ask the person to confirm (workflow slice S2); that slice deletes this class
///     and passes the confirmed slots to the services instead. Until then the pages cannot ask, so they acknowledge
///     what the person could not yet be asked about. This is exactly the behavior before the services refused such a
///     change: the edit goes through and the signatures become out of date. The class is stateless and thread-safe.
/// </remarks>
internal static class SignatureAcknowledgement
{
    /// <summary>
    ///     Runs the action; if it is refused with <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/>, runs it
    ///     once more with the refused slots acknowledged.
    /// </summary>
    /// <param name="action">
    ///     The action to run, given the slot identities to acknowledge (<c>null</c> on the first attempt). Must not be
    ///     null.
    /// </param>
    /// <returns>A task that completes when the action has completed.</returns>
    /// <exception cref="DomainRuleException">Thrown with any other rule the action violates, or by the second attempt.</exception>
    public static async Task RunAsync(Func<IReadOnlyCollection<int>?, Task> action)
    {
        try
        {
            await action(null);
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.SignaturesWouldBecomeOutOfDate)
        {
            await action(ex.AffectedSlots.Select(slot => slot.SlotId).ToList());
        }
    }
}
