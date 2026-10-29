namespace Quantum.Platform.SkillDistribution;

internal static class QuantumPublishSkillDistributionEndpoints
{
    public static IEndpointRouteBuilder MapQuantumPublishSkill(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"/skills/{QuantumPublishSkillPackage.Name}").AllowAnonymous();
        group.MapGet("/manifest.json", (HttpContext context, QuantumPublishSkillPackageCache cache) =>
        {
            var package = cache.Value;
            context.Response.Headers.CacheControl = "no-cache";
            var entityTag = $"\"{package.ArchiveSha256}\"";
            context.Response.Headers.ETag = entityTag;
            if (IsNotModified(context.Request, entityTag)) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Ok(package.CreateManifest());
        });
        group.MapGet("/download", (HttpContext context, QuantumPublishSkillPackageCache cache) =>
        {
            var package = cache.Value;
            var requested = context.Request.Query["sha256"].ToString();
            if (!string.IsNullOrWhiteSpace(requested) && !requested.Equals(package.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound();
            }
            context.Response.Headers.CacheControl = string.IsNullOrWhiteSpace(requested) ? "no-cache" : "public, max-age=31536000, immutable";
            var entityTag = $"\"{package.ArchiveSha256}\"";
            context.Response.Headers.ETag = entityTag;
            if (IsNotModified(context.Request, entityTag)) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.File(package.Archive, "application/zip", $"{QuantumPublishSkillPackage.Name}-{package.ContentSha256[..12]}.zip");
        });
        return endpoints;
    }

    private static bool IsNotModified(HttpRequest request, string entityTag)
        => request.Headers.IfNoneMatch
            .SelectMany(value => (value ?? string.Empty).Split(','))
            .Select(static value => value.Trim())
            .Any(value => value == "*" || value == entityTag || value == $"W/{entityTag}");
}
