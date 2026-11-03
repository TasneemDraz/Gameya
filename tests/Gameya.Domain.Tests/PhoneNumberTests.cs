using Gameya.Domain.Common;
using Gameya.Domain.Users;

namespace Gameya.Domain.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("01012345678")]
    [InlineData("010 1234 5678")]
    [InlineData("010-1234-5678")]
    [InlineData("+201012345678")]
    [InlineData("00201012345678")]
    [InlineData("٠١٠١٢٣٤٥٦٧٨")]
    [InlineData("۰۱۰۱۲۳۴۵۶۷۸")]
    public void Parse_normalizes_to_e164(string input) =>
        PhoneNumber.Parse(input).E164.ShouldBe("+201012345678");

    [Theory]
    [InlineData("01112345678", "+201112345678")]
    [InlineData("01212345678", "+201212345678")]
    [InlineData("01512345678", "+201512345678")]
    public void Parse_accepts_other_prefixes(string input, string expected) =>
        PhoneNumber.Parse(input).E164.ShouldBe(expected);

    [Theory]
    [InlineData("01312345678")]
    [InlineData("0101234567")]
    [InlineData("010123456789")]
    [InlineData("")]
    [InlineData("abc")]
    public void Parse_rejects_invalid(string input)
    {
        var ex = Should.Throw<DomainException>(() => PhoneNumber.Parse(input));
        ex.Code.ShouldBe(ErrorCodes.INVALID_PHONE);
        PhoneNumber.TryParse(input, out var p).ShouldBeFalse();
        p.ShouldBeNull();
    }

    [Fact]
    public void TryParse_null_is_false() => PhoneNumber.TryParse(null, out _).ShouldBeFalse();

    [Fact]
    public void Masked_hides_middle() =>
        PhoneNumber.Parse("01012345678").Masked.ShouldBe("010****5678");
}
