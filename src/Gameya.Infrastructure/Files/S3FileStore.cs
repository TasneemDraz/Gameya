using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Gameya.Application.Abstractions;

namespace Gameya.Infrastructure.Files;

/// <summary>
/// Stores files as objects named <c>{container}/{key}</c> in one bucket. Reads still go through the API's
/// signed URLs, so the bucket stays private.
/// </summary>
public sealed class S3FileStore(IAmazonS3 s3, string bucket, SignedFileUrls urls) : IFileStore, IStorageHealthProbe
{
    public async Task UploadAsync(
        string container, string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var objectKey = ObjectKey(container, key);

        // The SDK needs a seekable stream with a known length to sign the payload.
        var body = content;
        MemoryStream? buffer = null;
        if (!content.CanSeek)
        {
            buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            body = buffer;
        }

        try
        {
            await s3.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = objectKey,
                    InputStream = body,
                    ContentType = contentType,
                    AutoCloseStream = false
                },
                cancellationToken);
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    public async Task DeleteAsync(string container, string key, CancellationToken cancellationToken) =>
        await s3.DeleteObjectAsync(bucket, ObjectKey(container, key), cancellationToken);

    public async Task<Stream?> OpenReadAsync(string container, string key, CancellationToken cancellationToken)
    {
        var objectKey = ObjectKey(container, key);
        try
        {
            using var response = await s3.GetObjectAsync(bucket, objectKey, cancellationToken);
            // Photos are small (5 MB at most); copying releases the HTTP response immediately.
            var copy = new MemoryStream();
            await response.ResponseStream.CopyToAsync(copy, cancellationToken);
            copy.Position = 0;
            return copy;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Uri GetReadUrl(string container, string key, TimeSpan lifetime)
    {
        ObjectKey(container, key);
        return urls.GetReadUrl(container, key, lifetime);
    }

    public bool IsReadUrlValid(string container, string key, long expiresAtUnixSeconds, string signature) =>
        FileLocation.IsAllowed(container, key) && urls.IsValid(container, key, expiresAtUnixSeconds, signature);

    public async Task<bool> IsWritableAsync(CancellationToken ct)
    {
        var probeKey = $".health/{Guid.NewGuid():N}";
        try
        {
            await s3.PutObjectAsync(
                new PutObjectRequest { BucketName = bucket, Key = probeKey, ContentBody = "ok" }, ct);
            await s3.DeleteObjectAsync(bucket, probeKey, ct);
            return true;
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            return false;
        }
    }

    private static string ObjectKey(string container, string key) =>
        FileLocation.IsAllowed(container, key)
            ? $"{container}/{key}"
            : throw new ArgumentException("Container or key is not allowed.");
}
