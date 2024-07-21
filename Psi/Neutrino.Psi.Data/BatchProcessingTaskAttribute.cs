﻿// <copyright file="BatchProcessingTaskAttribute.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Neutrino.Psi.Data;

/// <summary>
/// Represents a batch processing task attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class BatchProcessingTaskAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BatchProcessingTaskAttribute"/> class.
    /// </summary>
    /// <param name="name">Name of this task.</param>
    public BatchProcessingTaskAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentNullException(nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// Gets the task name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the task description.
    /// </summary>
    public string Description { get; set; } = null;

    /// <summary>
    /// Gets or sets the path to the associated icon.
    /// </summary>
    public string IconSourcePath { get; set; } = null;

    /// <summary>
    /// Gets or sets a value indicating whether to use the <see cref="ReplayDescriptor.ReplayAllRealTime"/> descriptor when executing this batch task.
    /// </summary>
    public bool ReplayAllRealTime { get; set; } = false;

    /// <summary>
    /// Gets or sets the pipeline level delivery policy specifier.
    /// </summary>
    public DeliveryPolicySpec DeliveryPolicySpec { get; set; } = DeliveryPolicySpec.Unlimited;

    /// <summary>
    /// Gets or sets a value indicating whether to enable pipeline diagnostics when running this batch task.
    /// </summary>
    public bool EnableDiagnostics { get; set; } = false;

    /// <summary>
    /// Gets or sets the output store name.
    /// </summary>
    public string OutputStoreName { get; set; } = null;

    /// <summary>
    /// Gets or sets the output store path.
    /// </summary>
    public string OutputStorePath { get; set; } = null;

    /// <summary>
    /// Gets or sets the output partition name.
    /// </summary>
    public string OutputPartitionName { get; set; } = null;
}
