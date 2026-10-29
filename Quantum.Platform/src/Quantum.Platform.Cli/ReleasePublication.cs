using System.Text.Json;

internal static class ReleasePublication
{
    public static string Evaluate(JsonElement release)
    {
        var status = Name(release.GetProperty("status"), ["", "Pending", "Published", "Rejected"]);
        if (status == "Published") return status;
        if (status == "Rejected") throw new QuantumCli.CliException("The platform rejected this release. Inspect its review notes in the portal.");
        if (status != "Pending") throw new QuantumCli.CliException($"Unknown release status: {status}.");
        var review = Name(release.GetProperty("automatedReviewStatus"), ["", "Queued", "Running", "Approved", "Rejected", "ManualReview", "Failed"]);
        if (review is "Rejected" or "ManualReview" or "Failed")
            throw new QuantumCli.CliException($"Automated review ended with {review}. Inspect the release in the portal.");
        if (review is not ("Queued" or "Running" or "Approved"))
            throw new QuantumCli.CliException($"Unknown automated review status: {review}.");
        return status;
    }

    private static string Name(JsonElement value, string[] names)
        => value.ValueKind == JsonValueKind.String ? value.GetString()! :
            value.TryGetInt32(out var index) && index > 0 && index < names.Length ? names[index] : "Unknown";
}
