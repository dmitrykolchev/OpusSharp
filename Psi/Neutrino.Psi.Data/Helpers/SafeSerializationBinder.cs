// <copyright file="SafeSerializationBinder.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using Newtonsoft.Json.Serialization;

namespace Neutrino.Psi.Data.Helpers;

/// <summary>
/// Represents a JSON serialization binder that will only deserialize
/// types in assemblies referenced directly by the application or
/// assemblies that have been allowed to load by the user.
/// </summary>
public class SafeSerializationBinder : ISerializationBinder
{
    /// <inheritdoc/>
    public void BindToName(Type serializedType, out string assemblyName, out string typeName)
    {
        assemblyName = serializedType.Assembly.FullName;
        typeName = serializedType.FullName;
    }

    /// <inheritdoc/>
    public Type BindToType(string assemblyName, string typeName)
    {
        return TypeResolutionHelper.GetVerifiedType($"{typeName}, {assemblyName}");
    }
}
