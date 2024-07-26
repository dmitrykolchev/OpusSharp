// <copyright file="Pickers.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using Neutrino.Psi.Common;
using Neutrino.Psi.Components;

namespace Neutrino.Psi;

/// <summary>
/// Extension methods that simplify operator usage.
/// </summary>
public static partial class Operators
{
    /// <summary>
    /// Filter messages to those where a given condition is met.
    /// </summary>
    /// <typeparam name="T">Type of source/output messages.</typeparam>
    /// <param name="source">Source stream.</param>
    /// <param name="condition">Predicate function by which to filter messages.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    /// <param name="name">An optional name for this stream operator.</param>
    /// <returns>Output stream.</returns>
    public static IProducer<T> Where<T>(this IProducer<T> source, Func<T, Envelope, bool> condition, DeliveryPolicy<T> deliveryPolicy = null, string name = nameof(Where))
    {
        return Process<T, T>(
                    source,
                    (d, e, s) =>
                    {
                        if (condition(d, e))
                        {
                            s.Post(d, e.OriginatingTime);
                        }
                    },
                    deliveryPolicy,
                    name);
    }

    /// <summary>
    /// Filter messages to those where a given condition is met.
    /// </summary>
    /// <typeparam name="T">Type of source/output messages.</typeparam>
    /// <param name="source">Source stream.</param>
    /// <param name="condition">Predicate function by which to filter messages.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    /// <param name="name">An optional name for this stream operator.</param>
    /// <returns>Output stream.</returns>
    public static IProducer<T> Where<T>(this IProducer<T> source, Predicate<T> condition, DeliveryPolicy<T> deliveryPolicy = null, string name = nameof(Where))
    {
        return Where(source, (d, e) => condition(d), deliveryPolicy, name);
    }

    /// <summary>
    /// Filter stream to the first n messages.
    /// </summary>
    /// <typeparam name="T">Type of source/output messages.</typeparam>
    /// <param name="source">Source stream.</param>
    /// <param name="number">Number of messages.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    /// <param name="name">An optional name for this stream operator.</param>
    /// <returns>Output stream.</returns>
    public static IProducer<T> First<T>(this IProducer<T> source, int number, DeliveryPolicy<T> deliveryPolicy = null, string name = nameof(First))
    {
        return source.Where(v => number-- > 0, deliveryPolicy, name);
    }

    /// <summary>
    /// Filter stream to the first message (single-message stream).
    /// </summary>
    /// <typeparam name="T">Type of source/output messages.</typeparam>
    /// <param name="source">Source stream.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    /// <param name="name">An optional name for this stream operator.</param>
    /// <returns>An output stream containing only the first message.</returns>
    public static IProducer<T> First<T>(this IProducer<T> source, DeliveryPolicy<T> deliveryPolicy = null, string name = nameof(First))
    {
        return First(source, 1, deliveryPolicy, name);
    }

    /// <summary>
    /// Filter stream to the last n messages.
    /// </summary>
    /// <typeparam name="T">Type of source/output messages.</typeparam>
    /// <param name="source">Source stream.</param>
    /// <param name="count">The number of messages to filter.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    /// <param name="name">An optional name for this stream operator.</param>
    /// <returns>An output stream containing only the last message.</returns>
    public static IProducer<T> Last<T>(this IProducer<T> source, int count, DeliveryPolicy<T> deliveryPolicy = null, string name = nameof(Last))
    {
        List<(T, DateTime)> lastValues = new();
        Processor<T, T> processor = new(
            source.Out.Pipeline,
            (t, envelope, _) =>
            {
                lastValues.Add((t.DeepClone(), envelope.OriginatingTime));
                if (lastValues.Count > count)
                {
                    lastValues.RemoveAt(0);
                }
            },
            (_, emitter) =>
            {
                foreach ((T t, DateTime originatingTime) in lastValues)
                {
                    emitter.Post(t, originatingTime);
                }
            },
            name);

        return source.PipeTo(processor, deliveryPolicy);
    }

    /// <summary>
    /// Filter stream to the last message.
    /// </summary>
    /// <typeparam name="T">Type of source/output messages.</typeparam>
    /// <param name="source">Source stream.</param>
    /// <param name="deliveryPolicy">An optional delivery policy.</param>
    /// <param name="name">An optional name for this stream operator.</param>
    /// <returns>An output stream containing only the last message.</returns>
    public static IProducer<T> Last<T>(this IProducer<T> source, DeliveryPolicy<T> deliveryPolicy = null, string name = nameof(Last))
    {
        bool captured = false;
        T last = default;
        DateTime lastOriginatingTime = default;
        Processor<T, T> processor = new(
            source.Out.Pipeline,
            (t, envelope, _) =>
            {
                captured = true;
                t.DeepClone(ref last);
                lastOriginatingTime = envelope.OriginatingTime;
            },
            (_, emitter) =>
            {
                if (captured)
                {
                    emitter.Post(last, lastOriginatingTime);
                }
            },
            name);

        return source.PipeTo(processor, deliveryPolicy);
    }
}
