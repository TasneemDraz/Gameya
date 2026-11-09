using System.Reflection;
using Gameya.Infrastructure.Time;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace Gameya.ArchitectureTests;

public class LayerTests
{
    private const string DomainNs = "Gameya.Domain";
    private const string ApplicationNs = "Gameya.Application";
    private const string InfrastructureNs = "Gameya.Infrastructure";
    private const string ApiNs = "Gameya.Api";

    private static readonly string[] SourceAssemblies = [DomainNs, ApplicationNs, InfrastructureNs, ApiNs];

    private static Assembly Load(string name) => Assembly.Load(name);

    [Fact]
    public void Domain_has_no_dependency_on_other_layers()
    {
        var result = Types.InAssembly(Load(DomainNs))
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNs, InfrastructureNs, ApiNs, "Microsoft.EntityFrameworkCore", "MediatR")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_Api()
    {
        var result = Types.InAssembly(Load(ApplicationNs))
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNs, ApiNs)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Only_SystemClock_reads_the_system_time()
    {
        var allowedType = typeof(SystemClock).FullName!;
        var offenders = new List<string>();

        foreach (var assemblyName in SourceAssemblies)
        {
            var path = Load(assemblyName).Location;
            using var module = ModuleDefinition.ReadModule(path);

            foreach (var type in module.GetTypes())
            {
                if (IsSameOrNestedIn(type, allowedType))
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

                        if (instruction.Operand is MethodReference called && IsForbiddenTimeRead(called))
                        {
                            offenders.Add($"{type.FullName}.{method.Name} -> {called.DeclaringType.Name}.{called.Name}");
                        }
                    }
                }
            }
        }

        offenders.ShouldBeEmpty("Only SystemClock may read the system time. Offenders: "
            + string.Join("; ", offenders));
    }

    private static bool IsSameOrNestedIn(TypeDefinition type, string allowedFullName)
    {
        for (var t = type; t is not null; t = t.DeclaringType)
        {
            if (t.FullName == allowedFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForbiddenTimeRead(MethodReference called) =>
        (called.DeclaringType.FullName, called.Name) is
            ("System.DateTime", "get_Now") or
            ("System.DateTime", "get_UtcNow") or
            ("System.DateTimeOffset", "get_UtcNow");

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null ? string.Empty : "Failing types: " + string.Join(", ", result.FailingTypeNames);
}
