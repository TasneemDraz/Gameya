using Amazon.Runtime;
using Amazon.S3;

namespace Gameya.Infrastructure.Files;

public static class S3ClientFactory
{
    /// <summary>
    /// Path-style addressing, and checksums only where the API requires them: Backblaze B2 rejects the
    /// SDK's default checksum headers.
    /// </summary>
    public static AmazonS3Client Create(S3StorageOptions o) =>
        new(
            new BasicAWSCredentials(o.AccessKeyId, o.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = o.ServiceUrl,
                AuthenticationRegion = o.Region,
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
}
