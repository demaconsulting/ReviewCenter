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

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Stands in for the pause of a temporary directory between two delete attempts: it records every pause that is
///     asked for, runs an optional action on the first one, and never waits, so a test observes the retry policy
///     instead of measuring how long a disposal takes.
/// </summary>
internal sealed class PauseRecorder
{
    /// <summary>
    ///     More pauses than any policy under test asks for; reaching it means the retry does not stop.
    /// </summary>
    private const int Limit = 1000;

    /// <summary>
    ///     The action to run on the first pause, or <c>null</c>.
    /// </summary>
    private Action? _onFirstPause;

    /// <summary>
    ///     Whether a pause really waits for the time asked for.
    /// </summary>
    private readonly bool _wait;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PauseRecorder"/> class.
    /// </summary>
    /// <param name="onFirstPause">An action to run on the first pause (for example to release a file), or <c>null</c>.</param>
    /// <param name="wait">
    ///     <c>true</c> to really wait for the time asked for, so that a scanner that looks at a released file gets
    ///     its moment as in production; nothing is measured either way.
    /// </param>
    public PauseRecorder(Action? onFirstPause = null, bool wait = false)
    {
        _onFirstPause = onFirstPause;
        _wait = wait;
    }

    /// <summary>
    ///     Gets the pauses asked for, in order.
    /// </summary>
    public List<TimeSpan> Pauses { get; } = [];

    /// <summary>
    ///     Records one pause, and runs the first-pause action on the first one.
    /// </summary>
    /// <param name="delay">The pause asked for.</param>
    /// <exception cref="InvalidOperationException">Thrown when far more pauses are asked for than any policy allows.</exception>
    public void Pause(TimeSpan delay)
    {
        Pauses.Add(delay);
        if (Pauses.Count > Limit)
        {
            throw new InvalidOperationException("The delete is retried without end.");
        }

        var action = _onFirstPause;
        _onFirstPause = null;
        action?.Invoke();
        if (_wait)
        {
            Thread.Sleep(delay);
        }
    }
}
