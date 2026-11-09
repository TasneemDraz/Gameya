using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gameya.ArchitectureTests;

public class DataAccessTests
{
    private static readonly string[] ForbiddenQueryableNames =
    [
        "Where", "Select", "SelectMany", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "GroupBy", "Join", "Skip", "Take", "Distinct",
        "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault",
        "ElementAt", "ElementAtOrDefault", "Any", "All", "Count", "LongCount", "Contains",
        "Min", "Max", "Sum", "Average"
    ];

    // EF Core 10 declares these on EntityFrameworkQueryableExtensions (in addition to every *Async).
    private static readonly string[] ForbiddenEfNames =
        ["Include", "ThenInclude", "AsNoTracking", "AsTracking", "AsNoTrackingWithIdentityResolution",
         "ExecuteUpdate", "ExecuteDelete"];

    [Fact]
    public void Handlers_do_not_query_DbSets_directly()
    {
        var path = Assembly.Load("Gameya.Application").Location;
        var offenders = FindOffenders(path, _ => true);
        offenders.ShouldBeEmpty("Handlers must read through specification extensions. Offenders: "
            + string.Join("; ", offenders));
    }

    private static List<string> Scan(string typeName) =>
        FindOffenders(typeof(DataAccessTests).Assembly.Location, t => t.Name == typeName);

    [Fact]
    public void Scanner_detects_a_handler_that_queries_directly() => Scan(nameof(BadHandler)).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_detects_an_async_bad_handler() => Scan(nameof(AsyncBadHandler)).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_detects_a_lambda_wrapped_bad_handler() => Scan(nameof(LambdaBadHandler)).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_detects_a_bad_handler_that_inherits_the_handler_interface_from_a_base_class() =>
        Scan(nameof(DerivedBadHandler)).ShouldNotBeEmpty();

    [Fact]
    public void Scanner_detects_Find_Include_and_ExecuteDelete_on_DbSets()
    {
        Scan(nameof(FindHandler)).ShouldNotBeEmpty();
        Scan(nameof(IncludeHandler)).ShouldNotBeEmpty();
        Scan(nameof(ExecuteDeleteHandler)).ShouldNotBeEmpty();
        Scan(nameof(MinHandler)).ShouldNotBeEmpty();
    }

    [Fact]
    public void Scanner_ignores_handlers_that_only_add_and_remove() => Scan(nameof(GoodHandler)).ShouldBeEmpty();

    internal sealed record BadRequest : MediatR.IRequest<int>;

    internal sealed record Item(int Id);

    internal sealed class BadHandler : MediatR.IRequestHandler<BadRequest, int>
    {
        public Task<int> Handle(BadRequest request, CancellationToken cancellationToken)
        {
            IQueryable<int> set = new[] { 1, 2 }.AsQueryable();
            return Task.FromResult(set.Where(x => x > 1).Count());
        }
    }

    internal sealed class AsyncBadHandler : MediatR.IRequestHandler<BadRequest, int>
    {
        public async Task<int> Handle(BadRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(1, cancellationToken);
            IQueryable<int> set = new[] { 1, 2 }.AsQueryable();
            return set.Count(x => x > 1);
        }
    }

    internal sealed class LambdaBadHandler : MediatR.IRequestHandler<BadRequest, int>
    {
        public Task<int> Handle(BadRequest request, CancellationToken cancellationToken)
        {
            IQueryable<int> set = new[] { 1, 2 }.AsQueryable();
            var local = request.GetHashCode();
            Func<int> run = () => set.Where(x => x > local).Count();
            return Task.Run(run, cancellationToken);
        }
    }

    internal abstract class BaseHandler<TRequest> : MediatR.IRequestHandler<TRequest, int>
        where TRequest : MediatR.IRequest<int>
    {
        public abstract Task<int> Handle(TRequest request, CancellationToken cancellationToken);
    }

