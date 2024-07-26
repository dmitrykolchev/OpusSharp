// <copyright file="DeliveryPolicySpec.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Neutrino.Psi.Common;

/// <summary>
/// Defines specifiers for global delivery policies.
/// </summary>
public enum DeliveryPolicySpec
{
    /// <summary>
    /// Specifies the <see cref="DeliveryPolicy.Unlimited"/> delivery policy.
    /// </summary>
    Unlimited,

    /// <summary>
    /// Specifies the <see cref="DeliveryPolicy.LatestMessage"/> delivery policy.
    /// </summary>
    LatestMessage,

    /// <summary>
    /// Specifies the <see cref="DeliveryPolicy.Throttle"/> delivery policy.
    /// </summary>
    Throttle,

    /// <summary>
    /// Specifies the <see cref="DeliveryPolicy.SynchronousOrThrottle"/> delivery policy.
    /// </summary>
    SynchronousOrThrottle,
}
