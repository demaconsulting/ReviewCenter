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

namespace DemaConsulting.ReviewCenter.Tests.SelfTest;

/// <summary>
///     Points environment variables of the process at values for a test and restores them afterwards.
/// </summary>
/// <remarks>
///     The configuration check reads the environment the way the server does, so a test configures it through the
///     variables (<c>Database__ConnectionString</c>, <c>DataProtection__KeysPath</c>, and so on). The class belongs to
///     the tests of the "Sequential" collection, which never run in parallel with each other.
/// </remarks>
internal sealed class ConfiguredKeysEnvironment : IDisposable
{
    /// <summary>
    ///     The values of the variables before the test; <c>null</c> for a variable that was not set.
    /// </summary>
    private readonly List<KeyValuePair<string, string?>> _previous = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConfiguredKeysEnvironment"/> class.
    /// </summary>
    /// <param name="variables">The variables to set; a <c>null</c> value removes the variable.</param>
    public ConfiguredKeysEnvironment(params KeyValuePair<string, string?>[] variables)
    {
        foreach (var (name, value) in variables)
        {
            _previous.Add(new(name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Backwards, so a variable that was set twice returns to its original value
        for (var i = _previous.Count - 1; i >= 0; i--)
        {
            Environment.SetEnvironmentVariable(_previous[i].Key, _previous[i].Value);
        }
    }
}
