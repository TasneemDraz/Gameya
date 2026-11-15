using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Gameyas;
using Microsoft.EntityFrameworkCore;
using static Gameya.Api.IntegrationTests.Gameyas.GameyaSeed;

namespace Gameya.Api.IntegrationTests.Gameyas;

[Collection(DatabaseCollection.Name)]
public sealed class GameyaSetupTests : IDisposable
{
    private const string CodePattern = "^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$";

    private readonly DatabaseFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public GameyaSetupTests(DatabaseFixture db)
    {
        _db = db;
        _factory = new GameyaApiFactory(db);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Create_returns_201_and_stores_a_Draft_with_organizer_share()
    {
        var organizerId = await TestApi.LoginAsync(_factory, _client);

        var response = await _client.PostAsJsonAsync("/api/gameyas", GameyaTestApi.ValidBody(GameyaTestApi.Today()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = (await TestApi.JsonAsync(response)).GetProperty("id").GetGuid();
        response.Headers.Location!.OriginalString.ShouldBe($"/api/gameyas/{id}");

        await using var ctx = _db.CreateAppContext();
        var stored = await LoadAsync(ctx, id);
        stored.Status.ShouldBe(GameyaStatus.Draft);
        stored.OrganizerId.ShouldBe(organizerId);
        stored.ShareCount.ShouldBe(10);
        stored.InviteCode.ShouldBeNull();
        var membership = stored.Memberships.ShouldHaveSingleItem();
        membership.UserId.ShouldBe(organizerId);
        membership.Shares.Count.ShouldBe(1);
        (await ctx.ActivityLog.CountAsync(a => a.Action == "GAMEYA_CREATED" && a.GameyaId == id && a.ActorUserId == organizerId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Create_with_invalid_fields_returns_400_with_field_errors()
    {
        await TestApi.LoginAsync(_factory, _client);

        var response = await _client.PostAsJsonAsync(
            "/api/gameyas", GameyaTestApi.Body(GameyaTestApi.Today(), new { name = "ab", shareCount = 51 }));

        await ShouldFailWith(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var errors = (await TestApi.JsonAsync(response)).GetProperty("errors");
        errors.TryGetProperty("name", out _).ShouldBeTrue(errors.ToString());
        errors.TryGetProperty("shareCount", out _).ShouldBeTrue(errors.ToString());
    }

    [Fact]
    public async Task Open_generates_a_code_and_logs_without_the_code()
    {
        await TestApi.LoginAsync(_factory, _client);
        var id = await GameyaTestApi.CreateAsync(_client);

        var code = await GameyaTestApi.OpenAsync(_db, _client, id);

        code.ShouldMatch(CodePattern);
        await using var ctx = _db.CreateAppContext();
        (await ctx.Gameyas.SingleAsync(g => g.Id == id)).Status.ShouldBe(GameyaStatus.Recruiting);
        var entry = await ctx.ActivityLog.SingleAsync(a => a.Action == "GAMEYA_OPENED" && a.GameyaId == id);
        entry.DetailsJson.ShouldNotContain(code);
        (entry.Note ?? string.Empty).ShouldNotContain(code);
    }

    [Fact]
    public async Task Regenerate_replaces_the_code()
    {
        await TestApi.LoginAsync(_factory, _client);
        var id = await GameyaTestApi.CreateAsync(_client);
        var first = await GameyaTestApi.OpenAsync(_db, _client, id);

        var response = await _client.PostAsync($"/api/gameyas/{id}/invite-code", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var second = await GameyaTestApi.CodeAsync(_db, id);
        second.ShouldMatch(CodePattern);
        second.ShouldNotBe(first);
        await using var ctx = _db.CreateAppContext();
        var entry = await ctx.ActivityLog.SingleAsync(a => a.Action == "INVITE_CODE_REGENERATED" && a.GameyaId == id);
        entry.DetailsJson.ShouldNotContain(second);
    }

    [Fact]
    public async Task Update_while_Recruiting_bumps_terms_and_logs_old_and_new_amount()
    {
        await TestApi.LoginAsync(_factory, _client);
        var id = await GameyaTestApi.CreateAsync(_client);
        await GameyaTestApi.OpenAsync(_db, _client, id);

        var response = await _client.PutAsJsonAsync(
            $"/api/gameyas/{id}", GameyaTestApi.Body(GameyaTestApi.Today(), new { contributionAmount = 2000 }));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        await using var ctx = _db.CreateAppContext();
        var stored = await ctx.Gameyas.SingleAsync(g => g.Id == id);
        stored.ContributionAmount.ShouldBe(2000m);
        stored.TermsVersion.ShouldBe(2);
        var entry = await ctx.ActivityLog.SingleAsync(a => a.Action == "GAMEYA_SETTINGS_CHANGED" && a.GameyaId == id);
        using var details = JsonDocument.Parse(entry.DetailsJson);
        var amount = details.RootElement.GetProperty("changed").GetProperty("contributionAmount");
        amount.GetProperty("old").GetDecimal().ShouldBe(1000m);
        amount.GetProperty("new").GetDecimal().ShouldBe(2000m);
        details.RootElement.GetProperty("termsVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Cancel_returns_204_and_a_second_cancel_is_409_INVALID_STATUS_TRANSITION()
    {
        await TestApi.LoginAsync(_factory, _client);
        var id = await GameyaTestApi.CreateAsync(_client);

        var first = await _client.PostAsJsonAsync($"/api/gameyas/{id}/cancel", new { reason = "Changed our plans" });
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent, await first.Content.ReadAsStringAsync());

        var second = await _client.PostAsJsonAsync($"/api/gameyas/{id}/cancel", new { reason = (string?)null });
        await ShouldFailWith(second, HttpStatusCode.Conflict, "INVALID_STATUS_TRANSITION");

        await using var ctx = _db.CreateAppContext();
        var stored = await ctx.Gameyas.SingleAsync(g => g.Id == id);
        stored.Status.ShouldBe(GameyaStatus.Cancelled);
        stored.CancelReason.ShouldBe("Changed our plans");
        (await ctx.ActivityLog.CountAsync(a => a.Action == "GAMEYA_CANCELLED" && a.GameyaId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task Member_calling_organizer_endpoints_gets_404()
    {
        await TestApi.LoginAsync(_factory, _client);
        var id = await GameyaTestApi.CreateAsync(_client);
        await GameyaTestApi.OpenAsync(_db, _client, id);

        using var memberClient = _factory.CreateClient();
        var memberId = await TestApi.LoginAsync(_factory, memberClient);
        await using (var seed = _db.CreateAppContext())
        {
            var gameya = await LoadAsync(seed, id);
            gameya.ApproveJoinRequest(gameya.RequestToJoin(memberId, OneShare()).Id, Now);
            await seed.SaveChangesAsync();
        }

        var today = GameyaTestApi.Today();
        await ShouldFailWith(
            await memberClient.PutAsJsonAsync($"/api/gameyas/{id}", GameyaTestApi.ValidBody(today)),
            HttpStatusCode.NotFound, "GAMEYA_NOT_FOUND");
        await ShouldFailWith(
            await memberClient.PostAsync($"/api/gameyas/{id}/open", null), HttpStatusCode.NotFound, "GAMEYA_NOT_FOUND");
        await ShouldFailWith(
            await memberClient.PostAsync($"/api/gameyas/{id}/invite-code", null),
            HttpStatusCode.NotFound, "GAMEYA_NOT_FOUND");
        await ShouldFailWith(
            await memberClient.PostAsJsonAsync($"/api/gameyas/{id}/cancel", new { reason = (string?)null }),
            HttpStatusCode.NotFound, "GAMEYA_NOT_FOUND");

        await using var ctx = _db.CreateAppContext();
        (await ctx.Gameyas.SingleAsync(g => g.Id == id)).Status.ShouldBe(GameyaStatus.Recruiting);
    }
}
