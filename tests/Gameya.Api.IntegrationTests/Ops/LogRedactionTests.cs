using System.Net.Http.Json;
using System.Text.Json;
using Gameya.Api.Infrastructure;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Application.Abstractions;
using Gameya.Domain.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Parsing;

namespace Gameya.Api.IntegrationTests.Ops;

[Collection(DatabaseCollection.Name)]
public sealed class LogRedactionTests : IDisposable
{
    private const string LoggingSmsSenderContext = "Gameya.Infrastructure.Sms.LoggingSmsSender";

    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public LogRedactionTests(DatabaseFixture db)
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
    public void Exception_text_is_masked_in_the_real_formatter_output()
    {
        var output = new StringWriter();
        var sinkLogger = new LoggerConfiguration()
            .WriteTo.Sink(new FormattingSink(new CompactJsonFormatter(), output))
            .CreateLogger();
        var logger = new LoggerConfiguration()
            .Enrich.With<RedactingEnricher>()
            .WriteTo.Sink(new ExceptionRedactingSink(sinkLogger))
            .CreateLogger();
        var ex = new InvalidOperationException(
            "outer for +201012345678", new ArgumentException("inner Key (phone)=(+201112345678) exists"));

        logger.Error(ex, "failed");
        logger.Dispose();

        var line = output.ToString();
        line.ShouldContain("010****5678");
        line.ShouldContain("011****5678");
        line.ShouldNotContain("1012345678");
        line.ShouldNotContain("1112345678");
        System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("@x").GetString()!.ShouldContain("InvalidOperationException");
    }

    [Fact]
    public async Task Unhandled_500_with_a_phone_in_the_exception_logs_no_unmasked_phone()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddSingleton<ISmsSender, ThrowingSmsSender>()));
        using var client = factory.CreateClient();
        var phone = TestApi.NewPhone(); // fresh: the shared database may already hold a challenge for a fixed number

        var response = await client.PostAsJsonAsync("/api/auth/otp/request", new { phone, language = "en" });

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.InternalServerError);
        var failures = _factory.LogSink.Events.Where(e => e.Exception is not null).ToList();
        failures.ShouldNotBeEmpty();
        foreach (var text in _factory.LogSink.Events.Select(CapturingSink.FullText))
        {
            text.ShouldNotContain(phone);
            text.ShouldNotContain("+2" + phone);
        }

        failures.ShouldContain(e => e.Exception!.ToString().Contains("****"));
    }

    /// <summary>Writes events the way the console sink does: through the same formatter.</summary>
    private sealed class FormattingSink(Serilog.Formatting.ITextFormatter formatter, TextWriter writer) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => formatter.Format(logEvent, writer);
    }

    private sealed class ThrowingSmsSender : ISmsSender
    {
        public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"provider rejected {to.E164}");
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
