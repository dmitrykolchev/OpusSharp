﻿// <copyright file="DeliveryPolicy{T}.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi;


/// <summary>
/// Encapsulates the options for message delivery behavior.
/// </summary>
/// <typeparam name="T">The type of messages.</typeparam>
public class DeliveryPolicy<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeliveryPolicy{T}"/> class.
    /// </summary>
    /// <param name="initialQueueSize">The initial receiver queue size.</param>
    /// <param name="maximumQueueSize">The maximum receiver queue size.</param>
    /// <param name="maximumLatency">The maximum latency allowable for messages to be delivered.</param>
    /// <param name="throttleQueueSize">The number of messages in the receiver queue at and above which the upstream producer will be blocked.</param>
    /// <param name="attemptSynchronous">A value indicating whether to attempt synchronous delivery.</param>
    /// <param name="guaranteeDelivery">A function that indicates for which messages delivery should be guaranteed.</param>
    /// <param name="name">Name used for debugging and diagnostics.</param>
    internal DeliveryPolicy(
        int initialQueueSize = DeliveryPolicy.DefaultInitialQueueSize,
        int maximumQueueSize = int.MaxValue,
        TimeSpan? maximumLatency = null,
        int? throttleQueueSize = null,
        bool attemptSynchronous = false,
        Func<T, bool> guaranteeDelivery = null,
        string name = null)
    {
        InitialQueueSize = initialQueueSize;
        MaximumQueueSize = maximumQueueSize;
        MaximumLatency = maximumLatency;
        ThrottleQueueSize = throttleQueueSize;
        AttemptSynchronousDelivery = attemptSynchronous;
        GuaranteeDelivery = guaranteeDelivery;
        Name = name ?? $"{nameof(DeliveryPolicy<T>)}({nameof(initialQueueSize)}={initialQueueSize}, {nameof(maximumQueueSize)}={maximumQueueSize}, {nameof(maximumLatency)}={maximumLatency}, {nameof(throttleQueueSize)}={throttleQueueSize}, {nameof(attemptSynchronous)}={attemptSynchronous})";
    }

    /// <summary>
    /// Gets the number of messages in the receiver queue at and above which the upstream producer will be blocked.
    /// </summary>
    /// <remarks>Use with care, as it affects all other subscribers to the same producer and can introduce deadlocks (a blocked producer cannot process control messages anymore).</remarks>
    public int? ThrottleQueueSize { get; private set; }

    /// <summary>
    /// Gets the initial size of the receiver queue that holds the messages pending delivery.
    /// </summary>
    public int InitialQueueSize { get; private set; }

    /// <summary>
    /// Gets the maximum size of the receiver queue that holds the messages pending delivery.
    /// </summary>
    public int MaximumQueueSize { get; private set; }

    /// <summary>
    /// Gets the maximum latency of items to be delivered. Items with a latency larger than this are discarded.
    /// </summary>
    public TimeSpan? MaximumLatency { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the runtime should attempt synchronous delivery when possible.
    /// </summary>
    public bool AttemptSynchronousDelivery { get; private set; }

    /// <summary>
    /// Gets a function that indicates for which messages the delivery should be guaranteed.
    /// </summary>
    public Func<T, bool> GuaranteeDelivery { get; private set; }

    /// <summary>
    /// Gets name used for debugging and diagnostics.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Implicitly cast from an untyped to a typed delivery policy.
    /// </summary>
    /// <param name="policy">The untyped delivery policy.</param>
    public static implicit operator DeliveryPolicy<T>(DeliveryPolicy policy)
    {
        return policy == null ? null : new(
               policy.InitialQueueSize,
               policy.MaximumQueueSize,
               policy.MaximumLatency,
               policy.ThrottleQueueSize,
               policy.AttemptSynchronousDelivery,
               null,
               policy.Name);
    }

    /// <summary>
    /// Implicit cast operator from <see cref="DeliveryPolicySpec"/> to <see cref="DeliveryPolicy{T}"/>.
    /// </summary>
    /// <param name="deliveryPolicySpec">The delivery policy specification.</param>
    public static implicit operator DeliveryPolicy<T>(DeliveryPolicySpec deliveryPolicySpec)
    {
        return deliveryPolicySpec switch
        {
            DeliveryPolicySpec.Unlimited => DeliveryPolicy.Unlimited,
            DeliveryPolicySpec.LatestMessage => DeliveryPolicy.LatestMessage,
            DeliveryPolicySpec.Throttle => DeliveryPolicy.Throttle,
            DeliveryPolicySpec.SynchronousOrThrottle => DeliveryPolicy.SynchronousOrThrottle,
            _ => DeliveryPolicy.Unlimited,
        };
    }

    /// <summary>
    /// Creates a delivery policy with guarantees by adding a message guaranteed function to an existing delivery policy.
    /// </summary>
    /// <param name="guaranteeDelivery">A function that evaluates whether the delivery of a given message should be guaranteed.</param>
    /// <returns>The typed delivery policy with guarantees.</returns>
    public DeliveryPolicy<T> WithGuarantees(Func<T, bool> guaranteeDelivery)
    {
        return new(
                DeliveryPolicy.DefaultInitialQueueSize,
                MaximumQueueSize,
                MaximumLatency,
                ThrottleQueueSize,
                AttemptSynchronousDelivery,
                GuaranteeDelivery == null ? guaranteeDelivery : t => GuaranteeDelivery(t) || guaranteeDelivery(t),
                $"{Name}.WithGuarantees");
    }
}
