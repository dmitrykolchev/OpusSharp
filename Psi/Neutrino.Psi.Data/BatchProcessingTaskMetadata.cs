// <copyright file="BatchProcessingTaskMetadata.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.IO;

namespace Microsoft.Psi.Data;

/// <summary>
/// Represents metadata about a dynamically loaded batch processing task
/// and provides functionality for configuring and executing the task.
/// </summary>
public class BatchProcessingTaskMetadata
{
    private readonly BatchProcessingTaskAttribute _batchProcessingTaskAttribute = null;
    private readonly Type _batchProcessingTaskType;
    private readonly string _batchProcessingTaskConfigurationsPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchProcessingTaskMetadata"/> class.
    /// </summary>
    /// <param name="batchProcessingTaskType">The batch processing task type.</param>
    /// <param name="batchProcessingTaskAttribute">The batch processing task attribute.</param>
    /// <param name="batchProcessingTaskConfigurationsPath">The folder in which batch processing task configurations are saved.</param>
    public BatchProcessingTaskMetadata(Type batchProcessingTaskType, BatchProcessingTaskAttribute batchProcessingTaskAttribute, string batchProcessingTaskConfigurationsPath)
    {
        _batchProcessingTaskType = batchProcessingTaskType;
        _batchProcessingTaskAttribute = batchProcessingTaskAttribute;
        _batchProcessingTaskConfigurationsPath = Path.Combine(batchProcessingTaskConfigurationsPath, batchProcessingTaskAttribute.Name);
    }

    /// <summary>
    /// Gets the batch processing task name.
    /// </summary>
    public string Name => _batchProcessingTaskAttribute.Name;

    /// <summary>
    /// Gets the batch processing task description.
    /// </summary>
    public string Description => _batchProcessingTaskAttribute.Description;

    /// <summary>
    /// Gets the batch processing task icon source path.
    /// </summary>
    public string IconSourcePath => _batchProcessingTaskAttribute.IconSourcePath;

    /// <summary>
    /// Gets the folder under which configurations for this batch processing task should be stored.
    /// </summary>
    public string ConfigurationsPath => _batchProcessingTaskConfigurationsPath;

    /// <summary>
    /// Gets the namespace for the batch processing task type.
    /// </summary>
    public string Namespace => _batchProcessingTaskType.Namespace;

    /// <summary>
    /// Gets or sets the name of the most recently used configuration.
    /// </summary>
    public string MostRecentlyUsedConfiguration { get; set; }

    /// <summary>
    /// Gets the default configuration for the batch processing task.
    /// </summary>
    /// <returns>The default configuration.</returns>
    public BatchProcessingTaskConfiguration GetDefaultConfiguration()
    {
        IBatchProcessingTask batchProcessingTask = Activator.CreateInstance(_batchProcessingTaskType) as IBatchProcessingTask;
        return batchProcessingTask.GetDefaultConfiguration();
    }

    /// <summary>
    /// Creates a corresponding batch processing task instance.
    /// </summary>
    /// <returns>The batch processing task instance.</returns>
    public IBatchProcessingTask CreateBatchProcessingTask()
    {
        return Activator.CreateInstance(_batchProcessingTaskType) as IBatchProcessingTask;
    }
}
