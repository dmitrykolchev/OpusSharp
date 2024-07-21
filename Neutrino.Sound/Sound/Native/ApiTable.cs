// <copyright file="ApiTable.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Neutrino.Sound.Native;

internal abstract class ApiTable
{
    private readonly nint _module;

    protected ApiTable(nint module)
    {
        _module = module;
        Initialize();
    }

    public nint Module => _module;

    protected virtual void Initialize()
    {
        IEnumerable<FieldInfo> importFields = GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.GetCustomAttribute<ImportAttribute>() != null);
        foreach (FieldInfo? importField in importFields)
        {
            ImportAttribute importAttribute = importField.GetCustomAttribute<ImportAttribute>()!;
            string importName = importAttribute.Name ?? importField.Name;
            importField.SetValue(this, GetExport(importName));
            Debug.Print($"Export '{importName}' bound.");
        }
    }

    private nint GetExport(string name)
    {
        return NativeLibrary.GetExport(Module, name);
    }
}
