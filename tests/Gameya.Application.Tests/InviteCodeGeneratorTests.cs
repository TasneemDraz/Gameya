using System.Text.RegularExpressions;
using Gameya.Application.Gameyas;

namespace Gameya.Application.Tests;

public class InviteCodeGeneratorTests
{
    [Fact]
    public void Codes_are_6_chars_from_the_alphabet()
    {
        var generator = new InviteCodeGenerator();
        var codes = Enumerable.Range(0, 1000).Select(_ => generator.Next()).ToList();

        codes.ShouldAllBe(c => Regex.IsMatch(c, "^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$"));
        codes.Distinct().Count().ShouldBeGreaterThan(990);
    }
}
