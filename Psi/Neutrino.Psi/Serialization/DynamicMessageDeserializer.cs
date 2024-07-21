// <copyright file="DynamicMessageDeserializer.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Runtime.Serialization;
using Neutrino.Psi.Common;

namespace Neutrino.Psi.Serialization;

/// <summary>
/// Deserializer for any message type to dynamic.
/// </summary>
/// <remarks>Uses TypeSchema to construct message type as dynamic primitive and/or ExpandoObject of dynamic.</remarks>
internal sealed class DynamicMessageDeserializer
{
    private readonly string _rootTypeName;
    private readonly IDictionary<string, TypeSchema> _schemasByTypeName;
    private readonly IDictionary<int, TypeSchema> _schemasById;
    private readonly List<dynamic> _instanceCache = new();
    private readonly IDictionary<string, string> _typeNameSynonyms;
    private readonly XsdDataContractExporter _dataContractExporter = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicMessageDeserializer"/> class.
    /// </summary>
    /// <param name="typeName">Type name of message.</param>
    /// <param name="schemas">Collection of known TypeSchemas.</param>
    /// <param name="typeNameSynonyms">Type name synonyms.</param>
    public DynamicMessageDeserializer(string typeName, IDictionary<string, TypeSchema> schemas, IDictionary<string, string> typeNameSynonyms)
    {
        _rootTypeName = typeName;
        _schemasByTypeName = schemas;
        _schemasById = schemas.Values.ToDictionary(s => s.Id);
        _typeNameSynonyms = typeNameSynonyms;
    }

    /// <summary>
    /// Deserialize message bytes to dynamic.
    /// </summary>
    /// <param name="reader">BufferReader of message bytes.</param>
    /// <returns>dynamic (primitive or ExpandoObject).</returns>
    public dynamic Deserialize(BufferReader reader)
    {
        dynamic message = Read(_rootTypeName, reader);
        _instanceCache.Clear();
        return message;
    }

    private dynamic Read(string typeName, BufferReader reader, bool isCollectionElement = false)
    {
        // handle primitive types
        string simpleTypeName = typeName.Split(',')[0]; // without assembly qualification
        switch (simpleTypeName)
        {
            case "System.Boolean":
                return reader.ReadBool();
            case "System.Byte":
                return reader.ReadByte();
            case "System.Char":
                return reader.ReadChar();
            case "System.DateTime":
                return reader.ReadDateTime();
            case "System.Double":
                return reader.ReadDouble();
            case "System.Int16":
                return reader.ReadInt16();
            case "System.Int32":
                return reader.ReadInt32();
            case "System.Int64":
                return reader.ReadInt64();
            case "System.SByte":
                return reader.ReadSByte();
            case "System.Single":
                return reader.ReadSingle();
            case "System.UInt16":
                return reader.ReadUInt16();
            case "System.UInt32":
                return reader.ReadUInt32();
            case "System.UInt64":
                return reader.ReadUInt64();
        }

        // determine type info and schema
        bool isString = simpleTypeName == "System.String";
        if (!isString && !_schemasByTypeName.ContainsKey(typeName))
        {
            string ResolveTypeName()
            {
                if (_typeNameSynonyms.TryGetValue(typeName, out string synonym))
                {
                    return synonym;
                }
                else
                {
                    // try contract name (if type can be resolved)
                    Type typ = Type.GetType(typeName, false);
                    if (typ != null)
                    {
                        System.Xml.XmlQualifiedName contractName = _dataContractExporter.GetSchemaTypeName(typ);
                        if (contractName != null)
                        {
                            synonym = contractName.ToString();
                            _typeNameSynonyms.Add(typeName, synonym);
                            return synonym;
                        }
                    }

                    // try custom serializer
                    string prefix = typeName.Split('[', ',')[0];
                    string customTypeName = $"{prefix}+CustomSerializer{typeName.Substring(prefix.Length)}";
                    if (!_schemasByTypeName.ContainsKey(customTypeName))
                    {
                        throw new Exception($"Unknown schema type name ({typeName}).\nA synonym may be needed (see {nameof(KnownSerializers)}.{nameof(KnownSerializers.RegisterDynamicTypeSchemaNameSynonym)}())");
                    }

                    return customTypeName;
                }
            }

            typeName = ResolveTypeName();
        }

        TypeSchema schema = isString ? null : _schemasByTypeName[typeName];
        bool isStruct = !isString && (schema.Flags & TypeFlags.IsStruct) != 0;
        bool isClass = !isString && (schema.Flags & TypeFlags.IsClass) != 0;
        bool isContract = !isString && (schema.Flags & TypeFlags.IsContract) != 0;
        bool isCollection = !isString && (schema.Flags & TypeFlags.IsCollection) != 0;

        // reference types and strings (except when members of a collection) have ref-prefix flags
        if (isClass || isCollection || isContract || (isString && !isCollectionElement))
        {
            uint prefix = reader.ReadUInt32();
            switch (prefix & SerializationHandler.RefPrefixMask)
            {
                case SerializationHandler.RefPrefixNull:
                    return null;
                case SerializationHandler.RefPrefixExisting:
                    // get existing instance from cache
                    return _instanceCache[(int)(prefix & SerializationHandler.RefPrefixValueMask)];
                case SerializationHandler.RefPrefixTyped:
                    // update schema to concrete derived type
                    schema = _schemasById[(int)(prefix & SerializationHandler.RefPrefixValueMask)];
                    break;
                case SerializationHandler.RefPrefixNew:
                    // fall through to deserialize below
                    break;
                default:
                    throw new ArgumentException($"Unexpected ref prefix: {prefix}");
            }
        }

        if (isString)
        {
            string str = reader.ReadString();
            _instanceCache.Add(str);
            return str;
        }

        if (isCollection)
        {
            uint len = reader.ReadUInt32();
            string subType = schema.Members[0].Type; // single Elements member describes contained type
            dynamic[] elements = new dynamic[len];
            _instanceCache.Add(elements); // add before contents
            for (int i = 0; i < len; i++)
            {
                elements[i] = Read(subType, reader, true);
            }

            return elements;
        }

        IDictionary<string, dynamic> message = new ExpandoObject();
        if (!isStruct)
        {
            _instanceCache.Add(message); // add before members
        }

        if (schema.Members != null)
        {
            foreach (TypeMemberSchema mem in schema.Members)
            {
                string name = mem.Name;
                string type = mem.Type;
                message.Add(name, Read(type, reader));
            }
        }

        return message;
    }
}
