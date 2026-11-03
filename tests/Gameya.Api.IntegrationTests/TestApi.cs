using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Gameya.Api.IntegrationTests;

/// <summary>Shared helpers: OTP login and signed-URL construction for tests that call the API as a user.</summary>
internal static class TestApi
{
    public static string NewPhone() =>
        "01" + "0125"[Random.Shared.Next(4)] + Random.Shared.Next(0, 100_000_000).ToString("D8");

    /// <summary>
    /// Logs in with a fresh phone and sets the bearer header. The fake clock is moved to wall time first,
    /// because access tokens are validated against the wall clock.
    /// </summary>
    public static async Task<Guid> LoginAsync(GameyaApiFactory factory, HttpClient client, string? phone = null)
    {
        factory.Clock.UtcNow = DateTimeOffset.UtcNow;
        phone ??= NewPhone();
        (await client.PostAsJsonAsync("/api/auth/otp/request", new { phone, language = "en" })).EnsureSuccessStatusCode();
        using var codeDoc = JsonDocument.Parse(await client.GetStringAsync($"/api/dev/otp/{phone}"));
        var code = codeDoc.RootElement.GetProperty("code").GetString()!;

        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code });
        verify.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject);
    }

    public static Task<HttpResponseMessage> UploadPhotoAsync(HttpClient client, byte[] bytes, string fileName = "photo.jpg")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        return client.PutAsync("/api/me/photo", content);
    }

    public static byte[] TestFile(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestFiles", name));

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>Builds a correctly signed URL for any container/key, to prove checks beyond the signature.</summary>
    public static string SignedUrl(string container, string key, long exp)
    {
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(GameyaApiFactory.UrlSigningKey), Encoding.UTF8.GetBytes($"{container}/{key}:{exp}"));
        return $"/api/files/{container}/{key}?exp={exp}&sig={WebEncoders.Base64UrlEncode(mac)}";
    }
}
