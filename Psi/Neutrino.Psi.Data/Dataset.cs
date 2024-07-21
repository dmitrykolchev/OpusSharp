// <copyright file="Dataset.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Psi.Data.Helpers;
using Newtonsoft.Json;

namespace Microsoft.Psi.Data;

/// <summary>
/// Represents a dataset (collection of sessions) to be reasoned over.
/// </summary>
[DataContract(Namespace = "http://www.microsoft.com/psi")]
public class Dataset
{
    /// <summary>
    /// Default name of a dataset.
    /// </summary>
    public const string DefaultName = "Untitled Dataset";

    private string _name;

    /// <summary>
    /// Initializes a new instance of the <see cref="Dataset"/> class.
    /// </summary>
    /// <param name="name">The name of the new dataset. Default is <see cref="DefaultName"/>.</param>
    /// <param name="filename">An optional filename that indicates the location to save the dataset.<see cref="DefaultName"/>.</param>
    /// <param name="autoSave">Whether the dataset automatically autosave changes if a path is given (optional, default is false).</param>
    [JsonConstructor]
    public Dataset(string name = Dataset.DefaultName, string filename = "", bool autoSave = false)
    {
        Name = name;
        Filename = filename;
        AutoSave = autoSave;
        InternalSessions = new List<Session>();
        if (AutoSave && filename == string.Empty)
        {
            throw new ArgumentException("filename needed to be provided for autosave dataset.");
        }
    }

    /// <summary>
    /// Event raise when the dataset's structure changed.
    /// </summary>
    public event EventHandler DatasetChanged;

