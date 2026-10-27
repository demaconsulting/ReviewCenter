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

using DemaConsulting.ReviewCenter.Evidence;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     A key provider whose key, or whose problem, a test chooses; it creates no files and uses no store.
/// </summary>
/// <remarks>
///     The provider owns the key it is given and disposes it with <see cref="Dispose"/>. Without a key and without
///     a problem it reports a generic problem.
/// </remarks>
internal sealed class FakeAttestationKeyProvider : IAttestationKeyProvider, IDisposable
{
    /// <summary>
    ///     The key that is supplied, or <c>null</c>.
    /// </summary>
    private readonly AttestationSigningKey? _key;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeAttestationKeyProvider"/> class that supplies a key.
    /// </summary>
    /// <param name="key">The key; the provider disposes it.</param>
    public FakeAttestationKeyProvider(AttestationSigningKey key)
    {
        _key = key;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeAttestationKeyProvider"/> class that has no usable key.
    /// </summary>
    /// <param name="problem">The problem to report.</param>
    public FakeAttestationKeyProvider(string problem)
    {
        Problem = problem;
    }

    /// <summary>
    ///     Gets the problem that is reported, or <c>null</c> when there is a key.
    /// </summary>
    public string? Problem { get; }

    /// <summary>
    ///     Gets the number of times a test asked for the key.
    /// </summary>
    public int Requests { get; private set; }

    /// <inheritdoc />
    public string Name => "Fake";

    /// <inheritdoc />
    public AttestationSigningKey GetSigningKey()
    {
        Requests++;
        return _key ?? throw new AttestationKeyException(Problem ?? "No key.");
    }

    /// <inheritdoc />
    public AttestationKeyStatus Describe()
    {
        return _key is null
            ? new AttestationKeyStatus(Name, false, null, Problem)
            : new AttestationKeyStatus(Name, true, _key.Info, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _key?.Dispose();
    }
}
