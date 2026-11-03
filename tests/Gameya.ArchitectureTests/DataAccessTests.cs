using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Gameya.ArchitectureTests;

public class DataAccessTests
{
    private static readonly string[] ForbiddenQueryableNames =
        ["Where", "Select", "SelectMany", "OrderBy", "OrderByDescending", "GroupBy", "Join", "Skip", "Take",
         "First", "FirstOrDefault", "Single", "SingleOrDefault", "Any", "All", "Count", "LongCount", "Contains"];

    [Fact]
    public void Handlers_do_not_query_DbSets_directly()
    {
        var path = Assembly.Load("Gameya.Application").Location;
        var offenders = FindOffenders(path, _ => true);
        offenders.ShouldBeEmpty("Handlers must read through specification extensions. Offenders: "
            + string.Join("; ", offenders));
    }

    [Fact]
    public void Scanner_detects_a_handler_that_queries_directly()
    {
        var path = typeof(DataAccessTests).Assembly.Location;
        var offenders = FindOffenders(path, t => t.Name == nameof(BadHandler));
        offenders.ShouldNotBeEmpty();
    }

    [Fact]
    public void Scanner_ignores_handlers_that_only_add_and_remove()
    {
        var path = typeof(DataAccessTests).Assembly.Location;
        FindOffenders(path, t => t.Name == nameof(GoodHandler)).ShouldBeEmpty();
    }

    internal sealed class BadHandler : MediatR.IRequestHandler<BadRequest, int>
    {
        public Task<int> Handle(BadRequest request, CancellationToken cancellationToken)
        {
            IQueryable<int> set = new[] { 1, 2 }.AsQueryable();
            return Task.FromResult(set.Where(x => x > 1).Count());
        }
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

    internal sealed record BadRequest : MediatR.IRequest<int>;

    private static List<string> FindOffenders(string assemblyPath, Func<TypeDefinition, bool> handlerFilter)
    {
        var offenders = new List<string>();
        using var module = ModuleDefinition.ReadModule(assemblyPath);

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

    private static bool IsHandler(TypeDefinition type) =>
        type.Interfaces.Any(i =>
            i.InterfaceType.GetElementType().FullName is "MediatR.IRequestHandler`2" or "MediatR.IRequestHandler`1");

    private static bool IsForbidden(MethodReference called)
    {
        var declaring = called.DeclaringType.FullName;
        if (declaring == "System.Linq.Queryable")
        {
            return ForbiddenQueryableNames.Contains(called.Name);
        }

        return declaring == "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
               && called.Name.EndsWith("Async", StringComparison.Ordinal);
    }
}
