using System.Text;
using System.Text.RegularExpressions;
using Gameya.Domain.Common;

namespace Gameya.Domain.Users;

public sealed partial record PhoneNumber
{
    private readonly string _local;

    private PhoneNumber(string local) => _local = local;

    public string E164 => "+2" + _local;

    public string Masked => _local[..3] + "****" + _local[^4..];

    public static PhoneNumber Parse(string input) =>
        Normalize(input) is { } local
            ? new PhoneNumber(local)
            : throw new DomainException(ErrorCodes.INVALID_PHONE);

    public static bool TryParse(string? input, out PhoneNumber? phone)
    {
        var local = input is null ? null : Normalize(input);
        phone = local is null ? null : new PhoneNumber(local);
        return phone is not null;
    }

    private static string? Normalize(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= '\u0660' and <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
            else if (ch is >= '\u06F0' and <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch is ' ' or '-') continue;
            else sb.Append(ch);
        }

        var s = sb.ToString();
        if (s.StartsWith("+20", StringComparison.Ordinal)) s = "0" + s[3..];
        else if (s.StartsWith("0020", StringComparison.Ordinal)) s = "0" + s[4..];

        return LocalPattern().IsMatch(s) ? s : null;
    }

    [GeneratedRegex(@"^01[0125][0-9]{8}\z")]
    private static partial Regex LocalPattern();
}
