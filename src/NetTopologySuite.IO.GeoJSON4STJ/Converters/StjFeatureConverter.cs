﻿using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;

namespace NetTopologySuite.IO.Converters
{
    /// <summary>
    /// Converts Feature object to its JSON representation.
    /// </summary>
    public class StjFeatureConverter : JsonConverter<IFeature>
    {
        private readonly Func<IFeature> _createFeatureFunction;
        private readonly Func<IAttributesTable> _createAttributesTableFunction;
        private readonly StjGeometryConverter _geometryConverter;
        private readonly StjAttributesTableConverter _attributeTableConverter;
        private readonly string _idPropertyName;
        
        /// <summary>
        /// Creates an instance of this class
        /// </summary>
        /// <param name="factory"></param>
        /// <param name="idPropertyName"></param>
        /// <param name="createFeatureFunction"></param>
        /// <param name="createAttributesTableFunction"></param>
        /// 
        public StjFeatureConverter(GeometryFactory factory = null, string idPropertyName = null,
            Func<IFeature> createFeatureFunction = null,
            Func<IAttributesTable> createAttributesTableFunction = null)
        {
            _geometryConverter = new StjGeometryConverter(factory);
            _idPropertyName = string.IsNullOrWhiteSpace(idPropertyName) ? "id" : idPropertyName;

            if (createFeatureFunction != null)
                _createFeatureFunction = createFeatureFunction;
            else
                _createFeatureFunction = () => new Feature();

            if (createAttributesTableFunction != null)
                _createAttributesTableFunction = createAttributesTableFunction;
            else
                _createAttributesTableFunction = () => new AttributesTable();
            
            _attributeTableConverter = new StjAttributesTableConverter();
        }



        /// <summary>
        /// Writes the JSON representation of the object.
        /// </summary>
        /// <param name="writer">The <see cref="T:Newtonsoft.Json.JsonWriter"/> to write to.</param>
        /// <param name="value">The value.</param>
        /// <param name="options">The calling serializer.</param>
        public override void Write(Utf8JsonWriter writer, IFeature value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();

            // type
            writer.WriteString("type", nameof(GeoJsonObjectType.Feature));

            // Add the id here if present in attributes.
            // It will be skipped in serialization of properties
            object id = null;
            if (value.Attributes?.TryGetId(out id) == true)
            {
                writer.WritePropertyName(_idPropertyName);
                writer.WriteString(_idPropertyName, JsonSerializer.Serialize(id, id.GetType(), options));
            }

            // bbox (optional)
            var bbox = value.BoundingBox ?? value.Geometry?.EnvelopeInternal;
            if (bbox != null || !options.IgnoreNullValues)
            {
                if (bbox == null)
                    bbox = value.Geometry?.EnvelopeInternal;

                writer.WritePropertyName("bbox");
                _geometryConverter.WriteBBox(writer, bbox, options);
            }

            // geometry
            if (value.Geometry != null || !options.IgnoreNullValues)
            {
                writer.WritePropertyName("geometry");
                _geometryConverter.Write(writer, value.Geometry, options);
            }

            // properties
            if (value.Attributes != null || ! options.IgnoreNullValues)
            {
                writer.WritePropertyName("properties");
                _attributeTableConverter.Write(writer, value.Attributes, options);
            }

            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads the JSON representation of the object.
        /// </summary>
        /// <param name="reader">The <see cref="T:Newtonsoft.Json.JsonReader"/> to read from.</param>
        /// <param name="objectType">Type of the object.</param>
        /// <param name="options">The calling serializer.</param>
        /// <returns>
        /// The object value.
        /// </returns>
        public override IFeature Read(ref Utf8JsonReader reader, Type objectType, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                reader.Read();
                return null;
            }

            reader.ReadToken(JsonTokenType.StartObject);
            reader.SkipComments();

            // Create a new feature
            var feature = _createFeatureFunction();
            while (reader.TokenType == JsonTokenType.PropertyName)
            {
                // Read the property name
                string propertyName = reader.GetString();

                // Advance to value
                reader.Read();

                switch (propertyName)
                {
                    case "type":
                        if (reader.GetString() != "Feature")
                            throw new ArgumentException("Expected value 'Feature' not found.");
                        reader.Read();
                        break;

                    case "id":
                        dynamic featureId;
                        if (reader.TokenType == JsonTokenType.Number)
                        {
                            if (reader.TryGetInt32(out int i4))
                                featureId = i4;
                            else if (reader.TryGetInt32(out int i8))
                                featureId = i8;
                            else
                                throw new JsonException();
                        }
                        else if (reader.TokenType == JsonTokenType.String)
                        {
                            if (reader.TryGetGuid(out var guid))
                                featureId = guid;
                            else
                                featureId = reader.GetString();
                        }
                        else
                        {
                            throw new JsonException();
                        }

                        if (feature.Attributes is null)
                            feature.Attributes = _createAttributesTableFunction();
                        else if (feature.Attributes.Exists("id"))
                            feature.Attributes["id"] = featureId;
                        else
                            feature.Attributes.Add("id", (object)featureId);

                        reader.Read();
                        break;

                    case "bbox":
                        var bbox = _geometryConverter.ReadBBox(ref reader, options);
                        feature.BoundingBox = bbox;
                        //Debug.WriteLine("BBOX: {0}", bbox.ToString());
                        break;

                    case "geometry":
                        var geometry = _geometryConverter.Read(ref reader, typeof(Geometry), options);
                        feature.Geometry = geometry;
                        break;

                    case "properties":
                        var attributeTable = _attributeTableConverter.Read(ref reader, typeof(IAttributesTable), feature, options);
                        if (feature.Attributes == null)
                            feature.Attributes = attributeTable;
                        break;

                    default:
                        // Advance
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName || reader.TokenType == JsonTokenType.EndObject)
                                break;
                        }
                        break;
                }

                reader.SkipComments();
            }

            reader.ReadToken(JsonTokenType.EndObject);
            return feature;
        }

        /// <summary>
        /// Determines whether this instance can convert the specified object type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns>
        ///   <c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.
        /// </returns>
        public override bool CanConvert(Type objectType)
        {
            return typeof(IFeature).IsAssignableFrom(objectType);
        }
    }
}
