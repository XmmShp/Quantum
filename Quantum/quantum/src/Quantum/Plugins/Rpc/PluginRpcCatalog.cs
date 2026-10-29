using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

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
        Type? responseType,
        PluginRpcSerializer serializer)
    {
        var serviceAttributes = ReadAttributes(serviceType.CustomAttributes);
        var methodAttributes = ReadAttributes(method.CustomAttributes);
        var parameterAttributes = ReadAttributes(parameter.CustomAttributes);
        var returnAttributes = ReadAttributes(method.ReturnParameter.CustomAttributes);
        var inputSchema = AddDescription(
            BuildSchema(parameter.ParameterType, serializer),
            parameterAttributes);
        var outputSchema = responseType is null
            ? JsonSerializer.SerializeToElement(new { type = "null" }, JsonOptions)
            : BuildSchema(responseType, serializer);
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

    private static JsonElement BuildSchema(Type type, PluginRpcSerializer serializer)
    {
        var schema = serializer.GetSchema(type, new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = EnrichSchema
        });
        return JsonSerializer.SerializeToElement(schema, JsonOptions);
    }

    private static JsonNode EnrichSchema(JsonSchemaExporterContext context, JsonNode node)
    {
        var schema = node as JsonObject ?? new JsonObject { ["allOf"] = new JsonArray(node.DeepClone()) };
        var type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;
        schema["dotnetType"] = GetTypeName(type);
        AddDocumentation(schema, ReadAttributes(type.CustomAttributes));
        if (context.PropertyInfo?.AttributeProvider is MemberInfo member)
        {
            AddDocumentation(schema, ReadAttributes(member.CustomAttributes));
        }

        if (type.IsEnum)
        {
            var stringValues = schema["enum"] is JsonArray values
                && values.All(value => value is null || value is JsonValue scalar && scalar.TryGetValue<string>(out _));
            var stringConstant = schema["const"] is JsonValue constant && constant.TryGetValue<string>(out _);
            if (schema["type"] is null && (stringValues || stringConstant))
            {
                schema["type"] = schema["enum"] is JsonArray enumValues && enumValues.Any(value => value is null)
                    ? new JsonArray("string", "null")
                    : JsonValue.Create("string");
            }

            // Keep old CLR value descriptions while making the actual wire format explicit.
            if (stringValues || stringConstant || schema["type"] is JsonValue kind && kind.TryGetValue<string>(out var name) && name == "string")
            {
                var description = schema["description"]?.GetValue<string>();
                schema["description"] = (description is null ? "" : description + " ")
                    + "JSON enum values must be strings; numeric values are not accepted.";
            }
        }

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

}
