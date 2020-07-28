// BarJson.cs: Bar deserialisation.
//
// Copyright 2020 Raising the Floor - International
//
// Licensed under the New BSD license. You may not use this file except in
// compliance with this License.
//
// You may obtain a copy of the License at
// https://github.com/GPII/universal/blob/master/LICENSE.txt

namespace Morphic.Bar.Config
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public static class BarJson
    {
        public static BarData FromJson(string json)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            BarData bar = JsonSerializer.Create(settings)
                .Deserialize<BarData>(new BarJsonTextReader(new StringReader(json), "win"))!;

            bar.BarTheme.Apply(Theme.DefaultBar());
            
            // Make the theme of each item inherit the default theme.
            bar.DefaultTheme.Apply(Theme.DefaultItem());
            foreach (BarItem item in bar.AllItems)
            {
                item.Theme.Inherit(bar.DefaultTheme);
            }

            return bar;
        }

        /// <summary>
        /// Customised JSON reader which handles platform specific fields. The platform for which a field is used,
        /// is identified by a '$id' suffix. A field with a platform identifier of the current platform will be
        /// used instead of one without. 
        ///
        /// For example:
        /// 
        /// "value": "default value",
        /// "value$win": "windows-specific value",
        /// "value$mac": "macOS-specific value
        /// 
        /// </summary>
        public class BarJsonTextReader : JsonTextReader
        {
            /// <summary>
            /// Field paths visited which have the platform identifier.
            /// </summary>
            private readonly HashSet<string> overridden = new HashSet<string>();

            public BarJsonTextReader(TextReader reader) : base(reader)
            {
            }

            public BarJsonTextReader(TextReader reader, string platformId) : this(reader)
            {
                this.PlatformId = platformId;
            }

            public string PlatformId { get; } = "win";

            public override object? Value
            {
                get
                {
                    if (this.TokenType == JsonToken.PropertyName)
                    {
                        string name = base.Value?.ToString() ?? string.Empty;
                        string platformId = string.Empty;
                        string path = this.Path;

                        // Take the platform identifier from the name.
                        if (name.Contains('$'))
                        {
                            string[]? parts = name.Split("$", 2);
                            if (parts.Length == 2)
                            {
                                name = parts[0];
                                platformId = parts[1].ToLowerInvariant();
                                path = path.Substring(0, path.Length - platformId.Length - 1);
                            }
                        }

                        if (platformId == this.PlatformId)
                        {
                            // It's for this platform - use this field, and mark as over-ridden so it takes
                            // precedence over subsequent fields with no platform ID.
                            this.overridden.Add(path);
                        }
                        else if (platformId == string.Empty)
                        {
                            // No platform ID on this field name - use it only if there hasn't already been a
                            // field with a platform ID.
                            if (this.overridden.Contains(path))
                            {
                                // Rename it so it's not used.
                                name = "_overridden:" + base.Value;
                            }
                        }
                        else
                        {
                            // Not for this platform - ignore this field.
                            name = "_ignored:" + base.Value;
                        }

                        return name;
                    }
                    else
                    {
                        return base.Value;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Used by a class to specify, by name, the type of item it supports.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class JsonTypeNameAttribute : Attribute
    {
        public JsonTypeNameAttribute(string name)
        {
            this.Name = name;
        }

        public string Name { get; }
    }
    
    /// <summary>
    /// Provides support for a polymorphic json object, while also allowing properties to deserialise with values
    /// from a child object.
    ///
    /// The base class identifies the JSON field which specifies the type name via the 2nd parameter of the
    /// JsonConverter attribute.
    ///
    /// The subclass specifies the type name which is supports via the JsonTypeName attribute. 
    /// </summary>
    public class TypedJsonConverter : JsonConverter
    {
        private readonly string typeFieldName;

        public TypedJsonConverter(string typeFieldName)
        {
            this.typeFieldName = typeFieldName;
        }

        /// <summary>
        /// Creates an instance of the class inheriting <c>baseType</c> which has the JsonTypeName attribute
        /// with the specified name.
        /// </summary>
        /// <param name="baseType">The base type.</param>
        /// <param name="name">The name of the type.</param>
        /// <returns>A class which inherits baseType.</returns>
        private static object? CreateInstance(Type baseType, string? name)
        {
            Type? type = null;
            if (name != null)
            {
                // Find the class which has the JsonTypeName attribute with the given name.
                type = baseType.Assembly.GetTypes()
                    .Where(t => !t.IsAbstract && t.IsSubclassOf(baseType))
                    .FirstOrDefault(t => t.GetCustomAttribute<JsonTypeNameAttribute>()?.Name == name);
            }

            return type == null ? null : Activator.CreateInstance(type);
        }

        /// <summary>
        /// Gets the JSON field name for a given property, from either the JsonProperty attribute or the
        /// name of the property.
        /// </summary>
        /// <param name="property"></param>
        /// <returns></returns>
        private static string GetFieldName(MemberInfo property)
        {
            JsonPropertyAttribute attribute = property.GetCustomAttributes<JsonPropertyAttribute>(true)
                .FirstOrDefault();
            return attribute.PropertyName ?? property.Name;
        }

        /// <summary>
        /// Instantiates the correct subclass of objectType, as identified by the type field.
        /// 
        /// Also, makes the JsonProperty attribute allow a path into child objects.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="objectType"></param>
        /// <param name="existingValue"></param>
        /// <param name="serializer"></param>
        /// <returns></returns>
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
            JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);

            // Get the type of item.
            string? kindName = jo[this.typeFieldName]?.ToString();
            
            // Create the class for the type.
            object? target = CreateInstance(objectType, kindName);

            if (target != null)
            {
                // For each property, get the value using a path rather than just the field name.
                // (inspired by https://stackoverflow.com/a/33094930/67586)
                foreach (PropertyInfo property in target.GetType().GetProperties()
                    .Where(p => p.CanRead && p.CanWrite))
                {
                    // Get the value, using the path in the field name attribute.
                    string jsonPath = GetFieldName(property);
                    JToken? token = jo.SelectToken(jsonPath);

                    if (token != null && token.Type != JTokenType.Null)
                    {
                        // Set the property value.
                        object? value = token.ToObject(property.PropertyType, serializer);
                        property.SetValue(target, value, null);
                    }
                }
            }
            
            return target;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            // This isn't worth writing - the client only consumes JSON.
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            throw new NotImplementedException();
        }
    }

}
