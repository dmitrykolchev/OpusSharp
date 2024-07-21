﻿// <copyright file="RuntimeInfo.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Reflection;


namespace Neutrino.Psi.Common;

/// <summary>
/// Runtime info metadata.
/// </summary>
public class RuntimeInfo : Metadata
{
    /// <summary>
    /// The latest version of the serialization subsystem.
    /// </summary>
    public const int LatestSerializationSystemVersion = 2;

    /// <summary>
    /// Gets name of the executing assembly.
    /// </summary>
    public static readonly AssemblyName RuntimeName = Assembly.GetExecutingAssembly().GetName();

    /// <summary>
    /// Gets the latest (current) runtime info.
    /// </summary>
    public static readonly RuntimeInfo Latest = new();

    internal RuntimeInfo(int serializationSystemVersion = LatestSerializationSystemVersion)
        : this(
              RuntimeName.FullName,
              version: (RuntimeName.Version.Major << 16) | RuntimeName.Version.Minor,
              serializationSystemVersion: serializationSystemVersion)
    {
    }

    internal RuntimeInfo(string name, int version, int serializationSystemVersion)
        : base(MetadataKind.RuntimeInfo, name, 0, version, serializationSystemVersion)
    {
    }

    internal override void Serialize(BufferWriter metadataBuffer)
    {
        // Serialization follows a legacy pattern of fields, as described
        // in the comments at the top of the Metadata.Deserialize method.
        metadataBuffer.Write(Name);
        metadataBuffer.Write(Id);
        metadataBuffer.Write(default(string));      // this metadata field is not used by RuntimeInfo
        metadataBuffer.Write(Version);
        metadataBuffer.Write(default(string));      // this metadata field is not used by RuntimeInfo
        metadataBuffer.Write(SerializationSystemVersion);
        metadataBuffer.Write(default(ushort));      // this metadata field is not used by RuntimeInfo
        metadataBuffer.Write((ushort)Kind);
    }
}
