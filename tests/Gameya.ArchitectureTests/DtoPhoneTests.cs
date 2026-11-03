using System.Reflection;
using Gameya.Application.Security;
using Gameya.Domain.Users;

namespace Gameya.ArchitectureTests;

public class DtoPhoneTests
{
    [Fact]
    public void No_dto_exposes_raw_phone()
    {
        var offenders = FindOffenders(Assembly.Load("Gameya.Application").GetExportedTypes());
        offenders.ShouldBeEmpty("DTOs must use DisplayPhone (or [OwnPhone] on the caller's own string phone). Offenders: "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void Scanner_detects_a_raw_PhoneNumber_property() =>
        FindOffenders([typeof(RawPhoneNumberDto)]).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_detects_a_raw_PhoneNumber_inside_a_collection() =>
        FindOffenders([typeof(CollectionPhoneResult)]).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_detects_an_unmarked_string_phone() =>
        FindOffenders([typeof(StringPhoneDto)]).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_accepts_OwnPhone_strings_and_DisplayPhone()
    {
        FindOffenders([typeof(OwnPhoneStringDto)]).ShouldBeEmpty();
        FindOffenders([typeof(DisplayPhoneResult)]).ShouldBeEmpty();
    }

    private static List<string> FindOffenders(IEnumerable<Type> types)
    {
        var offenders = new List<string>();
        foreach (var type in types.Where(t => t.Name.EndsWith("Dto", StringComparison.Ordinal)
                                              || t.Name.EndsWith("Result", StringComparison.Ordinal)))
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (ContainsPhoneNumber(p.PropertyType))
                {
                    offenders.Add($"{type.FullName}.{p.Name} exposes PhoneNumber");
                }
                else if (p.PropertyType == typeof(string)
                         && p.Name.Contains("Phone", StringComparison.OrdinalIgnoreCase)
                         && p.GetCustomAttribute<OwnPhoneAttribute>() is null)
                {
                    offenders.Add($"{type.FullName}.{p.Name} is a string phone without [OwnPhone]");
                }
            }
        }

        return offenders;
    }

    private static bool ContainsPhoneNumber(Type t) =>
        t == typeof(PhoneNumber)
        || (t.IsArray && ContainsPhoneNumber(t.GetElementType()!))
        || (t.IsGenericType && t.GetGenericArguments().Any(ContainsPhoneNumber));

    public sealed record RawPhoneNumberDto(PhoneNumber Phone);

    public sealed record CollectionPhoneResult(IReadOnlyList<PhoneNumber> Phones);

    public sealed record StringPhoneDto(string Phone);

    public sealed record OwnPhoneStringDto([property: OwnPhone] string Phone);

    public sealed record DisplayPhoneResult(DisplayPhone Phone);
}
