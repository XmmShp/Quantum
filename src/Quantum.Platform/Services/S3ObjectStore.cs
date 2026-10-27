using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;

namespace Quantum.Platform;

public sealed class S3StorageOptions
{
    public string ServiceUrl { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string BucketName { get; set; } = "root";
    public string KeyPrefix { get; set; } = "quantum/";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public bool ForcePathStyle { get; set; } = true;
}

public sealed record StoredObject(byte[] Bytes, DateTimeOffset LastModified);

public interface IQuantumObjectStore
{
    Task PutAsync(string key, byte[] bytes, bool createOnly, CancellationToken cancellationToken);
    Task<StoredObject> GetAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed class S3ObjectStore : IQuantumObjectStore, IDisposable
{
    private readonly AmazonS3Client client;
    private readonly S3StorageOptions options;

    public S3ObjectStore(IOptions<S3StorageOptions> configured)
    {
        options = configured.Value;
        if (!Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)
            || string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey)
            || string.IsNullOrWhiteSpace(options.BucketName) || string.IsNullOrWhiteSpace(options.Region)
            || !options.KeyPrefix.EndsWith('/') || options.KeyPrefix.StartsWith('/')
            || options.KeyPrefix.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("An HTTPS S3 endpoint, credentials, bucket, region and isolated key prefix are required.");
        }
        client = new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            AuthenticationRegion = options.Region,
            ForcePathStyle = options.ForcePathStyle,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromMinutes(3)
        });
    }

    private string Key(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':')
            || relative.StartsWith('/') || relative.Split('/').Any(s => s is "" or "." or ".."))
        {
            throw new InvalidDataException("Object keys must stay inside the configured prefix.");
        }
        return options.KeyPrefix + relative;
    }

    public async Task PutAsync(string key, byte[] bytes, bool createOnly, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = options.BucketName, Key = Key(key), InputStream = stream,
                ContentType = "application/octet-stream", UseChunkEncoding = false,
                IfNoneMatch = createOnly ? "*" : null,
                AutoCloseStream = false
            };
            request.Headers.ContentMD5 = Convert.ToBase64String(MD5.HashData(bytes));
            request.Headers.ContentLength = bytes.LongLength;
            await client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new IOException("The object already exists.", exception);
        }
    }

    public async Task<StoredObject> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetObjectAsync(options.BucketName, Key(key), cancellationToken);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return new StoredObject(buffer.ToArray(), response.LastModified ?? DateTime.UtcNow);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("The requested object does not exist.", exception);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
        => client.DeleteObjectAsync(options.BucketName, Key(key), cancellationToken);

    public async Task DeleteExpiredMobileObjectsAsync(CancellationToken cancellationToken)
    {
        string? continuation = null;
        var cutoff = DateTime.UtcNow.AddHours(-24);
        do
        {
            var page = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = options.BucketName,
                Prefix = options.KeyPrefix + "mobile-attachments/",
                ContinuationToken = continuation
            }, cancellationToken);
            foreach (var item in page.S3Objects ?? [])
            {
                if (item.LastModified < cutoff)
                    await client.DeleteObjectAsync(options.BucketName, item.Key, cancellationToken);
            }
            continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (!string.IsNullOrEmpty(continuation));
    }

    public void Dispose() => client.Dispose();
}
