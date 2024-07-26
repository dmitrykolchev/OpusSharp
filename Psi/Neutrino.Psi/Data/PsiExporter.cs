// <copyright file="PsiExporter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using Neutrino.Psi.Executive;
using Neutrino.Psi.Serialization;


namespace Neutrino.Psi.Data;

/// <summary>
/// Component that writes messages to a multi-stream \psi store.
/// </summary>
/// <remarks>
/// The store can be backed by a file on disk, can be ephemeral (in-memory) for inter-process communication
/// or can be a network protocol for cross-machine communication.
/// Instances of this component can be created using <see cref="PsiStore.Create(Pipeline, string, string, bool, Serialization.KnownSerializers)"/>.
/// </remarks>
public sealed class PsiExporter : Exporter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PsiExporter"/> class.
    /// </summary>
    /// <param name="pipeline">The pipeline to add the component to.</param>
    /// <param name="name">The name of the application that generated the persisted files, or the root name of the files.</param>
    /// <param name="path">The directory in which the main persisted file resides or will reside, or null to create a volatile data store.</param>
    /// <param name="createSubdirectory">If true, a numbered sub-directory is created for this store.</param>
    /// <param name="serializers">
    /// A collection of known serializers, or null to infer it from the data being written to the store.
    /// The known serializer set can be accessed and modified afterwards via the <see cref="Exporter.Serializers"/> property.
    /// </param>
    internal PsiExporter(Pipeline pipeline, string name, string path, bool createSubdirectory = true, KnownSerializers serializers = null)
        : base(pipeline, name, path, createSubdirectory, serializers)
    {
    }
}
