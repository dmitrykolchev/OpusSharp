// <copyright file="MetadataCache.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Psi.Common;

namespace Microsoft.Psi.Persistence;

internal class MetadataCache : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly string _name;
    private readonly string _path;
    private readonly Action<IEnumerable<Metadata>, RuntimeInfo> _entriesAdded;
    private volatile Dictionary<string, PsiStreamMetadata> _streamDescriptors = new();
    private volatile Dictionary<int, PsiStreamMetadata> _streamDescriptorsById = new();
    private InfiniteFileReader _catalogReader;
    private TimeInterval _messageCreationTimeInterval;
    private TimeInterval _messageOriginatingTimeInterval;
    private TimeInterval _streamTimeInterval;
    private RuntimeInfo _runtimeInfo;

    public MetadataCache(string name, string path, Action<IEnumerable<Metadata>, RuntimeInfo> entriesAdded)
    {
        _name = name;
        _path = path;
        _catalogReader = new InfiniteFileReader(path, PsiStoreCommon.GetCatalogFileName(name));
        _entriesAdded = entriesAdded;

        // assume v0 for backwards compat. Update will fix this up if the file is newer.
        _runtimeInfo = new RuntimeInfo(0);
        Update();
    }

    public RuntimeInfo RuntimeInfo => _runtimeInfo;

    public IEnumerable<PsiStreamMetadata> AvailableStreams
    {
        get
        {
            Update();
            return _streamDescriptors.Values;
        }
    }

    public TimeInterval MessageCreationTimeInterval
    {
        get
        {
            Update();
            return _messageCreationTimeInterval;
        }
    }

    public TimeInterval MessageOriginatingTimeInterval
    {
        get
        {
            Update();
            return _messageOriginatingTimeInterval;
        }
    }

    public TimeInterval StreamTimeInterval
    {
        get
        {
            Update();
            return _streamTimeInterval;
        }
    }

    public void Dispose()
    {
        if (_catalogReader != null)
        {
            lock (_syncRoot)
            {
                _catalogReader.Dispose();
                _catalogReader = null;
            }
        }
    }

    public bool TryGet(string name, out PsiStreamMetadata metadata)
    {
        if (!_streamDescriptors.ContainsKey(name) || !_streamDescriptors[name].IsClosed)
        {
            Update();
        }

        return _streamDescriptors.TryGetValue(name, out metadata);
    }

    public bool TryGet(int id, out PsiStreamMetadata metadata)
    {
        if (!_streamDescriptorsById.ContainsKey(id) || !_streamDescriptorsById[id].IsClosed)
        {
            Update();
        }

        return _streamDescriptorsById.TryGetValue(id, out metadata);
    }

    public void Update()
    {
        if (_catalogReader == null)
        {
            return;
        }

        // since the cache is possibly shared by several store readers,
        // we need to lock before making changes
        lock (_syncRoot)
        {
            if (_catalogReader == null || !_catalogReader.HasMoreData())
            {
                return;
            }

            byte[] buffer = new byte[1024]; // will resize as needed
            List<Metadata> newMetadata = new();
            Dictionary<string, PsiStreamMetadata> newStreamDescriptors = new(_streamDescriptors);
            Dictionary<int, PsiStreamMetadata> newStreamDescriptorsById = new(_streamDescriptorsById);
            while (_catalogReader.MoveNext())
            {
                int count = _catalogReader.ReadBlock(ref buffer);
                BufferReader br = new(buffer, count);
                Metadata meta = Metadata.Deserialize(br);
                if (meta.Kind == MetadataKind.RuntimeInfo)
                {
                    // we expect this to be first in the file (or completely missing in v0 files)
                    _runtimeInfo = meta as RuntimeInfo;

                    // Need to review this. The issue was that the RemoteExporter is not writing
                    // out the RuntimeInfo to the stream. This causes the RemoteImporter side of things to
                    // never see a RuntimeInfo metadata object and thus it assumes that the stream is using
                    // version 0.0 of serialization (i.e. non-data-contract version) which causes a mismatch
                    // in the serialization resulting in throw from TypeSchema.ValidateCompatibleWith. This
                    // change fixes the issue.
                    newMetadata.Add(meta);
                }
                else
                {
                    newMetadata.Add(meta);
                    if (meta.Kind == MetadataKind.StreamMetadata)
                    {
                        PsiStreamMetadata sm = meta as PsiStreamMetadata;
                        sm.StoreName = _name;
                        sm.StorePath = _path;

                        // the same meta entry will appear multiple times (written on open and on close).
                        // The last one wins.
                        newStreamDescriptors[sm.Name] = sm;
                        newStreamDescriptorsById[sm.Id] = sm;
                    }
                }
            }

            // compute the time ranges
            _messageCreationTimeInterval = GetTimeRange(newStreamDescriptors.Values, meta => meta.MessageCreationTimeInterval);
            _messageOriginatingTimeInterval = GetTimeRange(newStreamDescriptors.Values, meta => meta.MessageOriginatingTimeInterval);
            _streamTimeInterval = GetTimeRange(newStreamDescriptors.Values, meta => meta.StreamTimeInterval);

            // clean up if the catalog is closed and we really reached the end
            if (!PsiStoreMonitor.IsStoreLive(_name, _path) && !_catalogReader.HasMoreData())
            {
                _catalogReader.Dispose();
                _catalogReader = null;
            }

            // swap the caches
            _streamDescriptors = newStreamDescriptors;
            _streamDescriptorsById = newStreamDescriptorsById;

            // let the registered delegates know about the change
            if (newMetadata.Count > 0 && _entriesAdded != null)
            {
                _entriesAdded(newMetadata, _runtimeInfo);
            }
        }
    }

    private static TimeInterval GetTimeRange(IEnumerable<PsiStreamMetadata> descriptors, Func<PsiStreamMetadata, TimeInterval> timeIntervalSelector)
    {
        DateTime left = DateTime.MaxValue;
        DateTime right = DateTime.MinValue;
        if (descriptors.Count() == 0)
        {
            return TimeInterval.Empty;
        }

        descriptors = descriptors.Where(d => d.MessageCount > 0);
        if (descriptors.Count() == 0)
        {
            return TimeInterval.Empty;
        }

        foreach (PsiStreamMetadata streamInfo in descriptors)
        {
            left = descriptors.Select(d => timeIntervalSelector(d).Left).Min();
            right = descriptors.Select(d => timeIntervalSelector(d).Right).Max();
        }

        if (left > right)
        {
            throw new Exception("The metadata appears to be invalid because the start time is greater than the end time: start = {left}, end = {right}");
        }

        return new TimeInterval(left, right);
    }
}
