// <copyright file="IEnumerableAnnotationValueSchema.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Collections.Generic;

namespace Neutrino.Psi.Data.Annotations;

/// <summary>
/// Defines an enumerable annotation value schema, i.e., a value schema with
/// a fixed set of possible values.
/// </summary>
public interface IEnumerableAnnotationValueSchema : IAnnotationValueSchema
{
    /// <summary>
    /// Gets the set of possible values for this annotation value schema.
    /// </summary>
    /// <returns>The set of possible values.</returns>
    public IEnumerable<IAnnotationValue> GetPossibleAnnotationValues();
}
