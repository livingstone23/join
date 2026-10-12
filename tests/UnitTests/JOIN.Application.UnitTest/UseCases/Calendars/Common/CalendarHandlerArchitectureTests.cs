using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Common;

/// <summary>
/// SPEC 44 F10 — architecture guard.
///
/// Enforces the module isolation guarantee: every command handler (and any helper, coordinator
/// or writer invoked by one) under <c>JOIN.Application.UseCases.Calendars</c> may only persist
/// changes through <c>IUnitOfWork.GetRepository&lt;T&gt;()</c> when <c>T</c> belongs to the
/// <c>JOIN.Domain.Calendars</c> namespace. Reads of <c>JOIN.Domain.Security</c>,
/// <c>JOIN.Domain.Admin</c>, <c>JOIN.Domain.Messaging</c> and <c>JOIN.Domain.Common</c>
/// types (e.g. <c>ApplicationUser</c>, <c>UserCompany</c>, <c>Person</c>, <c>Customer</c>,
/// <c>Ticket</c>, <c>Project</c>, <c>CommunicationChannel</c>, <c>SystemModule</c>,
/// <c>CompanyModule</c>) are explicitly allowed by the spec ("salvo para lectura") and would
/// fail this test only if paired with a write method (InsertAsync / UpdateAsync).
///
/// <para>
/// Approach: source-level static analysis. Two patterns are inspected on every <c>.cs</c> file
/// under <c>src/2.Application/UseCases/Calendars/</c>:
/// </para>
/// <list type="number">
///   <item>The inline chain <c>unitOfWork.GetRepository&lt;T&gt;().InsertAsync/UpdateAsync</c>.</item>
///   <item>The bind-then-call form — <c>var x = unitOfWork.GetRepository&lt;T&gt;(); ...</c>
///   followed later by <c>x.InsertAsync/UpdateAsync</c> in the same file.</item>
/// </list>
/// <para>
/// Each <c>T</c> is resolved through the loaded <c>JOIN.Domain</c> assembly, preferring
/// namespaces reachable through a <c>using JOIN.Domain.X;</c> directive in the file. Fails with
/// a friendly report listing each violation as <c>file:line, type, method</c>.
/// </para>
/// </summary>
public class CalendarHandlerArchitectureTests
{
    private const string CalendarsNamespace = "JOIN.Domain.Calendars";
    private const string DomainRoot = "JOIN.Domain";

    private static readonly Regex InlineWriteChainPattern = new(
        @"\.GetRepository\s*<\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*>\s*\([^)]*\)\s*\.\s*(?<method>InsertAsync|UpdateAsync)\b",
        RegexOptions.Compiled);

    private static readonly Regex RepositoryBindingPattern = new(
        @"^\s*(?:var|[\w<>\s,]+)\s+(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*=[^;\n]*?\.GetRepository\s*<\s*(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*>\s*\([^)]*\)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex VariableWriteCallPattern = new(
        @"\b(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<method>InsertAsync|UpdateAsync)\b",
        RegexOptions.Compiled);

