using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 44 — architecture test ("Aislamiento del módulo"): the code under <c>UseCases.Calendars</c> (handlers,
/// coordinators and services, including the compiler-generated async state machines) never inserts or updates an
/// entity outside <c>JOIN.Domain.Calendars</c>, nor creates one. CRM and security entities are read only. Scans the IL
/// for <c>call</c>/<c>callvirt</c>/<c>newobj</c> instructions.
/// </summary>
public sealed class CalendarModuleIsolationTests
{
    private const string ModuleNamespace = "JOIN.Application.UseCases.Calendars";
    private const string CalendarDomainNamespace = "JOIN.Domain.Calendars";

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    [Fact]
    public void CalendarCode_ShouldOnlyWriteCalendarEntities()
    {
        var types = typeof(CalendarEventCommandProcessor).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith(ModuleNamespace, StringComparison.Ordinal) == true)
            .ToList();
        types.Should().NotBeEmpty();

        var violations = new List<string>();
        var scannedWrites = 0;

        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                         .Cast<MethodBase>()
                         .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)))
            {
                foreach (var called in CalledMembers(method))
                {
                    var declaring = called.DeclaringType;
                    if (declaring is null)
                    {
                        continue;
                    }

                    if (declaring.IsGenericType
                        && declaring.GetGenericTypeDefinition() == typeof(IGenericRepository<>)
                        && called.Name is nameof(IGenericRepository<object>.InsertAsync) or nameof(IGenericRepository<object>.UpdateAsync))
                    {
                        scannedWrites++;
                        var entity = declaring.GetGenericArguments()[0];
                        if (entity.Namespace != CalendarDomainNamespace)
                        {
                            violations.Add($"{type.FullName}.{method.Name} writes {entity.Name} through {called.Name}");
                        }
                    }

                    if (called is ConstructorInfo
                        && declaring.Namespace?.StartsWith("JOIN.Domain", StringComparison.Ordinal) == true
                        && declaring.Namespace != CalendarDomainNamespace
                        && !declaring.IsValueType)
                    {
                        violations.Add($"{type.FullName}.{method.Name} creates {declaring.Name}");
                    }
                }
            }
        }

        scannedWrites.Should().BeGreaterThan(20, "the scan must actually see the calendar writes");
        violations.Should().BeEmpty();
    }

    private static IEnumerable<MethodBase> CalledMembers(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var typeArgs = method.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var position = 0;

        while (position < il.Length)
        {
            short value = il[position];
            if (value == 0xFE && position + 1 < il.Length)
            {
                value = (short)(0xFE00 | il[position + 1]);
                position += 2;
            }
            else
            {
                position += 1;
            }

            if (!OpCodesByValue.TryGetValue(value, out var opCode))
            {
                yield break;
            }

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, position);
                MethodBase? resolved = null;
                try
                {
                    resolved = method.Module.ResolveMethod(token, typeArgs, methodArgs);
                }
                catch (ArgumentException)
                {
                }

                if (resolved is not null)
                {
                    yield return resolved;
                }
            }

            position += OperandSize(opCode, il, position);
        }
    }

    private static int OperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, position) * 4,
        _ => 4
    };
}
