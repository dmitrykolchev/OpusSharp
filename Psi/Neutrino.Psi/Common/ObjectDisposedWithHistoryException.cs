// <copyright file="ObjectDisposedWithHistoryException.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;


namespace Microsoft.Psi;

/// <summary>
/// The exception that is thrown, including history, when an operation is performed on a disposed object.
/// </summary>
public class ObjectDisposedWithHistoryException : ObjectDisposedException
{
    private readonly List<Tuple<string, StackTrace>> _history = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ObjectDisposedWithHistoryException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public ObjectDisposedWithHistoryException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Gets the exception history. List of tuples of descriptions and <see cref="StackTrace"/> objects.
    /// </summary>
    public List<Tuple<string, StackTrace>> History => _history;

    /// <summary>
    /// Adds history to the exception with the given description and the caller's stack frame.
    /// </summary>
    /// <param name="description">The description for the history.</param>
    public void AddHistory(string description)
    {
        AddHistory(description, new StackTrace(true));
    }

    /// <summary>
    /// Adds history to the exception with the given description and stack frame.
    /// </summary>
    /// <param name="description">The description for the history.</param>
    /// <param name="trace">The stack frame for the history.</param>
    public void AddHistory(string description, StackTrace trace)
    {
        _history.Add(Tuple.Create(description, trace));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        StringBuilder sb = new(base.ToString());

        foreach (Tuple<string, StackTrace> historyEntry in History)
        {
            sb.AppendLine();
            sb.AppendLine(historyEntry.Item1);
            foreach (StackFrame frame in historyEntry.Item2.GetFrames())
            {
                sb.AppendLine($"{frame.GetFileName()}({frame.GetFileLineNumber()}): {frame.GetMethod().DeclaringType}.{frame.GetMethod().Name}");
            }
        }

        return sb.ToString();
    }
}