    /// <summary>
    /// Gets or sets the name of this dataset.
    /// </summary>
    [DataMember]
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnDatasetChanged();
        }
    }

    /// <summary>
    /// Gets or sets the current save path of this dataset.
    /// </summary>
    public string Filename { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether autosave is enabled.
    /// </summary>
    public bool AutoSave { get; set; }

    /// <summary>
    /// Gets a value indicating whether changes to this dataset have been saved.
    /// </summary>
    public bool HasUnsavedChanges { get; private set; } = false;

    /// <summary>
    /// Gets the originating time interval (earliest to latest) of the messages in this dataset.
    /// </summary>
    [IgnoreDataMember]
    public TimeInterval MessageOriginatingTimeInterval =>
        TimeInterval.Coverage(
            InternalSessions
                .Where(s => s.MessageOriginatingTimeInterval.Left > DateTime.MinValue && s.MessageOriginatingTimeInterval.Right < DateTime.MaxValue)
                .Select(s => s.MessageOriginatingTimeInterval));

    /// <summary>
    /// Gets the creation time interval (earliest to latest) of the messages in this dataset.
    /// </summary>
    [IgnoreDataMember]
    public TimeInterval MessageCreationTimeInterval =>
        TimeInterval.Coverage(
            InternalSessions
                .Where(s => s.MessageCreationTimeInterval.Left > DateTime.MinValue && s.MessageCreationTimeInterval.Right < DateTime.MaxValue)
                .Select(s => s.MessageCreationTimeInterval));

    /// <summary>
    /// Gets the stream open-close time interval in this dataset.
    /// </summary>
    [IgnoreDataMember]
    public TimeInterval TimeInterval =>
        TimeInterval.Coverage(
            InternalSessions
                .Where(s => s.TimeInterval.Left > DateTime.MinValue && s.TimeInterval.Right < DateTime.MaxValue)
                .Select(s => s.TimeInterval));

    /// <summary>
    /// Gets the size of the dataset, in bytes.
    /// </summary>
    public long? Size => InternalSessions.Sum(p => p.Size);

    /// <summary>
    /// Gets the number of streams in the dataset.
    /// </summary>
    public long? StreamCount => InternalSessions.Sum(p => p.StreamCount);

    /// <summary>
    /// Gets the collection of sessions in this dataset.
    /// </summary>
    [IgnoreDataMember]
    public ReadOnlyCollection<Session> Sessions => InternalSessions.AsReadOnly();

    [DataMember(Name = "Sessions")]
    private List<Session> InternalSessions { get; set; }

    /// <summary>
    /// Loads a dataset from the specified file.
    /// </summary>
    /// <param name="filename">The name of the file that contains the dataset to be loaded.</param>
    /// <param name="autoSave">A value to indicate whether to enable autosave (optional, default is false).</param>
    /// <returns>The newly loaded dataset.</returns>
    public static Dataset Load(string filename, bool autoSave = false)
    {
#pragma warning disable SYSLIB0050 // Type or member is obsolete
        JsonSerializer serializer = JsonSerializer.Create(
            new JsonSerializerSettings()
            {
                Context = new StreamingContext(StreamingContextStates.File, filename),
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                SerializationBinder = new SafeSerializationBinder(),
            });
#pragma warning restore SYSLIB0050 // Type or member is obsolete
        using System.IO.StreamReader jsonFile = File.OpenText(filename);
        using JsonTextReader jsonReader = new(jsonFile);
        Dataset dataset = serializer.Deserialize<Dataset>(jsonReader);
        dataset.AutoSave = autoSave;
        dataset.Filename = filename;
        return dataset;
    }

    /// <summary>
    /// Creates a new dataset with a single session and partition from a specified stream reader.
    /// </summary>
    /// <param name="streamReader">The stream reader.</param>
    /// <param name="sessionName">An optional session name (defaults to the stream reader name).</param>
    /// <param name="partitionName">An optional partition name (defaults to the stream reader name.).</param>
    /// <param name="progress">An optional progress updates receiver.</param>
    /// <returns>The task for creating a new dataset with a single session and partition from a specified stream reader.</returns>
    public static async Task<Dataset> CreateAsync(
        IStreamReader streamReader,
        string sessionName = null,
        string partitionName = null,
        IProgress<(string, double)> progress = null)
    {
        Dataset dataset = new();
        await dataset.AddSessionAsync(streamReader, sessionName, partitionName, progress);
        return dataset;
    }

    /// <summary>
    /// Creates a new session within the dataset.
    /// </summary>
    /// <param name="sessionName">The session name.</param>
    /// <returns>The newly created session.</returns>
    public Session AddEmptySession(string sessionName = Session.DefaultName)
    {
        Session session = new(this, sessionName);
        AddSession(session);
        return session;
    }

    /// <summary>
    /// Removes the specified session from the dataset.
    /// </summary>
    /// <param name="session">The session to remove.</param>
    public void RemoveSession(Session session)
    {
        InternalSessions.Remove(session);
        OnDatasetChanged();
    }

    /// <summary>
    /// Appends sessions from a specified dataset to this dataset.
    /// </summary>
    /// <param name="inputDataset">The dataset to append from.</param>
    /// <param name="progress">An optional progress updates receiver.</param>
    /// <returns>The task for appending sessions to this dataset.</returns>
    public async Task AppendAsync(Dataset inputDataset, IProgress<(string, double)> progress = null)
    {
        foreach (Session session in inputDataset.Sessions)
        {
            Session newSession = AddEmptySession();
            newSession.Name = session.Name;
            int partitionsCount = session.Partitions.Count;
            for (int i = 0; i < partitionsCount; i++)
            {
                IPartition partition = session.Partitions[i];
                await newSession.AddPartitionAsync(
                    StreamReader.Create(partition.StoreName, partition.StorePath, partition.StreamReaderTypeName),
                    partition.Name,
                    new Progress<(string, double)>(t => progress?.Report(($"Adding session {session.Name}\n{t.Item1}", (i + t.Item2) / partitionsCount))));
            }
        }

        OnDatasetChanged();
    }

    /// <summary>
    /// Saves this dataset.
    /// </summary>
    /// <param name="filename">The filename that indicates the location to save the dataset.</param>
    public void SaveAs(string filename)
    {
        Filename = filename;
        Save();
    }

    /// <summary>
    /// Saves this dataset.
    /// </summary>
    public void Save()
    {
        if (Filename == string.Empty)
        {
            throw new ArgumentException("filename to save the dataset must be set before save operation.");
        }

#pragma warning disable SYSLIB0050 // Type or member is obsolete
        JsonSerializer serializer = JsonSerializer.Create(
            new JsonSerializerSettings()
            {
                // pass the dataset filename in the context to allow relative store paths to be computed using the RelativePathConverter
                Context = new StreamingContext(StreamingContextStates.File, Filename),
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                SerializationBinder = new SafeSerializationBinder(),
            });
#pragma warning restore SYSLIB0050 // Type or member is obsolete
        using StreamWriter jsonFile = File.CreateText(Filename);
        using JsonTextWriter jsonWriter = new(jsonFile);
        serializer.Serialize(jsonWriter, this);
        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Adds a session containing a single partition using the specified stream reader.
    /// </summary>
    /// <param name="streamReader">The stream reader of the partition.</param>
    /// <param name="sessionName">An optional name for the session (defaults to the stream reader name).</param>
    /// <param name="partitionName">An optional name for the partition (defaults to the stream reader name).</param>
    /// <param name="progress">An optional progress updates receiver.</param>
    /// <returns>The task for adding a session containing a single partition using the specified stream reader..</returns>
    public async Task<Session> AddSessionAsync(
        IStreamReader streamReader,
        string sessionName = null,
        string partitionName = null,
        IProgress<(string, double)> progress = null)
    {
        Session session = new(this, sessionName ?? streamReader.Name);
        await session.AddPartitionAsync(streamReader, partitionName, progress);
        AddSession(session);
        return session;
    }

    /// <summary>
    /// Adds a session containing a single partition from a specified \psi store.
    /// </summary>
    /// <param name="storeName">The name of the \psi store.</param>
    /// <param name="storePath">The path to the \psi store.</param>
    /// <param name="sessionName">An optional name for the session (defaults to the \psi store name).</param>
    /// <param name="partitionName">An optional name for the partition (defaults to the \psi store name).</param>
    /// <param name="progress">An optional progress updates receiver.</param>
    /// <returns>The task for adding a session containing a single partition from a specified \psi store.</returns>
    public async Task<Session> AddSessionFromPsiStoreAsync(
        string storeName,
        string storePath,
        string sessionName = null,
        string partitionName = null,
        IProgress<(string, double)> progress = null)
    {
        return await AddSessionAsync(new PsiStoreStreamReader(storeName, storePath), sessionName, partitionName, progress);
    }

    /// <summary>
    /// Compute derived results for each session in the dataset.
    /// </summary>
    /// <typeparam name="TResult">The type of data of the derived result.</typeparam>
    /// <param name="computeDerived">The action to be invoked to derive results.</param>
    /// <returns>List of results.</returns>
    public IReadOnlyList<TResult> ComputeDerived<TResult>(
        Action<Pipeline, SessionImporter, TResult> computeDerived)
        where TResult : class, new()
    {
        List<TResult> results = new();
        foreach (Session session in Sessions)
        {
            // the first partition is where we put the data if output is not specified
            IPartition inputPartition = session.Partitions.FirstOrDefault();

            // create and run the pipeline
            using Pipeline pipeline = Pipeline.Create();
            SessionImporter importer = SessionImporter.Open(pipeline, session);

            TResult result = new();
            computeDerived(pipeline, importer, result);

            DateTime startTime = DateTime.UtcNow;
            Console.WriteLine($"Computing derived features on {inputPartition.StorePath} ...");
            pipeline.Run(ReplayDescriptor.ReplayAll);

            DateTime finishTime = DateTime.UtcNow;
            Console.WriteLine($" - Time elapsed: {(finishTime - startTime).TotalMinutes:0.00} min.");

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Asynchronously computes a derived partition for each session in the dataset.
    /// </summary>
    /// <param name="computeDerived">The action to be invoked to compute derive partitions.</param>
    /// <param name="outputPartitionName">The name of the output partition to be created.</param>
    /// <param name="overwrite">An optional flag indicating whether the partition should be overwritten. Default is false.</param>
    /// <param name="outputStoreName">An optional name for the output data store. Default is the output partition name.</param>
    /// <param name="outputStorePath">An optional path for the output data store. Default is the path for the first partition in the session.</param>
    /// <param name="replayDescriptor">An optional replay descriptor to use when creating the derived partition.</param>
    /// <param name="deliveryPolicy">Pipeline-level delivery policy to use when creating the derived partition.</param>
    /// <param name="enableDiagnostics">Indicates whether to enable collecting and publishing diagnostics information on the Pipeline.Diagnostics stream.</param>
    /// <param name="progress">An optional progress object to be used for reporting progress.</param>
    /// <param name="cancellationToken">An optional token for canceling the asynchronous task.</param>
    /// <returns>A task that represents the asynchronous compute derive partition operation.</returns>
    public async Task CreateDerivedPartitionAsync(
        Action<Pipeline, SessionImporter, Exporter> computeDerived,
        string outputPartitionName,
        bool overwrite = false,
        string outputStoreName = null,
        string outputStorePath = null,
        ReplayDescriptor replayDescriptor = null,
        DeliveryPolicy deliveryPolicy = null,
        bool enableDiagnostics = false,
        IProgress<(string, double)> progress = null,
        CancellationToken cancellationToken = default)
    {
        await CreateDerivedPartitionAsync<long>(
                    (p, si, e, _) => computeDerived(p, si, e),
                    0,
                    outputPartitionName,
                    overwrite,
                    outputStoreName,
                    outputStorePath,
                    replayDescriptor,
                    deliveryPolicy,
                    enableDiagnostics,
                    progress,
                    cancellationToken);
    }

    /// <summary>
    /// Asynchronously computes a derived partition for each session in the dataset.
    /// </summary>
    /// <param name="computeDerived">The action to be invoked to compute derive partitions.</param>
    /// <param name="outputPartitionName">The name of the output partition to be created.</param>
    /// <param name="overwrite">An optional flag indicating whether the partition should be overwritten. Default is false.</param>
    /// <param name="outputStoreName">An optional name for the output data store. Default is the output partition name.</param>
    /// <param name="outputStorePathFunction">An optional function to determine output store path for each given session. Default is the path for the first partition in the session.</param>
    /// <param name="replayDescriptor">An optional replay descriptor to use when creating the derived partition.</param>
    /// <param name="deliveryPolicy">Pipeline-level delivery policy to use when creating the derived partition.</param>
    /// <param name="enableDiagnostics">Indicates whether to enable collecting and publishing diagnostics information on the Pipeline.Diagnostics stream.</param>
    /// <param name="progress">An optional progress object to be used for reporting progress.</param>
    /// <param name="cancellationToken">An optional token for canceling the asynchronous task.</param>
    /// <returns>A task that represents the asynchronous compute derive partition operation.</returns>
    public async Task CreateDerivedPartitionAsync(
        Action<Pipeline, SessionImporter, Exporter> computeDerived,
        string outputPartitionName,
        bool overwrite,
        string outputStoreName,
        Func<Session, string> outputStorePathFunction,
        ReplayDescriptor replayDescriptor = null,
        DeliveryPolicy deliveryPolicy = null,
        bool enableDiagnostics = false,
        IProgress<(string, double)> progress = null,
        CancellationToken cancellationToken = default)
    {
        await CreateDerivedPartitionAsync<long>(
                    (p, si, e, _) => computeDerived(p, si, e),
                    0,
                    outputPartitionName,
                    overwrite,
                    outputStoreName,
                    outputStorePathFunction,
                    replayDescriptor,
                    deliveryPolicy,
                    enableDiagnostics,
                    progress,
                    cancellationToken);
    }

    /// <summary>
    /// Asynchronously computes a derived partition for each session in the dataset.
    /// </summary>
    /// <typeparam name="TParameter">The type of parameter passed to the action.</typeparam>
    /// <param name="computeDerived">The action to be invoked to derive partitions.</param>
    /// <param name="parameter">The parameter to be passed to the action.</param>
    /// <param name="outputPartitionName">The output partition name to be created.</param>
    /// <param name="overwrite">Flag indicating whether the partition should be overwritten. Default is false.</param>
    /// <param name="outputStoreName">An optional name for the output data store. Default is the output partition name.</param>
    /// <param name="outputStorePath">An optional path for the output data store. Default is the path for the first partition in the session.</param>
    /// <param name="replayDescriptor">The replay descriptor to us.</param>
    /// <param name="deliveryPolicy">Pipeline-level delivery policy to use when creating the derived partition.</param>
    /// <param name="enableDiagnostics">Indicates whether to enable collecting and publishing diagnostics information on the Pipeline.Diagnostics stream.</param>
    /// <param name="progress">An object that can be used for reporting progress.</param>
    /// <param name="cancellationToken">A token for canceling the asynchronous task.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task CreateDerivedPartitionAsync<TParameter>(
        Action<Pipeline, SessionImporter, Exporter, TParameter> computeDerived,
        TParameter parameter,
        string outputPartitionName,
        bool overwrite = false,
        string outputStoreName = null,
        string outputStorePath = null,
        ReplayDescriptor replayDescriptor = null,
        DeliveryPolicy deliveryPolicy = null,
        bool enableDiagnostics = false,
        IProgress<(string, double)> progress = null,
        CancellationToken cancellationToken = default)
    {
        await CreateDerivedPartitionAsync(
                    computeDerived,
                    parameter,
                    outputPartitionName,
                    overwrite,
                    outputStoreName,
                    _ => outputStorePath,
                    replayDescriptor,
                    deliveryPolicy,
                    enableDiagnostics,
                    progress,
                    cancellationToken);
    }

    /// <summary>
    /// Asynchronously computes a derived partition for each session in the dataset.
    /// </summary>
    /// <typeparam name="TParameter">The type of parameter passed to the action.</typeparam>
    /// <param name="computeDerived">The action to be invoked to derive partitions.</param>
    /// <param name="parameter">The parameter to be passed to the action.</param>
    /// <param name="outputPartitionName">The name of the output partition to be created.</param>
    /// <param name="overwrite">An optional flag indicating whether the partition should be overwritten. Default is false.</param>
    /// <param name="outputStoreName">An optional name for the output data store. Default is the output partition name.</param>
    /// <param name="outputStorePathFunction">An optional function to determine output store path for each given session. Default is the path for the first partition in the session.</param>
    /// <param name="replayDescriptor">An optional replay descriptor to use when creating the derived partition.</param>
    /// <param name="deliveryPolicy">Pipeline-level delivery policy to use when creating the derived partition.</param>
    /// <param name="enableDiagnostics">Indicates whether to enable collecting and publishing diagnostics information on the Pipeline.Diagnostics stream.</param>
    /// <param name="progress">An optional progress object to be used for reporting progress.</param>
    /// <param name="cancellationToken">An optional token for canceling the asynchronous task.</param>
    /// <returns>A task that represents the asynchronous compute derive partition operation.</returns>
    public async Task CreateDerivedPartitionAsync<TParameter>(
        Action<Pipeline, SessionImporter, Exporter, TParameter> computeDerived,
        TParameter parameter,
        string outputPartitionName,
        bool overwrite,
        string outputStoreName,
        Func<Session, string> outputStorePathFunction,
        ReplayDescriptor replayDescriptor = null,
        DeliveryPolicy deliveryPolicy = null,
        bool enableDiagnostics = false,
        IProgress<(string, double)> progress = null,
        CancellationToken cancellationToken = default)
    {
        double totalDuration = default;
        List<double> sessionStart = Sessions.Select(s =>
            {
                double currentDuration = totalDuration;
                totalDuration += s.TimeInterval.Span.TotalSeconds;
                return currentDuration;
            }).ToList();
        List<double> sessionDuration = Sessions.Select(s => s.TimeInterval.Span.TotalSeconds).ToList();

        for (int i = 0; i < Sessions.Count; i++)
        {
            Session session = Sessions[i];
            await session.CreateDerivedPsiStorePartitionAsync(
                computeDerived,
                parameter,
                outputPartitionName,
                overwrite,
                outputStoreName ?? outputPartitionName,
                outputStorePathFunction(session) ?? session.Partitions.First().StorePath,
                replayDescriptor,
                deliveryPolicy,
                enableDiagnostics,
                progress != null ? new Progress<(string, double)>(tuple => progress.Report((tuple.Item1, (sessionStart[i] + (tuple.Item2 * sessionDuration[i])) / totalDuration))) : null,
                cancellationToken);
        }
    }

    /// <summary>
    /// Asynchronously runs a batch processing task of a specified type on the dataset.
    /// </summary>
    /// <typeparam name="TBatchProcessingTask">The type of the batch processing task.</typeparam>
    /// <param name="configuration">The batch processing task configuration.</param>
    /// <param name="progress">An optional progress object to be used for reporting progress.</param>
    /// <param name="cancellationToken">An optional token for canceling the asynchronous task.</param>
    /// <returns>A task that represents the asynchronous compute derive partition operation.</returns>
    public async Task RunBatchProcessingTaskAsync<TBatchProcessingTask>(
        BatchProcessingTaskConfiguration configuration,
        IProgress<(string, double)> progress = null,
        CancellationToken cancellationToken = default)
        where TBatchProcessingTask : IBatchProcessingTask
    {
        await RunBatchProcessingTaskAsync(Activator.CreateInstance<TBatchProcessingTask>(), configuration, 0L, progress, cancellationToken);
    }

    /// <summary>
    /// Asynchronously runs a specified batch processing task on the dataset.
    /// </summary>
    /// <param name="batchProcessingTask">The batch processing task to run.</param>
    /// <param name="configuration">The batch processing task configuration.</param>
    /// <param name="progress">An optional progress object to be used for reporting progress.</param>
    /// <param name="cancellationToken">An optional token for canceling the asynchronous task.</param>
    /// <returns>A task that represents the asynchronous compute derive partition operation.</returns>
    public async Task RunBatchProcessingTaskAsync(
        IBatchProcessingTask batchProcessingTask,
        BatchProcessingTaskConfiguration configuration,
        IProgress<(string, double)> progress = null,
        CancellationToken cancellationToken = default)
    {
        await RunBatchProcessingTaskAsync(batchProcessingTask, configuration, 0L, progress, cancellationToken);
    }

    /// <summary>
    /// Asynchronously runs a batch processing task of a specified type on the dataset.
    /// </summary>
    /// <typeparam name="TBatchProcessingTask">The type of the batch processing task.</typeparam>
    /// <typeparam name="TParameter">The type of parameter passed to the batch processing task.</typeparam>
    /// <param name="configuration">The batch processing task configuration.</param>
    /// <param name="parameter">The parameter to be passed to the action.</param>
    /// <param name="progress">An optional progress object to be used for reporting progress.</param>
    /// <param name="cancellationToken">An optional token for canceling the asynchronous task.</param>
    /// <returns>A task that represents the asynchronous compute derive partition operation.</returns>
    public async Task RunBatchProcessingTaskAsync<TBatchProcessingTask, TParameter>(
        BatchProcessingTaskConfiguration configuration,
        TParameter parameter,
        IProgress<(string, double)> progress = null,
        CancellationToken cancellationToken = default)
        where TBatchProcessingTask : IBatchProcessingTask
    {
        await RunBatchProcessingTaskAsync(Activator.CreateInstance<TBatchProcessingTask>(), configuration, parameter, progress, cancellationToken);
    }

    /// <summary>
    /// Asynchronously runs a specified batch processing task on the dataset.
    /// </summary>
    /// <typeparam name="TParameter">The type of parameter passed to the batch processing task.</typeparam>
    /// <param name="batchProcessingTask">The batch processing task to run.</param>
    /// <param name="configuration">The batch processing task configuration.</param>
    /// <param name="parameter">The parameter to be passed to the action.</param>
    /// <param name="progress">An optional progress object to be used for reporting progress.</param>
    /// <param name="cancellationToken">An optional token for canceling the asynchronous task.</param>
    /// <returns>A task that represents the asynchronous compute derive partition operation.</returns>
    public async Task RunBatchProcessingTaskAsync<TParameter>(
        IBatchProcessingTask batchProcessingTask,
        BatchProcessingTaskConfiguration configuration,
        TParameter parameter,
        IProgress<(string, double)> progress = null,
        CancellationToken cancellationToken = default)
    {
        double totalDuration = default;
        List<double> sessionStart = Sessions.Select(s =>
        {
            double currentDuration = totalDuration;
            totalDuration += s.TimeInterval.Span.TotalSeconds;
            return currentDuration;
        }).ToList();
        List<double> sessionDuration = Sessions.Select(s => s.TimeInterval.Span.TotalSeconds).ToList();

        batchProcessingTask.OnStartProcessingDataset();

        try
        {
            for (int i = 0; i < Sessions.Count; i++)
            {
                Progress<(string, double)> sessionProgress = progress != null ? new Progress<(string, double)>(tuple => progress.Report((tuple.Item1, (sessionStart[i] + (tuple.Item2 * sessionDuration[i])) / totalDuration))) : null;
                await Sessions[i].RunBatchProcessingTaskAsync(batchProcessingTask, configuration, parameter, sessionProgress, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            batchProcessingTask.OnCanceledProcessingDataset();
            throw;
        }
        catch (Exception)
        {
            batchProcessingTask.OnExceptionProcessingDataset();
            throw;
        }

        batchProcessingTask.OnEndProcessingDataset();
    }

    /// <summary>
    /// Method called when structure of the dataset changed.
    /// </summary>
    public virtual void OnDatasetChanged()
    {
        if (AutoSave)
        {
            Save();
        }
        else
        {
            HasUnsavedChanges = true;
        }

        // raise the event.
        EventHandler handler = DatasetChanged;
        handler?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a session to this dataset and updates its originating time interval.
    /// </summary>
    /// <param name="session">The session to be added.</param>
    private void AddSession(Session session)
    {
        if (Sessions.Any(s => s.Name == session.Name))
        {
            // session names must be unique
            throw new InvalidOperationException($"Dataset already contains a session named {session.Name}");
        }

        InternalSessions.Add(session);
        OnDatasetChanged();
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        foreach (Session session in InternalSessions)
        {
            session.Dataset = this;
        }
    }
}
