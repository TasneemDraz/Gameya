using Amazon.S3;
using Gameya.Infrastructure.Files;
using Testcontainers.Minio;

namespace Gameya.Api.IntegrationTests.Infrastructure;

/// <summary>One MinIO container per test collection, with the bucket <see cref="Bucket"/> created.</summary>
public sealed class MinioFixture : IAsyncLifetime
{
    public const string Bucket = "gameya-test";

    private readonly MinioContainer _container = new MinioBuilder("cgr.dev/chainguard/minio:latest@sha256:869ca3178831d9f521ea02258a0f0d4fbc1e9f84302e707514e1f861b860865d").Build();

    public S3StorageOptions Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Options = new S3StorageOptions
        {
            ServiceUrl = _container.GetConnectionString(),
            Bucket = Bucket,
            AccessKeyId = _container.GetAccessKey(),
            SecretAccessKey = _container.GetSecretKey(),
            Region = "us-east-1"
        };

        using var client = S3ClientFactory.Create(Options);
        await client.PutBucketAsync(Bucket);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AmazonS3Client CreateClient() => S3ClientFactory.Create(Options);

    /// <summary>The settings that make the API use this MinIO bucket as its file store.</summary>
    public IReadOnlyDictionary<string, string?> ApiSettings() => new Dictionary<string, string?>
    {
        ["Storage:Provider"] = "S3",
        ["Storage:S3:ServiceUrl"] = Options.ServiceUrl,
        ["Storage:S3:Bucket"] = Options.Bucket,
        ["Storage:S3:AccessKeyId"] = Options.AccessKeyId,
        ["Storage:S3:SecretAccessKey"] = Options.SecretAccessKey,
        ["Storage:S3:Region"] = Options.Region
    };
}
