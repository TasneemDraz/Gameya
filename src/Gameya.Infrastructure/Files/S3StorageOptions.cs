namespace Gameya.Infrastructure.Files;

/// <summary>Settings for an S3-compatible bucket (Backblaze B2 in production). All but <see cref="Region"/> are required.</summary>
public sealed class S3StorageOptions
{
    public const string Section = "Storage:S3";

    public string ServiceUrl { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
}
