// <copyright file="PipelineElementKind.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Microsoft.Psi.Diagnostics;

/// <summary>
/// Pipeline element kind.
/// </summary>
public enum PipelineElementKind
{
    /// <summary>
    /// Represents a source component.
    /// </summary>
    Source,

    /// <summary>
    /// Represents a purely reactive component.
    /// </summary>
    Reactive,

    /// <summary>
    /// Represents a Connector component.
    /// </summary>
    Connector,

    /// <summary>
    /// Represents a Subpipeline component.
    /// </summary>
    Subpipeline,
}
