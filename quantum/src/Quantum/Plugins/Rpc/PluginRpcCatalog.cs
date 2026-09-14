using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Quantum.Plugins;

internal sealed record PluginRpcCatalogDocument(
    int SchemaVersion,
    string CatalogRpcName,
    IReadOnlyList<PluginRpcCatalogService> Services);

internal sealed record PluginRpcCatalogService(
    string PluginId,
    string ServiceName,
    string ServiceType,
    string? Description,
    IReadOnlyList<PluginRpcAttributeMetadata> Attributes,
    IReadOnlyList<PluginRpcCatalogMethod> Methods);

internal sealed record PluginRpcCatalogMethod(
    string QualifiedName,
    string CanonicalName,
    IReadOnlyList<string> Aliases,
    string Declaration,
    string MethodName,
    string? Description,
    string RequestType,
    string? ResponseType,
    bool ReturnsValue,
    JsonElement InputSchema,
    JsonElement OutputSchema,
    IReadOnlyList<PluginRpcAttributeMetadata> Attributes,
    IReadOnlyList<PluginRpcAttributeMetadata> ParameterAttributes,
    IReadOnlyList<PluginRpcAttributeMetadata> ReturnAttributes);

internal sealed record PluginRpcAttributeMetadata(
    string Type,
    IReadOnlyList<PluginRpcAttributeArgumentMetadata> ConstructorArguments,
    IReadOnlyDictionary<string, PluginRpcAttributeArgumentMetadata> NamedArguments);

internal sealed record PluginRpcAttributeArgumentMetadata(string Type, JsonElement Value);

internal sealed record PluginRpcMethodCatalogMetadata(
    string ServiceName,
    string ServiceType,
    string? ServiceDescription,
    IReadOnlyList<PluginRpcAttributeMetadata> ServiceAttributes,
    PluginRpcCatalogMethod Method);

internal static class PluginRpcCatalogMetadataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PluginRpcMethodCatalogMetadata Describe(
        Type serviceType,
        MethodInfo method,
        ParameterInfo parameter,
        Type? responseType)
    {
        var serviceAttributes = ReadAttributes(serviceType.CustomAttributes);
        var methodAttributes = ReadAttributes(method.CustomAttributes);
        var parameterAttributes = ReadAttributes(parameter.CustomAttributes);
        var returnAttributes = ReadAttributes(method.ReturnParameter.CustomAttributes);
        var inputSchema = AddDescription(
            BuildSchema(parameter.ParameterType, TryGetNullability(parameter)),
            parameterAttributes);
        var outputSchema = responseType is null
            ? JsonSerializer.SerializeToElement(new { type = "null" }, JsonOptions)
            : BuildSchema(responseType, TryGetNullability(method.ReturnParameter));
        if (responseType is not null)
        {
            outputSchema = AddDescription(outputSchema, returnAttributes);
        }

        return new PluginRpcMethodCatalogMetadata(
            GetInvocationName(serviceType),
            GetTypeName(serviceType),
            ReadDescription(serviceAttributes),
            serviceAttributes,
            new PluginRpcCatalogMethod(
                QualifiedName: string.Empty,
                CanonicalName: string.Empty,
                Aliases: [],
                Declaration: $"{serviceType.FullName}.{method.Name}",
                MethodName: method.Name,
                Description: ReadDescription(methodAttributes),
                RequestType: GetTypeName(parameter.ParameterType),
                ResponseType: responseType is null ? null : GetTypeName(responseType),
                ReturnsValue: responseType is not null,
                InputSchema: inputSchema,
                OutputSchema: outputSchema,
                Attributes: methodAttributes,
                ParameterAttributes: parameterAttributes,
                ReturnAttributes: returnAttributes));
    }

    private static JsonElement BuildSchema(Type type, NullabilityInfo? nullability)
    {
        var schema = BuildSchemaNode(type, nullability, []);
        return JsonSerializer.SerializeToElement(schema, JsonOptions);
    }

    private static JsonObject BuildSchemaNode(
        Type type,
        NullabilityInfo? nullability,
        HashSet<Type> path)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        var effectiveType = nullableType ?? type;
        var schema = new JsonObject
        {
            ["dotnetType"] = GetTypeName(effectiveType)
        };
        var attributes = ReadAttributes(effectiveType.CustomAttributes);
        AddDocumentation(schema, attributes);
        var isNullable = nullableType is not null
            || (!effectiveType.IsValueType && nullability?.ReadState != NullabilityState.NotNull);
        if (isNullable)
        {
            schema["nullable"] = true;
        }

        if (effectiveType == typeof(string) || effectiveType == typeof(char))
        {
            schema["type"] = "string";
            return schema;
        }

        if (effectiveType == typeof(DateOnly))
        {
            schema["type"] = "string";
            schema["format"] = "date";
            return schema;
        }

        if (effectiveType == typeof(TimeOnly))
        {
            schema["type"] = "string";
            schema["format"] = "time";
            return schema;
        }

        if (effectiveType == typeof(DateTime) || effectiveType == typeof(DateTimeOffset))
        {
            schema["type"] = "string";
            schema["format"] = "date-time";
            return schema;
        }

        if (effectiveType == typeof(Guid))
        {
            schema["type"] = "string";
            schema["format"] = "uuid";
            return schema;
        }

        if (effectiveType == typeof(bool))
        {
            schema["type"] = "boolean";
            return schema;
        }

        if (effectiveType.IsEnum)
        {
            schema["type"] = "string";
            schema["enum"] = new JsonArray(Enum.GetNames(effectiveType)
                .Select(static name => JsonValue.Create(name))
                .ToArray());
            return schema;
        }

        if (IsInteger(effectiveType))
        {
            schema["type"] = "integer";
            return schema;
        }

        if (IsNumber(effectiveType))
        {
            schema["type"] = "number";
            return schema;
        }

        if (TryGetElementType(effectiveType) is { } elementType)
        {
            schema["type"] = "array";
            schema["items"] = BuildSchemaNode(elementType, null, path);
            return schema;
        }

        schema["type"] = "object";
        if (!path.Add(effectiveType))
        {
            schema["recursive"] = true;
            return schema;
        }

        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var property in effectiveType
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(static property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
                     .OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            var propertyNullability = TryGetNullability(property);
            var propertySchema = BuildSchemaNode(property.PropertyType, propertyNullability, path);
            var propertyAttributes = ReadAttributes(property.CustomAttributes);
            AddDocumentation(propertySchema, propertyAttributes);
            var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            properties[jsonName] = propertySchema;
            if (Nullable.GetUnderlyingType(property.PropertyType) is null
                && (property.PropertyType.IsValueType
                    || propertyNullability?.ReadState == NullabilityState.NotNull))
            {
                required.Add(jsonName);
            }
        }

        path.Remove(effectiveType);
        schema["properties"] = properties;
        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        schema["additionalProperties"] = false;
        return schema;
    }

    private static void AddDocumentation(
        JsonObject schema,
        IReadOnlyList<PluginRpcAttributeMetadata> attributes)
    {
        if (ReadDescription(attributes) is { } description)
        {
            schema["description"] = description;
        }

        if (attributes.Count > 0)
        {
            schema["x-dotnetAttributes"] = JsonSerializer.SerializeToNode(attributes, JsonOptions);
        }
    }

    private static JsonElement AddDescription(
        JsonElement schema,
        IReadOnlyList<PluginRpcAttributeMetadata> attributes)
    {
        if (ReadDescription(attributes) is not { } description)
        {
            return schema;
        }

        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        node["description"] = description;
        return JsonSerializer.SerializeToElement(node, JsonOptions);
    }

    private static IReadOnlyList<PluginRpcAttributeMetadata> ReadAttributes(
        IEnumerable<CustomAttributeData> attributes)
        => attributes
            .OrderBy(static attribute => attribute.AttributeType.FullName, StringComparer.Ordinal)
            .Select(static attribute => new PluginRpcAttributeMetadata(
                GetTypeName(attribute.AttributeType),
                attribute.ConstructorArguments
                    .Select(ToArgument)
                    .ToArray(),
                attribute.NamedArguments
                    .OrderBy(static argument => argument.MemberName, StringComparer.Ordinal)
                    .ToDictionary(
                        static argument => argument.MemberName,
                        static argument => ToArgument(argument.TypedValue),
                        StringComparer.Ordinal)))
            .ToArray();

    private static PluginRpcAttributeArgumentMetadata ToArgument(
        CustomAttributeTypedArgument argument)
        => new(
            GetTypeName(argument.ArgumentType),
            JsonSerializer.SerializeToElement(NormalizeValue(argument), JsonOptions));

    private static object? NormalizeValue(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> items)
        {
            return items.Select(NormalizeValue).ToArray();
        }

        if (argument.ArgumentType == typeof(Type) && argument.Value is Type type)
        {
            return GetTypeName(type);
        }

        if (argument.ArgumentType.IsEnum && argument.Value is not null)
        {
            return Enum.GetName(argument.ArgumentType, argument.Value)
                ?? Convert.ToString(argument.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return argument.Value;
    }

    private static string? ReadDescription(
        IReadOnlyList<PluginRpcAttributeMetadata> attributes)
        => attributes
            .FirstOrDefault(static attribute =>
                string.Equals(attribute.Type, typeof(DescriptionAttribute).FullName, StringComparison.Ordinal))
            ?.ConstructorArguments
            .FirstOrDefault()
            ?.Value.GetString();

    private static string GetInvocationName(Type serviceType)
        => serviceType.GetCustomAttribute<RpcInvocationNameAttribute>()?.Name
            ?? (serviceType.Name.Length > 1
                && serviceType.Name[0] == 'I'
                && char.IsUpper(serviceType.Name[1])
                    ? serviceType.Name[1..]
                    : serviceType.Name);

    private static string GetTypeName(Type type)
        => type.FullName ?? type.Name;

    private static NullabilityInfo? TryGetNullability(PropertyInfo property)
    {
        try
        {
            return new NullabilityInfoContext().Create(property);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static NullabilityInfo? TryGetNullability(ParameterInfo parameter)
    {
        try
        {
            return new NullabilityInfoContext().Create(parameter);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Type? TryGetElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type == typeof(string))
        {
            return null;
        }

        return type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(static candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static bool IsInteger(Type type)
        => type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong);

    private static bool IsNumber(Type type)
        => type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal);
}
