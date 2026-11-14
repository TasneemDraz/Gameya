using Gameya.Application.Abstractions;
using Gameya.Application.Gameyas;
using Gameya.Application.Security;
using MediatR;

namespace Gameya.Application.Tests;

public class AccessAttributeCoverageTests
{
    private static bool IsRequest(Type t) =>
        t is { IsAbstract: false, IsInterface: false } &&
        (typeof(IRequest).IsAssignableFrom(t) ||
         t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));

    private static bool IsCommandOrQuery(Type t) =>
        t.GetInterfaces().Any(i => i.IsGenericType &&
            i.GetGenericTypeDefinition() is var d && (d == typeof(ICommand<>) || d == typeof(IQuery<>)));

    internal static IEnumerable<string> AccessViolations(IEnumerable<Type> types) =>
        types.Where(IsRequest)
            .Select(t => (Type: t, Count: t.GetCustomAttributes(typeof(AccessAttribute), inherit: false).Length))
            .Where(x => x.Count != 1)
            .Select(x => $"{x.Type.FullName} has {x.Count} access attributes");

    internal static IEnumerable<string> KindViolations(IEnumerable<Type> types) =>
        types.Where(IsRequest).Where(t => !IsCommandOrQuery(t))
            .Select(t => $"{t.FullName} is neither ICommand<> nor IQuery<>");

    internal static IEnumerable<string> GameyaViolations(IEnumerable<Type> types) =>
        types.Where(IsRequest)
            .Where(t => t.IsDefined(typeof(RequiresGameyaMemberAttribute), inherit: false) ||
                        t.IsDefined(typeof(RequiresGameyaOrganizerAttribute), inherit: false))
            .Where(t => !typeof(IGameyaRequest).IsAssignableFrom(t))
            .Select(t => $"{t.FullName} is gam'eya-scoped but does not implement IGameyaRequest");

    [Fact]
    public void Every_request_has_exactly_one_access_attribute()
    {
        var violations = AccessViolations(typeof(DependencyInjection).Assembly.GetTypes()).ToList();
        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    public void Every_request_is_a_command_or_query()
    {
        var violations = KindViolations(typeof(DependencyInjection).Assembly.GetTypes()).ToList();
        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [Fact]
    public void Every_gameya_scoped_request_implements_IGameyaRequest()
    {
        var violations = GameyaViolations(typeof(DependencyInjection).Assembly.GetTypes()).ToList();
        violations.ShouldBeEmpty(string.Join("; ", violations));
    }

    [RequiresGameyaMember]
    private sealed record ScopedWithId(Guid GameyaId) : IQuery<int>, IGameyaRequest;

    [RequiresGameyaOrganizer]
    private sealed record ScopedWithoutId : ICommand<int>;

    [RequiresGameyaMember]
    private sealed record MemberScopedWithoutId : IQuery<int>;

    [Fact]
    public void Gameya_check_detects_scoped_requests_without_IGameyaRequest()
    {
        GameyaViolations([typeof(ScopedWithId), typeof(ScopedWithoutId), typeof(MemberScopedWithoutId), typeof(Ok)])
            .Count().ShouldBe(2);
    }

    [PublicAccess, RequiresAuth]
    private sealed record Two : IQuery<int>;

    private sealed record None : IQuery<int>;

    [PublicAccess]
    private sealed record Ok : ICommand<int>;

    private sealed record NonGenericNoAttribute : IRequest;

    [PublicAccess]
    private sealed record BareRequest : IRequest<int>;

    [Fact]
    public void Access_check_detects_missing_and_duplicate_attributes_including_non_generic_requests()
    {
        AccessViolations([typeof(Two), typeof(None), typeof(Ok), typeof(NonGenericNoAttribute)]).Count().ShouldBe(3);
    }

    [Fact]
    public void Kind_check_detects_requests_that_are_neither_command_nor_query()
    {
        KindViolations([typeof(Ok), typeof(None), typeof(BareRequest), typeof(NonGenericNoAttribute)]).Count()
            .ShouldBe(2);
    }
}
