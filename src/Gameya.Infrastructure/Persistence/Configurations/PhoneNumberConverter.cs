using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Gameya.Infrastructure.Persistence.Configurations;

/// <summary>Stores PhoneNumber as its E.164 text.</summary>
public sealed class PhoneNumberConverter()
    : ValueConverter<PhoneNumber, string>(p => p.E164, s => PhoneNumber.Parse(s))
{
    public static readonly PhoneNumberConverter Instance = new();
}
