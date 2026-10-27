using NOF.Domain;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Codex;

internal static class CodexAttachmentStore
{
    internal const int MaximumFileBytes = 8 * 1024 * 1024;

    public static async Task<CodexAttachmentFile[]> StageAsync(MobileAttachment[] files, CancellationToken cancellationToken)
    {
        if (files is null || files.Length > 4)
        {
            throw new DomainException("codex_attachment_invalid", "Select at most four attachments.");
        }
        var decoded = new List<(MobileAttachment File, byte[] Bytes)>();
        long total = 0;
        foreach (var file in files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 128 ||
                file.Name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) || file.Name is "." or ".." || file.Name.EndsWith('.') || file.Name.EndsWith(' ') || string.IsNullOrWhiteSpace(file.ContentType) ||
                file.ContentType.Length > 128 || file.ContentType.Any(c => char.IsControl(c)) || file.DataBase64 is null ||
                file.DataBase64.Length > ((MaximumFileBytes + 2) / 3) * 4)
            {
                throw new DomainException("codex_attachment_invalid", "Invalid attachment metadata or size.");
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(file.DataBase64);
            }
            catch (FormatException)
            {
                throw new DomainException("codex_attachment_invalid", "Invalid attachment encoding.");
            }
            total += bytes.Length;
            if (bytes.Length == 0 || bytes.Length > MaximumFileBytes || total > 16 * 1024 * 1024)
            {
                throw new DomainException("codex_attachment_invalid", "Attachments exceed the size limit.");
            }
            if (file.ContentType.StartsWith("image/", StringComparison.Ordinal) && file.ContentType is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp"))
            {
                throw new DomainException("codex_attachment_invalid", "Use PNG, JPEG, GIF or WebP images.");
            }
            decoded.Add((file, bytes));
        }
        if (decoded.Count == 0)
        {
            return [];
        }

        var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantum", "CodexAttachments");
        Directory.CreateDirectory(root);
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (Directory.GetLastWriteTimeUtc(directory) >= DateTime.UtcNow.AddDays(-7) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory))
            {
                File.Delete(path);
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        var result = new List<CodexAttachmentFile>();
        foreach (var (file, bytes) in decoded)
        {
            var directory = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, file.Name);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            result.Add(new()
            {
                Name = file.Name,
                ContentType = file.ContentType,
                Path = path,
                Size = bytes.Length
            });
        }
        return result.ToArray();
    }
}
