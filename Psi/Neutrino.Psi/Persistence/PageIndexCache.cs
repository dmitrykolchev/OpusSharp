// <copyright file="PageIndexCache.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Threading;
using Neutrino.Psi.Data;

namespace Neutrino.Psi.Persistence;

internal class PageIndexCache : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly string _name;
    private IndexEntry[] _pageIndex = new IndexEntry[0];
    private InfiniteFileReader _indexReader;

    public PageIndexCache(string name, string path)
    {
        _name = name;
        _indexReader = new InfiniteFileReader(path, PsiStoreCommon.GetIndexFileName(name));
    }

    public void Dispose()
    {
        if (_indexReader != null)
        {
            _indexReader.Dispose();
            _indexReader = null;
        }
    }

    public IndexEntry Search(DateTime time, bool useOriginatingTime)
    {
        // make a local copy to avoid a lock. The list is immutable, but the member variable can change (can point to a new list after an update).
        IndexEntry[] indexList = _pageIndex;

        // if this is a live index, make sure all the data published so far is loaded
        if (indexList.Length == 0 || CompareTime(time, indexList[indexList.Length - 1], useOriginatingTime) > 0)
        {
            Update();
            indexList = _pageIndex;
        }

        if (indexList.Length == 0)
        {
            return default;
        }

        int startIndex = 0;
        int endIndex = indexList.Length;
        if (CompareTime(time, indexList[0], useOriginatingTime) <= 0)
        {
            return indexList[0];
        }

        int midIndex = 0;
        while (startIndex < endIndex - 1)
        {
            midIndex = startIndex + ((endIndex - startIndex) / 2);
            int compResult = CompareTime(time, indexList[midIndex], useOriginatingTime);
            if (compResult > 0)
            {
                startIndex = midIndex;
            }
            else
            {
                endIndex = midIndex;
            }
        }

        return indexList[startIndex];
    }

    private static int CompareTime(DateTime time, IndexEntry entry, bool useOriginatingTime)
    {
        return useOriginatingTime ? time.CompareTo(entry.OriginatingTime) : time.CompareTo(entry.CreationTime);
    }

    private void Update()
    {
        if (_indexReader == null || !Monitor.TryEnter(_syncRoot))
        {
            // someone else is updating the list already
            return;
        }

        if (_indexReader != null)
        {
            List<IndexEntry> newList = new();
            while (_indexReader.MoveNext())
            {
                IndexEntry indexEntry;
                unsafe
                {
                    _indexReader.Read((byte*)&indexEntry, sizeof(IndexEntry));
                }

                newList.Add(indexEntry);
            }

            if (!PsiStoreMonitor.IsStoreLive(_name, _indexReader.Path))
            {
                _indexReader.Dispose();
                _indexReader = null;
            }

            if (newList.Count > 0)
            {
                IndexEntry[] newIndex = new IndexEntry[_pageIndex.Length + newList.Count];
                Array.Copy(_pageIndex, newIndex, _pageIndex.Length);
                newList.CopyTo(newIndex, _pageIndex.Length);
                _pageIndex = newIndex;
            }
        }

        Monitor.Exit(_syncRoot);
    }
}
