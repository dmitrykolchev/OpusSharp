// <copyright file="AnnotationSchema.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neutrino.Psi.Common.Intervals;
using Neutrino.Psi.Data.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Neutrino.Psi.Data.Annotations;

/// <summary>
/// Represents an annotation schema.
/// </summary>
public class AnnotationSchema
{
    private static readonly JsonSerializerSettings JsonSerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        TypeNameHandling = TypeNameHandling.Auto,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        Converters = { new StringEnumConverter() },
        SerializationBinder = new SafeSerializationBinder(),
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="AnnotationSchema"/> class.
    /// </summary>
    /// <param name="name">The name of the annotation schema.</param>
    /// <param name="attributeSchemas">An optional list of attribute schemas.</param>
    public AnnotationSchema(string name, params AnnotationAttributeSchema[] attributeSchemas)
    {
        Name = name;
        AttributeSchemas = attributeSchemas.ToList();
    }

    /// <summary>
    /// Gets the name of the annotation schema.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the collection of attribute schemas.
    /// </summary>
    public List<AnnotationAttributeSchema> AttributeSchemas { get; private set; }

    /// <summary>
    /// Loads an annotation schema from disk.
    /// </summary>
    /// <param name="fileName">The full path and filename of the annotation schema to load.</param>
    /// <returns>The requested annotation schema.</returns>
    public static AnnotationSchema LoadFrom(string fileName)
    {
        using System.IO.StreamReader streamReader = new(fileName);
        return LoadFrom(streamReader);
    }

    /// <summary>
    /// Tries to load an annotation schema from disk.
    /// </summary>
    /// <param name="fileName">The full path and filename of the annotation schema to load.</param>
    /// <param name="annotationSchema">The loaded annotation schema if successful.</param>
    /// <returns>True if the annotation schema is loaded successfully, otherwise null.</returns>
    public static bool TryLoadFrom(string fileName, out AnnotationSchema annotationSchema)
    {
        annotationSchema = null;
        if (!File.Exists(fileName))
        {
            return false;
        }

        try
        {
            annotationSchema = LoadFrom(fileName);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Loads an annotation schema from disk.
    /// </summary>
    /// <param name="fileName">The full path and filename of the annotation schema to load.</param>
    /// <returns>The requested annotation schema if it exists, otherwise null.</returns>
    public static AnnotationSchema LoadOrDefault(string fileName)
    {
        if (!File.Exists(fileName))
        {
            return null;
        }

        try
        {
            using System.IO.StreamReader streamReader = new(fileName);
            JsonReader reader = new JsonTextReader(streamReader);
            JsonSerializer serializer = JsonSerializer.Create(JsonSerializerSettings);
            AnnotationSchema annotationSchema = serializer.Deserialize<AnnotationSchema>(reader);

            // Perform simple deserialization checks
            if (string.IsNullOrEmpty(annotationSchema.Name))
            {
                throw new Exception("Deserialized annotation schema has empty name.");
            }
            else if (annotationSchema.AttributeSchemas.Count == 0)
            {
                throw new Exception("Deserialized annotation schema has no attributes.");
            }
            else if (annotationSchema.AttributeSchemas.Any(s => string.IsNullOrEmpty(s.Name)))
            {
                throw new Exception("Deserialized annotation schema which contains attributes with no names specified.");
            }

            return annotationSchema;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this annotation schema contains a specified attribute.
    /// </summary>
    /// <param name="attributeName">The name of the attribute.</param>
    /// <returns>True if the annotation schema contains the specified attribute, otherwise false.</returns>
    public bool ContainsAttribute(string attributeName)
    {
        return AttributeSchemas.Any(ad => ad.Name == attributeName);
    }

    /// <summary>
    /// Gets the schema for a specified attribute.
    /// </summary>
    /// <param name="attributeName">The name of the attribute.</param>
    /// <returns>The schema for a specified attribute if the attribute exists, otherwise null.</returns>
    public AnnotationAttributeSchema GetAttributeSchema(string attributeName)
    {
        return AttributeSchemas.FirstOrDefault(ad => ad.Name == attributeName);
    }

    /// <summary>
    /// Adds a new attribute to this annotation schema.
    /// </summary>
    /// <param name="attributeSchema">The attribute schema to add.</param>
    public void AddAttributeSchema(AnnotationAttributeSchema attributeSchema)
    {
        if (ContainsAttribute(attributeSchema.Name))
        {
            throw new ApplicationException(string.Format("AnnotationSchema {0} already contains an attribute named {1}.", Name, attributeSchema.Name));
        }

        AttributeSchemas.Add(attributeSchema);
    }

    /// <summary>
    /// Creates a new time interval annotation instance on a specified track, based on this annotation schema.
    /// </summary>
    /// <param name="timeInterval">The time interval.</param>
    /// <param name="track">The track.</param>
    /// <returns>A new time interval annotation.</returns>
    public TimeIntervalAnnotation CreateDefaultTimeIntervalAnnotation(TimeInterval timeInterval, string track)
    {
        // Create the collection of initial values for the annotation based on the default values
        Dictionary<string, IAnnotationValue> values = new();
        foreach (AnnotationAttributeSchema attributeSchema in AttributeSchemas)
        {
            values[attributeSchema.Name] = attributeSchema.ValueSchema.GetDefaultAnnotationValue();
        }

        return new TimeIntervalAnnotation(timeInterval, track, values);
    }

    /// <summary>
    /// Saves this annotation schema to a specified file.
    /// </summary>
    /// <param name="fileName">The full path and filename to save this annotation schema to.</param>
    public void Save(string fileName)
    {
        StreamWriter jsonFile = null;
        try
        {
            jsonFile = File.CreateText(fileName);
            using JsonTextWriter jsonWriter = new(jsonFile);
            JsonSerializer.Create(JsonSerializerSettings).Serialize(jsonWriter, this);
        }
        finally
        {
            jsonFile?.Dispose();
        }
    }

    private static AnnotationSchema LoadFrom(System.IO.StreamReader streamReader)
    {
        JsonTextReader reader = new(streamReader);
        JsonSerializer serializer = JsonSerializer.Create(JsonSerializerSettings);
        AnnotationSchema annotationSchema = serializer.Deserialize<AnnotationSchema>(reader);

        // Perform simple deserialization checks
        if (string.IsNullOrEmpty(annotationSchema.Name))
        {
            throw new Exception("Deserialized annotation schema has empty name.");
        }
        else if (annotationSchema.AttributeSchemas.Count == 0)
        {
            throw new Exception("Deserialized annotation schema has no attributes.");
        }

        return annotationSchema;
    }
}
