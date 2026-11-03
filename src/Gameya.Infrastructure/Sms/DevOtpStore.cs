using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Gameya.Domain.Users;

namespace Gameya.Infrastructure.Sms;

/// <summary>Development and E2E only: remembers the last SMS per phone so tests can read the OTP code.</summary>
public sealed partial class DevOtpStore
{
    private readonly ConcurrentDictionary<string, string> _lastMessage = new();

    public void Record(PhoneNumber phone, string message) => _lastMessage[phone.E164] = message;

    public string? TryGetCode(PhoneNumber phone) =>
        _lastMessage.TryGetValue(phone.E164, out var message) && Code().Match(message) is { Success: true } m
            ? m.Value
            : null;

    [GeneratedRegex(@"[0-9]{6}")]
    private static partial Regex Code();
}
