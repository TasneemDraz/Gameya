using Gameya.Application.Abstractions;
using Gameya.Application.Security;
using MediatR;

namespace Gameya.Application.Tests;

public class AccessAttributeCoverageTests
{
    internal static IEnumerable<string> Violations(IEnumerable<Type> types) =>
        types
            .Where(t => t is { IsAbstract: false, IsInterface: false } &&
                        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .Select(t => (Type: t, Count: t.GetCustomAttributes(typeof(AccessAttribute), inherit: false).Length))
            .Where(x => x.Count != 1)
            .Select(x => $"{x.Type.FullName} has {x.Count} access attributes");

    [Fact]
    public void Every_request_has_exactly_one_access_attribute()
    {
        var violations = Violations(typeof(DependencyInjection).Assembly.GetTypes()).ToList();
        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [PublicAccess, RequiresAuth]
    private sealed record Two : IQuery<int>;

    private sealed record None : IQuery<int>;

    [PublicAccess]
    private sealed record Ok : ICommand<int>;

    [Fact]
    public void Check_detects_missing_and_duplicate_attributes()
    {
        Violations([typeof(Two), typeof(None), typeof(Ok)]).Count().ShouldBe(2);
    }
}
