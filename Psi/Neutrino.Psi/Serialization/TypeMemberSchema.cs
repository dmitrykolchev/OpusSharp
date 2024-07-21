// <copyright file="TypeMemberSchema.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Reflection;

namespace Microsoft.Psi.Serialization;

/// <summary>
/// The type member schema information.
/// </summary>
public sealed class TypeMemberSchema
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TypeMemberSchema"/> class.
    /// </summary>
    /// <param name="name">The member name.</param>
    /// <param name="type">The type name, in contract form (either data contract name or assembly-qualified name).</param>
    /// <param name="isRequired">True if the member is required.</param>
    /// <param name="memberInfo">A fieldInfo or PropertyInfo object for this member. Optional.</param>
    public TypeMemberSchema(string name, string type, bool isRequired, MemberInfo memberInfo = null)
    {
        Name = name;
        Type = type;
        IsRequired = isRequired;
        MemberInfo = memberInfo;
    }

    /// <summary>
    /// Gets the name of the member.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the type of the member, in contract form (either data contract name or assembly-qualified name).
    /// </summary>
    public string Type { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the member is required.
    /// </summary>
    public bool IsRequired { get; private set; }

    /// <summary>
    /// Gets the PropertyInfo or FieldInfo specification for this member.
    /// </summary>
    public MemberInfo MemberInfo { get; private set; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Name;
    }
}
