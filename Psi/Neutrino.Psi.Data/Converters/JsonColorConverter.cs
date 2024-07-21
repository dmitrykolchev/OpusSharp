// <copyright file="JsonColorConverter.cs">
// Copyright (c) 2022-23 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

using System;
using System.Drawing;
using Newtonsoft.Json;

namespace Microsoft.Psi.Data.Converters;

/// <summary>
/// Represents a JSON color converter.
/// </summary>
public class JsonColorConverter : JsonConverter
{
    /// <inheritdoc/>
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(Color);
    }

    /// <inheritdoc/>
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        Color color = (Color)value;
        writer.WriteValue(color.ToArgb());
    }

    /// <inheritdoc/>
    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        return Color.FromArgb(Convert.ToInt32(reader.Value));
    }
}