    private static readonly Regex UsingDomainNamespacePattern = new(
        @"^\s*using\s+JOIN\.Domain\.(?<ns>[A-Za-z0-9_.]+)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void CalendarCommandHandlers_MustOnlyWriteToCalendarsDomain()
    {
        var calendarsRoot = ResolveCalendarsRoot();
        calendarsRoot.Should().NotBeNull(
            "the repo source tree must contain src/2.Application/UseCases/Calendars/ next to the test project.");

        var domainTypes = LoadDomainTypes();
        var files = Directory.EnumerateFiles(calendarsRoot!, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var violations = new List<string>();
        var inspectedFiles = 0;

        foreach (var file in files)
        {
            inspectedFiles++;
            var relativePath = Path.GetRelativePath(calendarsRoot!, file);
            var content = File.ReadAllText(file);
            var lineOffsets = BuildLineOffsetIndex(content);
            var usingNamespaces = ExtractDomainNamespaces(content);

            // Pass 1: inline chain `GetRepository<T>().InsertAsync/UpdateAsync`.
            foreach (Match match in InlineWriteChainPattern.Matches(content))
            {
                var typeName = match.Groups["type"].Value;
                var writeMethod = match.Groups["method"].Value;
                ReportIfNonCalendars(
                    file, relativePath, match.Index, content, lineOffsets,
                    typeName, writeMethod, usingNamespaces, domainTypes, violations);
            }

            // Pass 2: bind `var x = ...GetRepository<T>();` then call `x.InsertAsync/UpdateAsync`.
            // Bindings are kept in source order; for each call we use the most recent binding of
            // that variable name so reassignments are respected.
            var bindings = new List<(string VarName, string TypeName, int Offset)>();
            foreach (Match binding in RepositoryBindingPattern.Matches(content))
            {
                bindings.Add((binding.Groups["var"].Value, binding.Groups["type"].Value, binding.Index));
            }

            if (bindings.Count == 0)
            {
                continue;
            }

            foreach (Match call in VariableWriteCallPattern.Matches(content))
            {
                var varName = call.Groups["var"].Value;
                var writeMethod = call.Groups["method"].Value;

                // Find the most recent binding for `varName` whose offset is BEFORE the call.
                string? resolvedType = null;
                var matchingBindingCount = 0;
                for (var i = bindings.Count - 1; i >= 0; i--)
                {
                    if (!string.Equals(bindings[i].VarName, varName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    matchingBindingCount++;
                    if (bindings[i].Offset < call.Index)
                    {
                        resolvedType = bindings[i].TypeName;
                        break;
                    }
                }

                if (resolvedType is null)
                {
                    continue;
                }

                ReportIfNonCalendars(
                    file, relativePath, call.Index, content, lineOffsets,
                    resolvedType, writeMethod, usingNamespaces, domainTypes, violations);
            }
        }

        violations.Should().BeEmpty(
            "writes through IUnitOfWork.GetRepository<T>() inside " +
            "JOIN.Application.UseCases.Calendars must target an entity in " +
            $"'{CalendarsNamespace}' (SPEC 44 module isolation). " +
            $"Inspected {inspectedFiles} files under src/2.Application/UseCases/Calendars/." +
            (violations.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}"));
    }

    private static void ReportIfNonCalendars(
        string file,
        string relativePath,
        int callOffset,
        string content,
        int[] lineOffsets,
        string typeName,
        string writeMethod,
        HashSet<string> usingNamespaces,
        IReadOnlyDictionary<string, Type[]> domainTypes,
        List<string> violations)
    {
        var type = ResolveDomainType(typeName, usingNamespaces, domainTypes);
        if (type is null)
        {
            // Type is not in JOIN.Domain (e.g. a DTO or entity under another assembly). Out of scope
            // for this guard: the spec guards module isolation, not DTO composition.
            return;
        }

        if (type.Namespace == CalendarsNamespace)
        {
            return;
        }

        var lineNumber = LocateLine(lineOffsets, callOffset);
        violations.Add(
            $"  - {relativePath}:{lineNumber}  GetRepository<{typeName}>().{writeMethod}  ->  {type.FullName}");
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> until it finds the Calendars use-cases folder.
    /// Works whether the test runner is invoked from the repo root or the test project directory.
    /// </summary>
    private static string? ResolveCalendarsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "2.Application", "UseCases", "Calendars");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            // Fallback for unusual bin layouts: walk until we find a Calendars use-cases folder.
            if (string.Equals(dir.Name, "Calendars", StringComparison.Ordinal)
                && string.Equals(dir.Parent?.Name, "UseCases", StringComparison.Ordinal))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }
        return null;
    }

    private static IReadOnlyDictionary<string, Type[]> LoadDomainTypes()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "JOIN.Domain", StringComparison.Ordinal))
            ?? Assembly.Load(new AssemblyName("JOIN.Domain"));

        return assembly.GetTypes()
            .Where(t => !t.IsNested && t.Namespace?.StartsWith(DomainRoot, StringComparison.Ordinal) == true)
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    private static HashSet<string> ExtractDomainNamespaces(string content)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in UsingDomainNamespacePattern.Matches(content))
        {
            namespaces.Add(match.Groups["ns"].Value);
        }
        return namespaces;
    }

    private static Type? ResolveDomainType(
        string typeName,
        HashSet<string> usingNamespaces,
        IReadOnlyDictionary<string, Type[]> candidates)
    {
        if (!candidates.TryGetValue(typeName, out var matches) || matches.Length == 0)
        {
            return null;
        }

        if (matches.Length == 1)
        {
            return matches[0];
        }

        return matches
            .OrderByDescending(t => IsImported(t, usingNamespaces))
            .ThenByDescending(t => (t.Namespace?.Length ?? 0))
            .First();
    }

    private static bool IsImported(Type type, HashSet<string> usingNamespaces)
    {
        var ns = type.Namespace;
        if (ns is null)
        {
            return false;
        }

        var prefix = DomainRoot + ".";
        if (!ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var leaf = ns[prefix.Length..];
        return usingNamespaces.Any(u => string.Equals(u, leaf, StringComparison.Ordinal))
               || leaf.Contains('.')
               && usingNamespaces.Any(u => leaf.StartsWith(u + ".", StringComparison.Ordinal));
    }

    private static int[] BuildLineOffsetIndex(string content)
    {
        var offsets = new List<int>(capacity: 256) { 0 };
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                offsets.Add(i + 1);
            }
        }
        return offsets.ToArray();
    }

    private static int LocateLine(int[] lineStartOffsets, int characterOffset)
    {
        // lineStartOffsets is sorted ascending; the line that contains `characterOffset` is the
        // index of the largest offset that is <= characterOffset.
        var lo = 0;
        var hi = lineStartOffsets.Length - 1;
        var result = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lineStartOffsets[mid] <= characterOffset)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result + 1;
    }
}
