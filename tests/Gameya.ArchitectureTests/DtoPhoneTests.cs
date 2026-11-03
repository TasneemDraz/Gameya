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
    public void Scanner_detects_a_public_field() =>
        FindOffenders([typeof(FieldPhoneDto)]).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_detects_a_nested_record_without_a_Dto_suffix() =>
        FindOffenders([typeof(HoldsNestedDto)]).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_detects_a_list_of_a_nested_record() =>
        FindOffenders([typeof(HoldsListResult)]).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_terminates_on_cyclic_types()
    {
        FindOffenders([typeof(CycleADto)]).ShouldBeEmpty();
        FindOffenders([typeof(CycleLeakDto)]).ShouldNotBeEmpty();
    }

    [Fact]
    public void Scanner_accepts_OwnPhone_strings_and_DisplayPhone()
    {
        FindOffenders([typeof(OwnPhoneStringDto)]).ShouldBeEmpty();
        FindOffenders([typeof(DisplayPhoneResult)]).ShouldBeEmpty();
        FindOffenders([typeof(HoldsSafeListResult)]).ShouldBeEmpty();
    }

    private const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;

    private static List<string> FindOffenders(IEnumerable<Type> types)
    {
        var offenders = new List<string>();
        var visited = new HashSet<Type>();
        foreach (var type in types.Where(t => t.Name.EndsWith("Dto", StringComparison.Ordinal)
                                              || t.Name.EndsWith("Result", StringComparison.Ordinal)))
        {
            Walk(type, type.Name, visited, offenders);
        }

        return offenders;
    }

    private static void Walk(Type type, string path, HashSet<Type> visited, List<string> offenders)
    {
        if (!visited.Add(type))
        {
            return;
        }

        var members = type.GetProperties(Members).Select(p => (p.Name, Type: p.PropertyType, Member: (MemberInfo)p))
            .Concat(type.GetFields(Members).Select(f => (f.Name, Type: f.FieldType, Member: (MemberInfo)f)));

        foreach (var (name, memberType, member) in members)
        {
            var where = $"{path}.{name}";
            if (Unwrap(memberType).Contains(typeof(PhoneNumber)))
            {
                offenders.Add($"{where} exposes PhoneNumber");
            }
            else if (memberType == typeof(string)
                     && name.Contains("Phone", StringComparison.OrdinalIgnoreCase)
                     && member.GetCustomAttribute<OwnPhoneAttribute>() is null)
            {
                offenders.Add($"{where} is a string phone without [OwnPhone]");
            }

            foreach (var inner in Unwrap(memberType).Where(IsWalkable))
            {
                Walk(inner, where, visited, offenders);
            }
        }
    }

    /// <summary>The type itself plus, recursively, array elements and generic arguments.</summary>
    private static IEnumerable<Type> Unwrap(Type t)
    {
        yield return t;
        if (t.IsArray)
        {
            foreach (var u in Unwrap(t.GetElementType()!)) yield return u;
        }

        if (t.IsGenericType)
        {
            foreach (var arg in t.GetGenericArguments())
                foreach (var u in Unwrap(arg)) yield return u;
        }
    }

    private static bool IsWalkable(Type t) =>
        !t.IsEnum && !t.IsPrimitive && t != typeof(PhoneNumber) && t != typeof(DisplayPhone) && !t.IsGenericParameter
        && !t.IsArray && !t.IsGenericType
        && (t.Assembly.GetName().Name?.StartsWith("Gameya", StringComparison.Ordinal) ?? false);

    public sealed record RawPhoneNumberDto(PhoneNumber Phone);

    public sealed record CollectionPhoneResult(IReadOnlyList<PhoneNumber> Phones);

    public sealed record StringPhoneDto(string Phone);

    public sealed class FieldPhoneDto
    {
        public string PhoneField = "";
    }

    public sealed record LeakyRow(PhoneNumber Phone);

    public sealed record HoldsNestedDto(LeakyRow Row);

    public sealed record HoldsListResult(IReadOnlyList<LeakyRow> Rows);

    public sealed record SafeRow(DisplayPhone Phone);

    public sealed record HoldsSafeListResult(IReadOnlyList<SafeRow> Rows);

    public sealed record CycleADto(CycleB B);

    public sealed record CycleB(CycleADto A);

    public sealed record CycleLeakDto(CycleLeakB B);

    public sealed record CycleLeakB(CycleLeakDto A, string MobilePhone);

    public sealed record OwnPhoneStringDto([property: OwnPhone] string Phone);

    public sealed record DisplayPhoneResult(DisplayPhone Phone);
}
