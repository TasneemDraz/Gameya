using System.Net;
using System.Text;
using System.Text.Json;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using Gameya.Infrastructure.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gameya.Api.IntegrationTests.Sms;

public sealed class SmsGateSmsSenderTests
{
    private static readonly SmsGateOptions Config = new()
    {
        BaseUrl = "https://gate.example/3rdparty/v1", Username = "user", Password = "pass"
    };

    private static readonly PhoneNumber Phone = PhoneNumber.Parse("01012345678");

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(ct);
        }
    }

    private sealed class ListLogger : ILogger<SmsGateSmsSender>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add(formatter(state, exception));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static StubHandler Responding(HttpStatusCode status, string json) =>
        new(_ => Task.FromResult(Json(status, json)));

    private static SmsGateSmsSender Sender(StubHandler handler, ListLogger? logger = null, TimeSpan? timeout = null) =>
        new(new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(10) },
            Options.Create(Config), logger ?? new ListLogger());

    [Fact]
    public async Task Sends_basic_auth_and_e164_number_and_text()
    {
        var handler = Responding(HttpStatusCode.Accepted, """{"id":"msg-1","state":"Pending"}""");

        await Sender(handler).SendAsync(Phone, "hello", CancellationToken.None);

        handler.Request!.Method.ShouldBe(HttpMethod.Post);
        handler.Request.RequestUri!.ToString().ShouldBe("https://gate.example/3rdparty/v1/messages");
        handler.Request.Headers.Authorization!.Scheme.ShouldBe("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(handler.Request.Headers.Authorization.Parameter!))
            .ShouldBe("user:pass");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("textMessage").GetProperty("text").GetString().ShouldBe("hello");
        body.RootElement.GetProperty("phoneNumbers").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["+201012345678"]);
    }

    [Fact]
    public async Task Non_success_status_throws_SMS_SEND_FAILED()
    {
        var handler = Responding(HttpStatusCode.Unauthorized, "{}");

        var ex = await Should.ThrowAsync<DomainException>(
            () => Sender(handler).SendAsync(Phone, "hello", CancellationToken.None));

        ex.Code.ShouldBe(ErrorCodes.SMS_SEND_FAILED);
    }

    [Fact]
    public async Task Timeout_throws_SMS_SEND_FAILED_and_sends_once()
    {
        var handler = new StubHandler(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, "{}");
        });

        var ex = await Should.ThrowAsync<DomainException>(() =>
            Sender(handler, timeout: TimeSpan.FromMilliseconds(100)).SendAsync(Phone, "hello", CancellationToken.None));

        ex.Code.ShouldBe(ErrorCodes.SMS_SEND_FAILED);
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Log_contains_masked_phone_and_never_the_code()
    {
        var logger = new ListLogger();
        var handler = Responding(HttpStatusCode.Accepted, """{"id":"msg-42"}""");

        await Sender(handler, logger).SendAsync(Phone, "Your code is 987654", CancellationToken.None);

        var log = string.Join("\n", logger.Lines);
        log.ShouldContain(Phone.Masked);
        log.ShouldContain("msg-42");
        log.ShouldNotContain("987654");
        log.ShouldNotContain("01012345678");
        log.ShouldNotContain("+201012345678");
    }
}
