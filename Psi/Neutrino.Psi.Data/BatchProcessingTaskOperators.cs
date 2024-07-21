// <copyright file="BatchProcessingTaskOperators.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;

namespace Microsoft.Psi.Data;

/// <summary>
/// Provides static extension methods related to batch processing tasks.
/// </summary>
public static class BatchProcessingTaskOperators
{
    /// <summary>
    /// Checks if a specified type is a batch processing task type.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the specified type is a batch processing task type, o/w false.</returns>
    public static bool IsBatchProcessingTaskType(this Type type)
    {
        if (type == null)
        {
            return false;
        }
        else if (type.IsGenericType)
        {
            if (type.GetGenericTypeDefinition() == typeof(BatchProcessingTask<>))
            {
                return true;
            }
            else
            {
                return type.BaseType.IsBatchProcessingTaskType();
            }
        }
        else
        {
            return type.BaseType.IsBatchProcessingTaskType();
        }
    }
}