    internal sealed class DerivedBadHandler : BaseHandler<BadRequest>
    {
        public override Task<int> Handle(BadRequest request, CancellationToken cancellationToken)
        {
            IQueryable<int> set = new[] { 1, 2 }.AsQueryable();
            return Task.FromResult(set.Where(x => x > 1).Count());
        }
    }

    internal sealed class FindHandler(DbSet<Item> items) : MediatR.IRequestHandler<BadRequest, int>
    {
        public async Task<int> Handle(BadRequest request, CancellationToken cancellationToken)
        {
            var found = await items.FindAsync([1], cancellationToken);
            return found is null ? 0 : items.Find(1)!.Id;
        }
    }

    internal sealed class IncludeHandler(DbSet<Item> items) : MediatR.IRequestHandler<BadRequest, int>
    {
        public Task<int> Handle(BadRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(items.AsNoTracking().Include("x").GetHashCode());
    }

    internal sealed class ExecuteDeleteHandler(DbSet<Item> items) : MediatR.IRequestHandler<BadRequest, int>
    {
        public Task<int> Handle(BadRequest request, CancellationToken cancellationToken) =>
            items.ExecuteDeleteAsync(cancellationToken);
    }

    internal sealed class MinHandler : MediatR.IRequestHandler<BadRequest, int>
    {
        public Task<int> Handle(BadRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { 1 }.AsQueryable().Min());
    }

    internal sealed class GoodHandler : MediatR.IRequestHandler<BadRequest, int>
    {
        public Task<int> Handle(BadRequest request, CancellationToken cancellationToken)
        {
            var list = new List<int>();
            list.Add(1);
            list.Remove(1);
            return Task.FromResult(list.Count);
        }
    }

    private static List<string> FindOffenders(string assemblyPath, Func<TypeDefinition, bool> handlerFilter)
    {
        var offenders = new List<string>();
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
        using var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { AssemblyResolver = resolver });

        foreach (var type in module.GetTypes())
        {
            // A type is scanned if it, or an enclosing type, is a handler (covers async state machines and closures).
            var handler = type;
            while (handler is not null && !IsHandler(handler))
            {
                handler = handler.DeclaringType;
            }

            if (handler is null || !handlerFilter(handler))
            {
                continue;
            }

            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt))
                    {
                        continue;
                    }

                    if (instruction.Operand is MethodReference called && IsForbidden(called))
                    {
                        offenders.Add($"{type.FullName}.{method.Name} -> {called.DeclaringType.Name}.{called.Name}");
                    }
                }
            }
        }

        return offenders;
    }

    /// <summary>True if the type, any base type, or any inherited interface is MediatR's IRequestHandler.</summary>
    private static bool IsHandler(TypeDefinition type)
    {
        for (var t = type; t is not null; t = TryResolve(t.BaseType))
        {
            if (t.Interfaces.Any(i => IsHandlerInterface(i.InterfaceType)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHandlerInterface(TypeReference type, int depth = 0)
    {
        if (type.GetElementType().FullName is "MediatR.IRequestHandler`2" or "MediatR.IRequestHandler`1")
        {
            return true;
        }

        return depth < 8 && TryResolve(type) is { } def &&
               def.Interfaces.Any(i => IsHandlerInterface(i.InterfaceType, depth + 1));
    }

    private static TypeDefinition? TryResolve(TypeReference? type)
    {
        if (type is null)
        {
            return null;
        }

        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    private static bool IsForbidden(MethodReference called)
    {
        var declaring = called.DeclaringType.GetElementType().FullName;
        return declaring switch
        {
            "System.Linq.Queryable" => ForbiddenQueryableNames.Contains(called.Name),
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions" =>
                called.Name.EndsWith("Async", StringComparison.Ordinal) || StartsWithAny(called.Name, ForbiddenEfNames),
            "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions" =>
                StartsWithAny(called.Name, ForbiddenEfNames),
            "Microsoft.EntityFrameworkCore.DbSet`1" => called.Name is "Find" or "FindAsync",
            _ => false
        };
    }

    private static bool StartsWithAny(string name, string[] prefixes) =>
        prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
}
