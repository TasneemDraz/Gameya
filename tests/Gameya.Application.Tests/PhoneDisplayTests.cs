using System.Text.Json;
using Gameya.Application.Security;
using Gameya.Domain.Users;

namespace Gameya.Application.Tests;

public class PhoneDisplayTests
{
    private static readonly PhoneNumber Phone = PhoneNumber.Parse("01012345678");

    // The default encoder escapes the plus sign as a unicode escape; decoding the JSON string gives the value a client sees.
    private static string? Wire(DisplayPhone phone) => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(phone));

    [Fact]
    public void Self_sees_the_full_number()
    {
        var id = Guid.NewGuid();
        Wire(PhoneDisplay.For(id, id, Phone, false)).ShouldBe("+201012345678");
    }

    [Fact]
    public void Organizer_of_the_subjects_gameya_sees_the_full_number() =>
        Wire(PhoneDisplay.For(Guid.NewGuid(), Guid.NewGuid(), Phone, true))
            .ShouldBe("+201012345678");

    [Fact]
    public void Other_member_sees_the_masked_number() =>
        Wire(PhoneDisplay.For(Guid.NewGuid(), Guid.NewGuid(), Phone, false))
            .ShouldBe("010****5678");

    [Fact]
    public void Display_phone_serializes_as_a_plain_string_inside_an_object()
    {
        var json = JsonSerializer.Serialize(new { Phone = PhoneDisplay.For(Guid.NewGuid(), Guid.NewGuid(), Phone, false) });
        json.ShouldBe("{\"Phone\":\"010****5678\"}");
    }
}
