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

using System.Reflection;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     A pass-through proxy of the review store that counts how often <see cref="IReviewStore.ListReviewsAsync"/> is
///     called, so a test through the real pipeline can show that a request reads the review list once.
/// </summary>
/// <remarks>
///     A dispatch proxy keeps the wrapper independent of the (large) store interface: every call is forwarded
///     unchanged to the real store and only the one method is counted.
/// </remarks>
public class CountingReviewStore : DispatchProxy
{
    /// <summary>
    ///     The real store the calls are forwarded to.
    /// </summary>
    private IReviewStore? _inner;

    /// <summary>
    ///     The number of list calls so far.
    /// </summary>
    private int _listCalls;

    /// <summary>
    ///     Gets the number of times the reviews of a project were listed.
    /// </summary>
    public int ListCalls => Volatile.Read(ref _listCalls);

    /// <summary>
    ///     Wraps a store in a counting proxy.
    /// </summary>
    /// <param name="inner">The real store.</param>
    /// <returns>The store to register; it is a <see cref="CountingReviewStore"/> that implements the interface.</returns>
    public static IReviewStore Wrap(IReviewStore inner)
    {
        var proxy = Create<IReviewStore, CountingReviewStore>();
        ((CountingReviewStore)(object)proxy)._inner = inner;
        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == nameof(IReviewStore.ListReviewsAsync))
        {
            Interlocked.Increment(ref _listCalls);
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }
}
