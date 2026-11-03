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
///     The exception raised when a message could not be handed to the mail transport.
/// </summary>
/// <remarks>
///     The message is written for the operator and may be logged as it is: it names the kind of failure and never
///     holds the password of the mail server or the transcript of the conversation with it. The inner exception,
///     when there is one, is the transport's own and is for the log only. The exception is immutable and
///     thread-safe.
/// </remarks>
public sealed class NotificationSendException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationSendException"/> class.
    /// </summary>
    /// <param name="message">An operator-readable description that holds no secret.</param>
    public NotificationSendException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationSendException"/> class with the exception that
    ///     caused it.
    /// </summary>
    /// <param name="message">An operator-readable description that holds no secret.</param>
    /// <param name="innerException">The transport's own exception, for the log.</param>
    public NotificationSendException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
