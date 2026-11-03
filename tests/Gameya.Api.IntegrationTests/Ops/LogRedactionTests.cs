using System.Net.Http.Json;
using System.Text.Json;
using Gameya.Api.Infrastructure;
using Gameya.Api.IntegrationTests.Infrastructure;
using Serilog.Events;
using Serilog.Parsing;

namespace Gameya.Api.IntegrationTests.Ops;

[Collection(PostgresCollection.Name)]
public sealed class LogRedactionTests : IDisposable
{
    private const string LoggingSmsSenderContext = "Gameya.Infrastructure.Sms.LoggingSmsSender";

    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public LogRedactionTests(PostgresFixture db)
    {
        _factory = new GameyaApiFactory(db);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Logs_never_contain_unmasked_phone_or_otp_code()
    {
        const string phone = "01012345678";
        _factory.Clock.UtcNow = DateTimeOffset.UtcNow;
        (await _client.PostAsJsonAsync("/api/auth/otp/request", new { phone, language = "en" })).EnsureSuccessStatusCode();
        using var codeDoc = JsonDocument.Parse(await _client.GetStringAsync($"/api/dev/otp/{phone}"));
        var code = codeDoc.RootElement.GetProperty("code").GetString()!;
        (await _client.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code })).EnsureSuccessStatusCode();

        var events = _factory.LogSink.Events
            .Where(e => CapturingSink.SourceContext(e) != LoggingSmsSenderContext)
            .ToList();

        // The request-logging events must exist, otherwise this test proves nothing about them.
        events.ShouldContain(e => e.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal));
        foreach (var text in events.Select(CapturingSink.FullText))
        {
            text.ShouldNotContain("01012345678");
            text.ShouldNotContain("+201012345678");
            text.ShouldNotContain(code);
        }
    }

    [Fact]
    public async Task Request_logs_do_not_record_the_query_string_of_signed_file_urls()
    {
        await TestApi.LoginAsync(_factory, _client);
        var upload = await TestApi.UploadPhotoAsync(_client, TestApi.TestFile("photo.jpg"));
        var url = (await TestApi.JsonAsync(upload)).GetProperty("photoUrl").GetString()!;
        var sig = url[(url.IndexOf("sig=", StringComparison.Ordinal) + 4)..];

        (await _client.GetAsync(url)).EnsureSuccessStatusCode();

        _factory.LogSink.Events.ShouldContain(e => e.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal));
        foreach (var text in _factory.LogSink.Events.Select(CapturingSink.FullText))
        {
            text.ShouldNotContain(sig);
            text.ShouldNotContain("sig=");
        }
    }

    [Fact]
    public void Enricher_masks_egyptian_phones_in_any_string_property()
    {
        var evt = NewEvent(
            ("Msg", new ScalarValue("call +201012345678 now")),
            ("Nested", new StructureValue([new LogEventProperty("p", new ScalarValue("+201112345678"))])));

        new RedactingEnricher().Enrich(evt, null!);

        evt.Properties["Msg"].ToString().ShouldBe("\"call 010****5678 now\"");
        evt.Properties["Nested"].ToString().ShouldContain("011****5678");
        CapturingSink.FullText(evt).ShouldNotContain("1012345678");
    }

    [Theory]
    [InlineData("code")]
    [InlineData("token")]
    [InlineData("refreshToken")]
    [InlineData("email")]
    [InlineData("Email")]
    public void Enricher_drops_sensitive_properties(string name)
    {
        var evt = NewEvent((name, new ScalarValue("secret")), ("Keep", new ScalarValue("ok")));

        new RedactingEnricher().Enrich(evt, null!);

        evt.Properties.ShouldNotContainKey(name);
        evt.Properties.ShouldContainKey("Keep");
    }

    [Fact]
    public void Enricher_leaves_the_dev_sms_sender_untouched()
    {
        var evt = NewEvent(
            ("SourceContext", new ScalarValue(LoggingSmsSenderContext)),
            ("Message", new ScalarValue("Your code is 123456")));

        new RedactingEnricher().Enrich(evt, null!);

        evt.Properties["Message"].ToString().ShouldContain("123456");
    }

    private static LogEvent NewEvent(params (string Name, LogEventPropertyValue Value)[] properties) =>
        new(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplate("test", [new TextToken("test")]),
            properties.Select(p => new LogEventProperty(p.Name, p.Value)));
}
