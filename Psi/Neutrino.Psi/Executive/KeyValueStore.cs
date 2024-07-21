// <copyright file="KeyValueStore.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Collections.Concurrent;

namespace Microsoft.Psi.Executive;

/// <summary>
/// Global store for key/value pairs that can be shared between components via the ApplicationCatalog.
/// Adding a value with an existing name overrides the previous value.
/// </summary>
public class KeyValueStore
{
    public static readonly string GlobalNamespace = "global";
    private readonly ConcurrentDictionary<string, object> _globalNamespace = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object>> _namespaces = new();

    public KeyValueStore()
    {
        _namespaces[GlobalNamespace] = _globalNamespace;
    }

    public T Get<T>(string namespaceName, string name)
    {
        return (T)_namespaces[namespaceName][name];
    }

    public void Set<T>(string namespaceName, string name, T value)
    {
        if (!_namespaces.ContainsKey(namespaceName))
        {
            _namespaces[namespaceName] = new ConcurrentDictionary<string, object>();
        }

        _namespaces[namespaceName][name] = value;
    }

    public bool TryGet<T>(string namespaceName, string name, out T value)
    {
        if (!_namespaces.ContainsKey(namespaceName) || !_namespaces[namespaceName].ContainsKey(name))
        {
            value = default;
            return false;
        }

        value = (T)_namespaces[namespaceName][name];
        return true;
    }

    public bool Contains(string namespaceName, string name)
    {
        return _namespaces.ContainsKey(namespaceName) && _namespaces[namespaceName].ContainsKey(name);
    }
}
