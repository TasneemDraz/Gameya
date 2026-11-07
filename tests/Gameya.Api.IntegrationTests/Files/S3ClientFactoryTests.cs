using Amazon.Runtime;
using Amazon.S3;
using Gameya.Infrastructure.Files;

namespace Gameya.Api.IntegrationTests.Files;

public sealed class S3ClientFactoryTests
{
    [Fact]
    public void Create_uses_path_style_and_when_required_checksums()
    {
        var options = new S3StorageOptions
        {
            ServiceUrl = "https://s3.us-west-004.backblazeb2.com",
            Bucket = "b",
            AccessKeyId = "id",
            SecretAccessKey = "secret",
            Region = "us-west-004"
        };

        using var client = S3ClientFactory.Create(options);

        var config = client.Config.ShouldBeOfType<AmazonS3Config>();
        config.ForcePathStyle.ShouldBeTrue();
        config.RequestChecksumCalculation.ShouldBe(RequestChecksumCalculation.WHEN_REQUIRED);
        config.ResponseChecksumValidation.ShouldBe(ResponseChecksumValidation.WHEN_REQUIRED);
        new Uri(config.ServiceURL).ShouldBe(new Uri(options.ServiceUrl));
    }
}
