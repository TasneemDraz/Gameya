using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gameya.Api.IntegrationTests.Gameyas;

/// <summary>HTTP helpers for gam'eya tests: a valid create body, create, and open (returning the stored code).</summary>
internal static class GameyaTestApi
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A valid body for ten 1,000 EGP monthly shares, ByRequest, starting a week after <paramref name="today"/>,
    /// with the organizer holding one share.
    /// </summary>
    public static JsonObject ValidBody(DateOnly today) => new()
    {
        ["name"] = "Family circle",
        ["description"] = null,
        ["contributionAmount"] = 1000,
        ["shareCount"] = 10,
        ["frequency"] = "Monthly",
        ["startDate"] = today.AddDays(7).ToString("yyyy-MM-dd"),
        ["graceDays"] = 3,
        ["payoutTiming"] = "AfterFullCollection",
        ["payoutOffsetDays"] = null,
        ["orderMethod"] = "ByRequest",
        ["maxSharesPerMember"] = 2,
        ["organizerParticipates"] = true,
        ["rulesText"] = null,
        ["organizerShares"] = 1,
    };

    /// <summary>The valid body with the given (camelCase) properties replaced.</summary>
    public static JsonObject Body(DateOnly today, object? overrides = null)
    {
        var body = ValidBody(today);
        if (overrides is not null && JsonSerializer.SerializeToNode(overrides, Web) is JsonObject changes)
        {
            foreach (var (key, value) in changes)
            {
                body[key] = value?.DeepClone();
            }
        }

        return body;
    }

    /// <summary>Cairo today by the wall clock, which <see cref="TestApi.LoginAsync"/> sets the fake clock to.</summary>
    public static DateOnly Today() => CairoCalendar.ToCairoDate(DateTimeOffset.UtcNow);

    public static async Task<Guid> CreateAsync(HttpClient client, object? overrides = null)
    {
        var response = await client.PostAsJsonAsync("/api/gameyas", Body(Today(), overrides));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await TestApi.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Opens the gam'eya and returns its invite code, read from the database.</summary>
    public static async Task<string> OpenAsync(DatabaseFixture db, HttpClient client, Guid id)
    {
        var response = await client.PostAsync($"/api/gameyas/{id}/open", null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        return await CodeAsync(db, id);
    }

    public static async Task<string> CodeAsync(DatabaseFixture db, Guid id)
    {
        await using var ctx = db.CreateAppContext();
        var code = await ctx.Gameyas.Where(g => g.Id == id).Select(g => g.InviteCode).SingleAsync();
        return code.ShouldNotBeNull();
    }
}
