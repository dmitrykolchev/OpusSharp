// <copyright file="BatchProcessingTaskConfiguration.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
using Neutrino.Psi.Common;
using Neutrino.Psi.Data.Helpers;
using Newtonsoft.Json;

namespace Neutrino.Psi.Data;

/// <summary>
/// Represents a configuration for a batch processing task.
/// </summary>
public class BatchProcessingTaskConfiguration : ObservableObject
{
    private bool _replayAllRealTime = false;
    private DeliveryPolicySpec _deliveryPolicySpec = DeliveryPolicySpec.Unlimited;
    private bool _enableDiagnostics = false;
    private string _outputStoreName = null;
    private string _outputStorePath = null;
    private string _outputPartitionName = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchProcessingTaskConfiguration"/> class.
    /// </summary>
    public BatchProcessingTaskConfiguration()
    {
    }

    /// <summary>
    /// Gets the name of the configuration.
    /// </summary>
    [Browsable(false)]
    public string Name => "Configuration";

    /// <summary>
    /// Gets or sets a value indicating whether to use the <see cref="ReplayDescriptor.ReplayAllRealTime"/> descriptor when executing this batch task.
    /// </summary>
    [DataMember]
    [DisplayName("Replay in Real Time")]
    [Description("Indicates whether the task will execute by performing replay in real time.")]
    public bool ReplayAllRealTime
    {
        get => _replayAllRealTime;
        set => Set(nameof(ReplayAllRealTime), ref _replayAllRealTime, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to use the <see cref="DeliveryPolicy.LatestMessage"/> pipeline-level delivery policy when executing this batch task.
    /// </summary>
    [DataMember]
    [DisplayName("Delivery Policy")]
    [Description("Indicates the type of global delivery policy to use for the batch task processing pipeline.")]
    public DeliveryPolicySpec DeliveryPolicySpec
    {
        get => _deliveryPolicySpec;
        set => Set(nameof(DeliveryPolicySpec), ref _deliveryPolicySpec, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to enable pipeline diagnostics when running this batch task.
    /// </summary>
    [DataMember]
    [DisplayName("Enable diagnostics")]
    [Description("Indicates whether diagnostics will be enabled on the pipeline while executing the task.")]
    public bool EnableDiagnostics
    {
        get => _enableDiagnostics;
        set => Set(nameof(EnableDiagnostics), ref _enableDiagnostics, value);
    }

    /// <summary>
    /// Gets or sets the output store name.
    /// </summary>
    [DataMember]
    [DisplayName("Output Store Name")]
    [Description("The output store name.")]
    public string OutputStoreName
    {
        get => _outputStoreName;
        set => Set(nameof(OutputStoreName), ref _outputStoreName, value);
    }

    /// <summary>
    /// Gets or sets the output store path.
    /// </summary>
    [DataMember]
    [DisplayName("Output Store Path")]
    [Description("The output store path.")]
    public string OutputStorePath
    {
        get => _outputStorePath;
        set => Set(nameof(OutputStorePath), ref _outputStorePath, value);
    }

    /// <summary>
    /// Gets or sets the output partition name.
    /// </summary>
    [DataMember]
    [DisplayName("Output Partition Name")]
    [Description("The output partition name.")]
    public string OutputPartitionName
    {
        get => _outputPartitionName;
        set => Set(nameof(OutputPartitionName), ref _outputPartitionName, value);
    }

    /// <summary>
    /// Loads a configuration from the specified file.
    /// </summary>
    /// <param name="fileName">The full path name of the configuration file.</param>
    /// <returns>The loaded configuration.</returns>
    public static BatchProcessingTaskConfiguration Load(string fileName)
    {
        JsonSerializer serializer = JsonSerializer.Create(
            new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                SerializationBinder = new SafeSerializationBinder(),
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            });

        using System.IO.StreamReader jsonFile = File.OpenText(fileName);
        using JsonTextReader jsonReader = new(jsonFile);
        return serializer.Deserialize<BatchProcessingTaskConfiguration>(jsonReader);
    }

    /// <summary>
    /// Saves the configuration to a file.
    /// </summary>
    /// <param name="fileName">The full path name of the configuration file.</param>
    public void Save(string fileName)
    {
        JsonSerializer serializer = JsonSerializer.Create(
            new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                SerializationBinder = new SafeSerializationBinder(),
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            });

        using StreamWriter jsonFile = File.CreateText(fileName);
        using JsonTextWriter jsonWriter = new(jsonFile);
        serializer.Serialize(jsonWriter, this, typeof(BatchProcessingTaskConfiguration));
    }

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <param name="error">A message describing the issue if the configuration is invalid.</param>
    /// <returns>True if the configuration is valid, false otherwise.</returns>
    public virtual bool Validate(out string error)
    {
        error = null;
        return true;
    }
}
