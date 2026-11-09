using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Application.Abstractions;
using Gameya.Infrastructure.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gameya.Api.IntegrationTests.Files;

[Collection(DatabaseCollection.Name)]
public sealed class S3FileStoreTests(MinioFixture minio)
{
    private static string NewKey() => $"{Guid.NewGuid()}/{Guid.NewGuid()}.jpg";

    private static SignedFileUrls Urls() => new(GameyaApiFactory.UrlSigningKey, new MutableClock(DateTimeOffset.UtcNow));

    private S3FileStore Store(string? bucket = null) =>
        new(minio.CreateClient(), bucket ?? MinioFixture.Bucket, Urls(), NullLogger<S3FileStore>.Instance);

    [Fact]
    public async Task Upload_then_open_read_returns_same_bytes()
    {
        var store = Store();
        var key = NewKey();
        byte[] bytes = [1, 2, 3, 4, 5, 255];

        await store.UploadAsync(FileContainers.ProfilePhotos, key, new MemoryStream(bytes), "image/jpeg", default);

        await using var stream = await store.OpenReadAsync(FileContainers.ProfilePhotos, key, default);
        stream.ShouldNotBeNull();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        copy.ToArray().ShouldBe(bytes);
    }

    [Fact]
    public async Task Open_read_missing_returns_null()
    {
        (await Store().OpenReadAsync(FileContainers.ProfilePhotos, NewKey(), default)).ShouldBeNull();
    }

    [Fact]
    public async Task Delete_removes_object()
    {
        var store = Store();
        var key = NewKey();
        await store.UploadAsync(FileContainers.Proofs, key, new MemoryStream([9]), "image/jpeg", default);

        await store.DeleteAsync(FileContainers.Proofs, key, default);

        (await store.OpenReadAsync(FileContainers.Proofs, key, default)).ShouldBeNull();
    }

    [Fact]
    public async Task Disallowed_container_throws_ArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            Store().UploadAsync("../x", NewKey(), new MemoryStream([1]), "image/jpeg", default));
    }

    [Fact]
    public async Task Probe_is_writable_against_bucket()
    {
        (await Store().IsWritableAsync(default)).ShouldBeTrue();
    }

    [Fact]
    public async Task Probe_is_false_for_missing_bucket()
    {
        (await Store("no-such-bucket").IsWritableAsync(default)).ShouldBeFalse();
    }
}
