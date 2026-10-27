using Hypershell.Models;
using System.IO;
using System.Reflection;

namespace Hypershell.Services;

public enum DotNetApiMemberKind
{
    Constructor,
    Method,
    Property,
    Field,
    Event
}

public enum DotNetApiOperation
{
    Invoke,
    Read,
    Write,
    Subscribe,
    Unsubscribe
}

public sealed class DotNetApiIndexService
{
    private readonly object _taskGate = new();
    private readonly Dictionary<string, Lazy<Task<DotNetApiIndex>>> _indexTasks =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<DotNetApiIndex> LoadAsync(CommandLibrary frameworkLibrary)
    {
        ArgumentNullException.ThrowIfNull(frameworkLibrary);
        if (!CSharpFrameworkCatalogService.IsIndexedManagedApiLibrary(
                frameworkLibrary))
        {
            throw new ArgumentException(
                "The library must describe an installed managed API surface.",
                nameof(frameworkLibrary));
        }

        lock (_taskGate)
        {
            if (!_indexTasks.TryGetValue(
                    frameworkLibrary.Id,
                    out var indexTask))
            {
                indexTask = new Lazy<Task<DotNetApiIndex>>(
                    () => Task.Run(() => BuildIndex(frameworkLibrary)));
                _indexTasks[frameworkLibrary.Id] = indexTask;
            }

            return indexTask.Value;
        }
    }

    private static DotNetApiIndex BuildIndex(CommandLibrary frameworkLibrary)
    {
        var referenceDirectory = frameworkLibrary.SourcePath;
        var primaryPaths = frameworkLibrary.ApiAssemblyPaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (primaryPaths.Count == 0 && Directory.Exists(referenceDirectory))
        {
            primaryPaths = Directory.EnumerateFiles(
                    referenceDirectory,
                    "*.dll",
                    SearchOption.AllDirectories)
                .ToList();
        }

        if (primaryPaths.Count == 0)
        {
            throw new FileNotFoundException(
                $"No managed assemblies were found for {frameworkLibrary.DisplayName}.");
        }

        var runtimeDirectory = Path.GetDirectoryName(
                                   typeof(object).Assembly.Location)
                               ?? string.Empty;
        var assemblies = new List<Assembly>();
        var referencePaths = BuildResolverPaths(
            primaryPaths,
            frameworkLibrary.ResolverAssemblyPaths);
        var coreAssemblyName = !string.IsNullOrWhiteSpace(
            frameworkLibrary.MetadataCoreAssemblyName)
            ? frameworkLibrary.MetadataCoreAssemblyName
            : referencePaths.Any(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                "mscorlib",
                StringComparison.OrdinalIgnoreCase))
                ? "mscorlib"
                : "System.Runtime";
        var metadataContext = new MetadataLoadContext(
            new PathAssemblyResolver(referencePaths),
            coreAssemblyName);
        foreach (var path in primaryPaths)
        {
            try
            {
                assemblies.Add(
                    metadataContext.LoadFromAssemblyPath(path));
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
            catch (InvalidOperationException)
            {
                // Some installed reference folders contain metadata-only
                // modules or native interop stubs alongside CLR assemblies.
            }
        }

        var typeByName = new Dictionary<string, Type>(
            StringComparer.Ordinal);
        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in GetExportedTypesSafely(assembly))
            {
                if (string.IsNullOrWhiteSpace(type.Namespace)
                    || string.IsNullOrWhiteSpace(type.FullName)
                    || (!type.IsPublic && !type.IsNestedPublic)
                    || type.Name.StartsWith('<'))
                {
                    continue;
                }

                typeByName.TryAdd(type.FullName, type);
            }
        }

        var types = typeByName.Values
            .Select(type => new DotNetApiTypeDefinition(type))
            .OrderBy(type => type.NamespaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(type => type.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new DotNetApiIndex(
            frameworkLibrary.Id,
            CreateRuntimeCatalogId(frameworkLibrary),
            frameworkLibrary.DisplayName,
            frameworkLibrary.TargetFramework,
            frameworkLibrary.DisplayName,
            frameworkLibrary.FrameworkVersion,
            runtimeDirectory,
            referenceDirectory,
            assemblies
                .Select(assembly => assembly.GetName().Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            types,
            metadataContext);
    }

    private static List<string> BuildResolverPaths(
        IEnumerable<string> primaryPaths,
        IEnumerable<string> dependencyPaths)
    {
        var pathsByAssemblyName = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in primaryPaths.Concat(dependencyPaths))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var name = AssemblyName.GetAssemblyName(path).Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    pathsByAssemblyName.TryAdd(name, path);
                }
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
        }

        return pathsByAssemblyName.Values.ToList();
    }

    private static string CreateRuntimeCatalogId(
        CommandLibrary frameworkLibrary) =>
        $"{frameworkLibrary.Id}-runtime-api";

    private static IEnumerable<Type> GetExportedTypesSafely(
        Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types
                .Where(type => type is not null)
                .Cast<Type>();
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (TypeLoadException)
        {
            return [];
        }
    }
}

public sealed class DotNetApiIndex
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<DotNetApiTypeDefinition>>
        _typesByNamespace;
    private readonly IReadOnlyDictionary<string, DotNetApiTypeDefinition>
        _typesByCoordinate;
    private readonly IReadOnlyList<string> _namespaces;
    private readonly IReadOnlyList<DotNetApiSearchEntry> _searchEntries;
    private readonly MetadataLoadContext? _metadataContext;

    internal DotNetApiIndex(
        string libraryId,
        string runtimeCatalogId,
        string displayName,
        string targetFramework,
        string frameworkDescription,
        string runtimeVersion,
        string runtimeDirectory,
        string referenceDirectory,
        int assemblyCount,
        IReadOnlyList<DotNetApiTypeDefinition> types,
        MetadataLoadContext? metadataContext)
    {
        LibraryId = libraryId;
        RuntimeCatalogId = runtimeCatalogId;
        DisplayName = displayName;
        TargetFramework = targetFramework;
        FrameworkDescription = frameworkDescription;
        RuntimeVersion = runtimeVersion;
        RuntimeDirectory = runtimeDirectory;
        ReferenceDirectory = referenceDirectory;
        AssemblyCount = assemblyCount;
        Types = types;
        _metadataContext = metadataContext;
        _typesByNamespace = types
            .GroupBy(type => type.NamespaceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DotNetApiTypeDefinition>)group
                    .OrderBy(type => type.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        _typesByCoordinate = types
            .GroupBy(
                type => BuildCoordinate(type.NamespaceName, type.DisplayName),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        _namespaces = _typesByNamespace.Keys
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var searchEntries = new List<DotNetApiSearchEntry>();
        var overloadCount = 0;
        foreach (var type in types)
        {
            foreach (var member in type.MemberGroups)
            {
                overloadCount += member.Overloads.Count;
                searchEntries.Add(new DotNetApiSearchEntry(
                    LibraryId,
                    DisplayName,
                    TargetFramework,
                    type,
                    member));
            }
        }

        OverloadCount = overloadCount;
        MemberGroupCount = searchEntries.Count;
        _searchEntries = searchEntries;
    }

    public string LibraryId { get; }
    public string RuntimeCatalogId { get; }
    public string DisplayName { get; }
    public string TargetFramework { get; }
    public string FrameworkDescription { get; }
    public string RuntimeVersion { get; }
    public string RuntimeDirectory { get; }
    public string ReferenceDirectory { get; }
    public int AssemblyCount { get; }
    public int OverloadCount { get; }
    public int MemberGroupCount { get; }
    public IReadOnlyList<DotNetApiTypeDefinition> Types { get; }
    public int NamespaceCount => _namespaces.Count;

    public IReadOnlyList<DotNetNamespaceDefinition> GetNamespaceChildren(
        string? parentPath)
    {
        var normalizedParent = parentPath?.Trim().Trim('.') ?? string.Empty;
        var prefix = string.IsNullOrWhiteSpace(normalizedParent)
            ? string.Empty
            : $"{normalizedParent}.";
        var childSegments = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var namespaceName in _namespaces)
        {
            if (!namespaceName.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = namespaceName[prefix.Length..];
            var separator = remainder.IndexOf('.');
            var segment = separator >= 0
                ? remainder[..separator]
                : remainder;
            if (!string.IsNullOrWhiteSpace(segment))
            {
                childSegments.Add(segment);
            }
        }

        return childSegments
            .Select(segment =>
            {
                var fullName = string.IsNullOrWhiteSpace(normalizedParent)
                    ? segment
                    : $"{normalizedParent}.{segment}";
                var descendantPrefix = $"{fullName}.";
                var directTypeCount = _typesByNamespace.TryGetValue(
                    fullName,
                    out var directTypes)
                    ? directTypes.Count
                    : 0;
                var descendantTypeCount = Types.Count(type =>
                    type.NamespaceName.StartsWith(
                        descendantPrefix,
                        StringComparison.OrdinalIgnoreCase));
                return new DotNetNamespaceDefinition(
                    segment,
                    fullName,
                    directTypeCount,
                    descendantTypeCount);
            })
            .OrderBy(option => option.Segment, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<DotNetApiTypeDefinition> GetTypes(
        string? namespaceName) =>
        !string.IsNullOrWhiteSpace(namespaceName)
        && _typesByNamespace.TryGetValue(namespaceName, out var types)
            ? types
            : [];

    public DotNetApiTypeDefinition? FindType(
        string? namespaceName,
        string? typeName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName)
            || string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return _typesByCoordinate.TryGetValue(
            BuildCoordinate(namespaceName, typeName),
            out var type)
            ? type
            : null;
    }

    public IReadOnlyList<DotNetApiSearchEntry> Search(
        string query,
        int take = 50)
    {
        var normalized = query.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var terms = normalized.Split(
            [' ', '\t', '\r', '\n', '.'],
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        return _searchEntries
            .Where(entry => terms.All(term =>
                entry.SearchText.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new
            {
                Entry = entry,
                Score = Score(entry, normalized)
            })
            .OrderBy(candidate => candidate.Score)
            .ThenBy(
                candidate => candidate.Entry.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, take))
            .Select(candidate => candidate.Entry)
            .ToList();
    }

    public CommandDefinition CreateCommand(
        DotNetApiTypeDefinition type,
        DotNetApiMemberGroup member,
        DotNetApiOverloadDefinition overload,
        ExecutionTarget executionTarget = ExecutionTarget.CSharp) =>
        DotNetApiCommandFactory.Create(
            RuntimeCatalogId,
            DisplayName,
            TargetFramework,
            type,
            member,
            overload,
            executionTarget);

    private static int Score(
        DotNetApiSearchEntry entry,
        string query)
    {
        if (string.Equals(
                entry.QualifiedMemberName,
                query,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                entry.DisplayName,
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (entry.QualifiedMemberName.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (entry.MemberName.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase)
            || entry.Type.DisplayName.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return entry.QualifiedMemberName.Contains(
            query,
            StringComparison.OrdinalIgnoreCase)
            ? 3
            : 4;
    }

    private static string BuildCoordinate(
        string namespaceName,
        string typeName) =>
        $"{namespaceName.Trim()}\u001F{NormalizeTypeName(typeName)}";

    private static string NormalizeTypeName(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
}

public sealed record DotNetNamespaceDefinition(
    string Segment,
    string FullName,
    int DirectTypeCount,
    int DescendantTypeCount)
{
    public int TotalTypeCount => DirectTypeCount + DescendantTypeCount;
    public string CountLabel =>
        $"{TotalTypeCount:N0} type{(TotalTypeCount == 1 ? string.Empty : "s")}";
}

public sealed class DotNetApiTypeDefinition
{
    private readonly Lazy<IReadOnlyList<DotNetApiMemberGroup>> _memberGroups;

    internal DotNetApiTypeDefinition(Type runtimeType)
    {
        RuntimeType = runtimeType;
        NamespaceName = runtimeType.Namespace ?? string.Empty;
        DisplayName = DotNetApiReflection.FormatTypeName(
            runtimeType,
            qualify: false);
        FullDisplayName = DotNetApiReflection.FormatTypeName(
            runtimeType,
            qualify: true);
        AssemblyName = runtimeType.Assembly.GetName().Name ?? "Unknown assembly";
        TypeKind = DotNetApiReflection.GetTypeKind(runtimeType);
        _memberGroups = new Lazy<IReadOnlyList<DotNetApiMemberGroup>>(
            () => DotNetApiReflection.BuildMemberGroups(runtimeType));
    }

    internal Type RuntimeType { get; }
    public string NamespaceName { get; }
    public string DisplayName { get; }
    public string FullDisplayName { get; }
    public string AssemblyName { get; }
    public string TypeKind { get; }
    public IReadOnlyList<DotNetApiMemberGroup> MemberGroups =>
        _memberGroups.Value;
    public string ContextLabel => $"{TypeKind} · {AssemblyName}";
}

public sealed class DotNetApiMemberGroup
{
    internal DotNetApiMemberGroup(
        string name,
        DotNetApiMemberKind kind,
        IReadOnlyList<DotNetApiOverloadDefinition> overloads)
    {
        Name = name;
        Kind = kind;
        Overloads = overloads;
    }

    public string Name { get; }
    public DotNetApiMemberKind Kind { get; }
    public IReadOnlyList<DotNetApiOverloadDefinition> Overloads { get; }
    public string DisplayName => Kind == DotNetApiMemberKind.Constructor
        ? "new"
        : Name;
    public bool HasStaticOverloads => Overloads.Any(overload =>
        overload.IsStatic);
    public bool HasInstanceOverloads => Overloads.Any(overload =>
        !overload.IsStatic);
    public string InvocationKindLabel => Kind switch
    {
        DotNetApiMemberKind.Constructor => "creates instance",
        _ when HasStaticOverloads && HasInstanceOverloads =>
            "static + instance",
        _ when HasStaticOverloads => "static",
        _ => "instance"
    };
    public string DetailLabel =>
        $"{Kind} · {InvocationKindLabel} · " +
        $"{Overloads.Count:N0} variation{(Overloads.Count == 1 ? string.Empty : "s")}";
}

public sealed class DotNetApiOverloadDefinition
{
    internal DotNetApiOverloadDefinition(
        MemberInfo member,
        DotNetApiOperation operation,
        string signature,
        bool isStatic)
    {
        Member = member;
        Operation = operation;
        Signature = signature;
        IsStatic = isStatic;
    }

    internal MemberInfo Member { get; }
    public DotNetApiOperation Operation { get; }
    public string Signature { get; }
    public bool IsStatic { get; }
    public string InvocationKindLabel => Member is ConstructorInfo
        ? "creates instance"
        : IsStatic
            ? "static"
            : "instance";
    public string SourceLabel =>
        $"{InvocationKindLabel} · {Operation.ToString().ToLowerInvariant()}";
}

public sealed class DotNetApiSearchEntry
{
    internal DotNetApiSearchEntry(
        string libraryId,
        string frameworkDisplayName,
        string targetFramework,
        DotNetApiTypeDefinition type,
        DotNetApiMemberGroup member)
    {
        LibraryId = libraryId;
        FrameworkDisplayName = frameworkDisplayName;
        TargetFramework = targetFramework;
        Type = type;
        Member = member;
        MemberName = member.DisplayName;
        QualifiedMemberName =
            $"{type.FullDisplayName}.{member.DisplayName}";
        DisplayName = $"{type.DisplayName}.{member.DisplayName}";
        ContextLabel =
            $"{frameworkDisplayName} · {type.NamespaceName} · {member.DetailLabel}";
        SearchText =
            $"{QualifiedMemberName} {DisplayName} {type.NamespaceName} " +
            $"{type.TypeKind} {type.AssemblyName} {member.Kind} " +
            $"{frameworkDisplayName} {targetFramework}";
    }

    public string LibraryId { get; }
    public string FrameworkDisplayName { get; }
    public string TargetFramework { get; }
    public DotNetApiTypeDefinition Type { get; }
    public DotNetApiMemberGroup Member { get; }
    public string MemberName { get; }
    public string QualifiedMemberName { get; }
    public string DisplayName { get; }
    public string ContextLabel { get; }
    public string SearchText { get; }
}

internal static class DotNetApiReflection
{
    private const BindingFlags DeclaredPublicMembers =
        BindingFlags.Public
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    private static readonly IReadOnlyDictionary<string, string> TypeAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Void"] = "void",
            ["System.Boolean"] = "bool",
            ["System.Byte"] = "byte",
            ["System.SByte"] = "sbyte",
            ["System.Int16"] = "short",
            ["System.UInt16"] = "ushort",
            ["System.Int32"] = "int",
            ["System.UInt32"] = "uint",
            ["System.Int64"] = "long",
            ["System.UInt64"] = "ulong",
            ["System.IntPtr"] = "nint",
            ["System.UIntPtr"] = "nuint",
            ["System.Single"] = "float",
            ["System.Double"] = "double",
            ["System.Decimal"] = "decimal",
            ["System.Char"] = "char",
            ["System.String"] = "string",
            ["System.Object"] = "object"
        };

    public static IReadOnlyList<DotNetApiMemberGroup> BuildMemberGroups(
        Type type)
    {
        var groups = new Dictionary<
            (DotNetApiMemberKind Kind, string Name),
            List<DotNetApiOverloadDefinition>>();

        try
        {
            foreach (var constructor in type.GetConstructors(
                         DeclaredPublicMembers))
            {
                Add(
                    groups,
                    DotNetApiMemberKind.Constructor,
                    ".ctor",
                    constructor,
                    DotNetApiOperation.Invoke,
                    FormatConstructorSignature(type, constructor),
                    isStatic: false);
            }

            foreach (var method in type.GetMethods(DeclaredPublicMembers)
                         .Where(method => !method.IsSpecialName))
            {
                Add(
                    groups,
                    DotNetApiMemberKind.Method,
                    method.Name,
                    method,
                    DotNetApiOperation.Invoke,
                    FormatMethodSignature(method),
                    method.IsStatic);
            }

            foreach (var property in type.GetProperties(
                         DeclaredPublicMembers))
            {
                var getter = property.GetMethod;
                if (getter?.IsPublic == true)
                {
                    Add(
                        groups,
                        DotNetApiMemberKind.Property,
                        property.Name,
                        property,
                        DotNetApiOperation.Read,
                        FormatPropertySignature(property, includeSetter: false),
                        getter.IsStatic);
                }

                var setter = property.SetMethod;
                if (setter?.IsPublic == true && !IsInitOnly(setter))
                {
                    Add(
                        groups,
                        DotNetApiMemberKind.Property,
                        property.Name,
                        property,
                        DotNetApiOperation.Write,
                        FormatPropertySignature(property, includeSetter: true),
                        setter.IsStatic);
                }
            }

            foreach (var field in type.GetFields(DeclaredPublicMembers))
            {
                Add(
                    groups,
                    DotNetApiMemberKind.Field,
                    field.Name,
                    field,
                    DotNetApiOperation.Read,
                    FormatFieldSignature(field, write: false),
                    field.IsStatic);
                if (!field.IsLiteral && !field.IsInitOnly)
                {
                    Add(
                        groups,
                        DotNetApiMemberKind.Field,
                        field.Name,
                        field,
                        DotNetApiOperation.Write,
                        FormatFieldSignature(field, write: true),
                        field.IsStatic);
                }
            }

            foreach (var eventInfo in type.GetEvents(
                         DeclaredPublicMembers))
            {
                var addMethod = eventInfo.AddMethod;
                if (addMethod?.IsPublic == true)
                {
                    Add(
                        groups,
                        DotNetApiMemberKind.Event,
                        eventInfo.Name,
                        eventInfo,
                        DotNetApiOperation.Subscribe,
                        FormatEventSignature(eventInfo, subscribe: true),
                        addMethod.IsStatic);
                }

                var removeMethod = eventInfo.RemoveMethod;
                if (removeMethod?.IsPublic == true)
                {
                    Add(
                        groups,
                        DotNetApiMemberKind.Event,
                        eventInfo.Name,
                        eventInfo,
                        DotNetApiOperation.Unsubscribe,
                        FormatEventSignature(eventInfo, subscribe: false),
                        removeMethod.IsStatic);
                }
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (TypeLoadException)
        {
        }

        return groups
            .Select(group => new DotNetApiMemberGroup(
                group.Key.Name,
                group.Key.Kind,
                group.Value
                    .OrderBy(overload => overload.Signature, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(group => group.Kind == DotNetApiMemberKind.Constructor ? 0 : 1)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Kind)
            .ToList();
    }

    public static string GetTypeKind(Type type)
    {
        if (type.IsInterface)
        {
            return "interface";
        }

        if (type.IsEnum)
        {
            return "enum";
        }

        if (string.Equals(
                type.BaseType?.FullName,
                "System.MulticastDelegate",
                StringComparison.Ordinal))
        {
            return "delegate";
        }

        if (type.IsValueType)
        {
            return "struct";
        }

        if (type.IsAbstract && type.IsSealed)
        {
            return "static class";
        }

        return type.IsAbstract ? "abstract class" : "class";
    }

    public static string FormatTypeName(
        Type type,
        bool qualify,
        IReadOnlyDictionary<Type, string>? genericReplacements = null)
    {
        if (genericReplacements is not null
            && genericReplacements.TryGetValue(type, out var replacement))
        {
            return replacement;
        }

        if (type.IsByRef || type.IsPointer)
        {
            var suffix = type.IsPointer ? "*" : string.Empty;
            return $"{FormatTypeName(
                type.GetElementType() ?? type,
                qualify,
                genericReplacements)}{suffix}";
        }

        if (type.IsArray)
        {
            var commas = new string(',', Math.Max(0, type.GetArrayRank() - 1));
            return $"{FormatTypeName(
                type.GetElementType() ?? type,
                qualify,
                genericReplacements)}[{commas}]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.FullName is not null
            && TypeAliases.TryGetValue(type.FullName, out var alias))
        {
            return alias;
        }

        if (type.IsGenericType
            && string.Equals(
                type.GetGenericTypeDefinition().FullName,
                "System.Nullable`1",
                StringComparison.Ordinal))
        {
            return $"{FormatTypeName(
                type.GetGenericArguments()[0],
                qualify,
                genericReplacements)}?";
        }

        var typeName = RemoveGenericArity(type.Name);
        var prefix = type.IsNested && type.DeclaringType is not null
            ? $"{FormatTypeName(
                type.DeclaringType,
                qualify,
                genericReplacements)}."
            : qualify && !string.IsNullOrWhiteSpace(type.Namespace)
                ? $"{type.Namespace}."
                : string.Empty;
        if (!type.IsGenericType)
        {
            return $"{prefix}{typeName}";
        }

        var declaringArgumentCount =
            type.IsNested && type.DeclaringType?.IsGenericType == true
                ? type.DeclaringType.GetGenericArguments().Length
                : 0;
        var arguments = type.GetGenericArguments()
            .Skip(declaringArgumentCount)
            .Select(argument => FormatTypeName(
                argument,
                qualify,
                genericReplacements))
            .ToList();
        return arguments.Count == 0
            ? $"{prefix}{typeName}"
            : $"{prefix}{typeName}<{string.Join(", ", arguments)}>";
    }

    public static string FormatPowerShellTypeName(
        Type type,
        bool qualify,
        IReadOnlyDictionary<Type, string>? genericReplacements = null)
    {
        if (genericReplacements is not null
            && genericReplacements.TryGetValue(type, out var replacement))
        {
            return replacement;
        }

        if (type.IsByRef || type.IsPointer)
        {
            var suffix = type.IsPointer ? "*" : string.Empty;
            return $"{FormatPowerShellTypeName(
                type.GetElementType() ?? type,
                qualify,
                genericReplacements)}{suffix}";
        }

        if (type.IsArray)
        {
            var commas = new string(
                ',',
                Math.Max(0, type.GetArrayRank() - 1));
            return $"{FormatPowerShellTypeName(
                type.GetElementType() ?? type,
                qualify,
                genericReplacements)}[{commas}]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.FullName is not null
            && TypeAliases.TryGetValue(type.FullName, out var alias))
        {
            return alias;
        }

        var typeName = RemoveGenericArity(type.Name);
        var prefix = type.IsNested && type.DeclaringType is not null
            ? $"{FormatPowerShellTypeName(
                type.DeclaringType,
                qualify,
                genericReplacements)}."
            : qualify && !string.IsNullOrWhiteSpace(type.Namespace)
                ? $"{type.Namespace}."
                : string.Empty;
        if (!type.IsGenericType)
        {
            return $"{prefix}{typeName}";
        }

        var declaringArgumentCount =
            type.IsNested && type.DeclaringType?.IsGenericType == true
                ? type.DeclaringType.GetGenericArguments().Length
                : 0;
        var arguments = type.GetGenericArguments()
            .Skip(declaringArgumentCount)
            .Select(argument => FormatPowerShellTypeName(
                argument,
                qualify,
                genericReplacements))
            .ToList();
        return arguments.Count == 0
            ? $"{prefix}{typeName}"
            : $"{prefix}{typeName}[{string.Join(", ", arguments)}]";
    }

    private static string FormatConstructorSignature(
        Type type,
        ConstructorInfo constructor) =>
        $"{FormatTypeName(type, qualify: false)}(" +
        $"{FormatParameters(constructor.GetParameters())})";

    private static string FormatMethodSignature(MethodInfo method)
    {
        var genericSuffix = method.IsGenericMethodDefinition
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        return $"{(method.IsStatic ? "static " : string.Empty)}" +
               $"{FormatTypeName(method.ReturnType, qualify: false)} " +
               $"{method.Name}{genericSuffix}(" +
               $"{FormatParameters(method.GetParameters())})";
    }

    private static string FormatPropertySignature(
        PropertyInfo property,
        bool includeSetter)
    {
        var accessor = property.GetMethod ?? property.SetMethod;
        var indexParameters = property.GetIndexParameters();
        var name = indexParameters.Length == 0
            ? property.Name
            : $"this[{FormatParameters(indexParameters)}]";
        var operation = includeSetter ? "set" : "get";
        return $"{(accessor?.IsStatic == true ? "static " : string.Empty)}" +
               $"{FormatTypeName(property.PropertyType, qualify: false)} " +
               $"{name} {{ {operation}; }}";
    }

    private static string FormatFieldSignature(
        FieldInfo field,
        bool write) =>
        $"{(field.IsStatic ? "static " : string.Empty)}" +
        $"{FormatTypeName(field.FieldType, qualify: false)} {field.Name}" +
        (write ? " = value" : string.Empty);

    private static string FormatEventSignature(
        EventInfo eventInfo,
        bool subscribe) =>
        $"{(eventInfo.AddMethod?.IsStatic == true ? "static " : string.Empty)}" +
        $"event {(eventInfo.EventHandlerType is null ? "Delegate" : FormatTypeName(eventInfo.EventHandlerType, qualify: false))} " +
        $"{eventInfo.Name} {(subscribe ? "+=" : "-=")} handler";

    private static string FormatParameters(
        IEnumerable<ParameterInfo> parameters) =>
        string.Join(
            ", ",
            parameters.Select(parameter =>
            {
                var modifier = parameter.IsOut
                    ? "out "
                    : parameter.ParameterType.IsByRef
                        ? parameter.IsIn
                            ? "in "
                            : "ref "
                        : string.Empty;
                var type = parameter.ParameterType.IsByRef
                    ? parameter.ParameterType.GetElementType()
                      ?? parameter.ParameterType
                    : parameter.ParameterType;
                var optional = parameter.IsOptional ? " = default" : string.Empty;
                return $"{modifier}{FormatTypeName(type, qualify: false)} " +
                       $"{parameter.Name ?? $"arg{parameter.Position}"}{optional}";
            }));

    private static void Add(
        IDictionary<
            (DotNetApiMemberKind Kind, string Name),
            List<DotNetApiOverloadDefinition>> groups,
        DotNetApiMemberKind kind,
        string name,
        MemberInfo member,
        DotNetApiOperation operation,
        string signature,
        bool isStatic)
    {
        var key = (kind, name);
        if (!groups.TryGetValue(key, out var overloads))
        {
            overloads = [];
            groups[key] = overloads;
        }

        overloads.Add(new DotNetApiOverloadDefinition(
            member,
            operation,
            signature,
            isStatic));
    }

    private static bool IsInitOnly(MethodInfo setter)
    {
        try
        {
            return setter.ReturnParameter
                .GetRequiredCustomModifiers()
                .Any(modifier => string.Equals(
                    modifier.FullName,
                    "System.Runtime.CompilerServices.IsExternalInit",
                    StringComparison.Ordinal));
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string RemoveGenericArity(string value)
    {
        var separator = value.IndexOf('`');
        return separator >= 0 ? value[..separator] : value;
    }
}

internal static class DotNetApiCommandFactory
{
    public static CommandDefinition Create(
        string runtimeCatalogId,
        string frameworkDisplayName,
        string targetFramework,
        DotNetApiTypeDefinition type,
        DotNetApiMemberGroup group,
        DotNetApiOverloadDefinition overload,
        ExecutionTarget executionTarget)
    {
        if (executionTarget is not (
            ExecutionTarget.CSharp or ExecutionTarget.PowerShell))
        {
            throw new ArgumentOutOfRangeException(
                nameof(executionTarget),
                executionTarget,
                "The .NET API browser can generate C# or PowerShell statements.");
        }

        var parameters = new List<CommandParameterDefinition>();
        var usedPlaceholders = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var genericReplacements = new Dictionary<Type, string>();

        foreach (var genericArgument in type.RuntimeType.GetGenericArguments())
        {
            var placeholder = CreateUniquePlaceholder(
                $"type_{genericArgument.Name}",
                usedPlaceholders);
            genericReplacements[genericArgument] = $"{{{placeholder}}}";
            parameters.Add(new CommandParameterDefinition
            {
                Name = genericArgument.Name,
                DisplayName = $"{genericArgument.Name} (type argument)",
                Placeholder = placeholder,
                Description =
                    $"{GetSyntaxName(executionTarget)} type used for the " +
                    $"{genericArgument.Name} generic parameter.",
                TypeName = "type",
                IsRequired = true,
                IncludeByDefault = true
            });
        }

        var template = overload.Member switch
        {
            ConstructorInfo constructor => BuildConstructorTemplate(
                type,
                constructor,
                parameters,
                usedPlaceholders,
                genericReplacements,
                executionTarget),
            MethodInfo method => BuildMethodTemplate(
                type,
                method,
                parameters,
                usedPlaceholders,
                genericReplacements,
                executionTarget),
            PropertyInfo property => BuildPropertyTemplate(
                type,
                property,
                overload,
                parameters,
                usedPlaceholders,
                genericReplacements,
                executionTarget),
            FieldInfo field => BuildFieldTemplate(
                type,
                field,
                overload,
                parameters,
                usedPlaceholders,
                genericReplacements,
                executionTarget),
            EventInfo eventInfo => BuildEventTemplate(
                type,
                eventInfo,
                overload,
                parameters,
                usedPlaceholders,
                genericReplacements,
                executionTarget),
            _ => $"{type.FullDisplayName}.{group.DisplayName};"
        };

        var memberLabel = group.Kind == DotNetApiMemberKind.Constructor
            ? "new"
            : group.Name;
        var displayName = $"{type.DisplayName}.{memberLabel}";
        var documentationUrl = BuildDocumentationUrl(type, group);
        var metadataToken = GetMetadataToken(overload.Member);
        var targetKeyword = targetFramework.EndsWith(
            ".0",
            StringComparison.Ordinal)
            ? targetFramework[..^2]
            : targetFramework.Replace(".", string.Empty, StringComparison.Ordinal);
        return new CommandDefinition
        {
            Id =
                $"{runtimeCatalogId}:{type.RuntimeType.FullName}:" +
                $"{group.Kind}:{metadataToken}:{overload.Operation}:" +
                $"{executionTarget}",
            CatalogId = runtimeCatalogId,
            Name = displayName,
            DisplayName = displayName,
            Description =
                $"{overload.Operation} a public {group.Kind.ToString().ToLowerInvariant()} " +
                $"declared by {type.FullDisplayName} using " +
                $"{GetSyntaxName(executionTarget)} syntax.",
            GuideDefinition =
                $"{overload.Signature} This entry was discovered from the " +
                $"supported public API surface of the matching " +
                $"{frameworkDisplayName} reference pack ({targetFramework}). " +
                $"The generated statement uses {GetSyntaxName(executionTarget)} syntax.",
            ModuleName = type.FullDisplayName,
            CommandType = group.Kind.ToString(),
            NamespaceName = type.NamespaceName,
            TypeName = type.DisplayName,
            MemberName = group.Name,
            MemberKind = group.Kind.ToString(),
            OverloadSignature = overload.Signature,
            DocumentationUrl = documentationUrl,
            Keywords =
            [
                "dotnet",
                targetKeyword,
                frameworkDisplayName,
                type.NamespaceName,
                type.TypeKind,
                group.Kind.ToString(),
                overload.Operation.ToString()
            ],
            Template = template,
            ExecutionTarget = executionTarget,
            PreferredShellKind = executionTarget == ExecutionTarget.CSharp
                ? TerminalShellKind.CSharp
                : TerminalShellKind.PowerShell,
            CompositionMode = CommandCompositionMode.Statement,
            Parameters = parameters
        };
    }

    private static string BuildConstructorTemplate(
        DotNetApiTypeDefinition type,
        ConstructorInfo constructor,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders,
        IReadOnlyDictionary<Type, string> genericReplacements,
        ExecutionTarget executionTarget)
    {
        var arguments = AddMethodParameters(
            constructor.GetParameters(),
            parameters,
            usedPlaceholders,
            executionTarget);
        var typeReference = FormatTypeReference(
            type.RuntimeType,
            genericReplacements,
            executionTarget);
        var objectName = AddObjectNameParameter(
            type,
            parameters,
            usedPlaceholders);
        return executionTarget == ExecutionTarget.PowerShell
            ? $"${objectName} = [{typeReference}]::new(" +
              $"{string.Join(", ", arguments)})"
            : $"var {objectName} = new {typeReference}(" +
              $"{string.Join(", ", arguments)});";
    }

    private static string BuildMethodTemplate(
        DotNetApiTypeDefinition type,
        MethodInfo method,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders,
        Dictionary<Type, string> genericReplacements,
        ExecutionTarget executionTarget)
    {
        var target = method.IsStatic
            ? FormatStaticTarget(
                type.RuntimeType,
                genericReplacements,
                executionTarget)
            : AddInstanceParameter(
                type,
                parameters,
                usedPlaceholders,
                executionTarget);
        var methodGenericArguments = method.IsGenericMethodDefinition
            ? method.GetGenericArguments()
            : [];
        var methodGenericMarkers = new List<string>();
        foreach (var genericArgument in methodGenericArguments)
        {
            var placeholder = CreateUniquePlaceholder(
                $"method_{genericArgument.Name}",
                usedPlaceholders);
            genericReplacements[genericArgument] = $"{{{placeholder}}}";
            methodGenericMarkers.Add($"{{{placeholder}}}");
            parameters.Add(new CommandParameterDefinition
            {
                Name = genericArgument.Name,
                DisplayName = $"{genericArgument.Name} (method type)",
                Placeholder = placeholder,
                Description =
                    $"{GetSyntaxName(executionTarget)} type used for the " +
                    $"{genericArgument.Name} method parameter.",
                TypeName = "type",
                IsRequired = true,
                IncludeByDefault = true
            });
        }

        var genericSuffix = methodGenericMarkers.Count == 0
            ? string.Empty
            : executionTarget == ExecutionTarget.PowerShell
                ? $"[{string.Join(", ", methodGenericMarkers)}]"
                : $"<{string.Join(", ", methodGenericMarkers)}>";
        var arguments = AddMethodParameters(
            method.GetParameters(),
            parameters,
            usedPlaceholders,
            executionTarget);
        var memberSeparator =
            executionTarget == ExecutionTarget.PowerShell
            && method.IsStatic
                ? "::"
                : ".";
        var invocation =
            $"{target}{memberSeparator}{method.Name}{genericSuffix}(" +
            $"{string.Join(", ", arguments)})";
        if (executionTarget == ExecutionTarget.PowerShell)
        {
            if (IsNonGenericAwaitable(method.ReturnType))
            {
                return $"({invocation}).GetAwaiter().GetResult()";
            }

            if (IsGenericAwaitable(method.ReturnType))
            {
                return $"$result = ({invocation}).GetAwaiter().GetResult()";
            }

            return string.Equals(
                method.ReturnType.FullName,
                "System.Void",
                StringComparison.Ordinal)
                ? invocation
                : $"$result = {invocation}";
        }

        if (string.Equals(
                method.ReturnType.FullName,
                "System.Void",
                StringComparison.Ordinal))
        {
            return $"{invocation};";
        }

        if (IsNonGenericAwaitable(method.ReturnType))
        {
            return $"await {invocation};";
        }

        return IsGenericAwaitable(method.ReturnType)
            ? $"var result = await {invocation};"
            : $"var result = {invocation};";
    }

    private static string BuildPropertyTemplate(
        DotNetApiTypeDefinition type,
        PropertyInfo property,
        DotNetApiOverloadDefinition overload,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders,
        IReadOnlyDictionary<Type, string> genericReplacements,
        ExecutionTarget executionTarget)
    {
        var accessor = overload.Operation == DotNetApiOperation.Write
            ? property.SetMethod
            : property.GetMethod;
        var isStatic = accessor?.IsStatic == true;
        var target = isStatic
            ? FormatStaticTarget(
                type.RuntimeType,
                genericReplacements,
                executionTarget)
            : AddInstanceParameter(
                type,
                parameters,
                usedPlaceholders,
                executionTarget);
        var indexArguments = AddMethodParameters(
            property.GetIndexParameters(),
            parameters,
            usedPlaceholders,
            executionTarget);
        var memberSeparator =
            executionTarget == ExecutionTarget.PowerShell && isStatic
                ? "::"
                : ".";
        var access = indexArguments.Count == 0
            ? $"{target}{memberSeparator}{property.Name}"
            : $"{target}[{string.Join(", ", indexArguments)}]";
        if (overload.Operation == DotNetApiOperation.Read)
        {
            return executionTarget == ExecutionTarget.PowerShell
                ? $"$value = {access}"
                : $"var value = {access};";
        }

        var valueMarker = AddValueParameter(
            "value",
            property.PropertyType,
            $"Value assigned to {property.Name}.",
            parameters,
            usedPlaceholders);
        return executionTarget == ExecutionTarget.PowerShell
            ? $"{access} = {valueMarker}"
            : $"{access} = {valueMarker};";
    }

    private static string BuildFieldTemplate(
        DotNetApiTypeDefinition type,
        FieldInfo field,
        DotNetApiOverloadDefinition overload,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders,
        IReadOnlyDictionary<Type, string> genericReplacements,
        ExecutionTarget executionTarget)
    {
        var target = field.IsStatic
            ? FormatStaticTarget(
                type.RuntimeType,
                genericReplacements,
                executionTarget)
            : AddInstanceParameter(
                type,
                parameters,
                usedPlaceholders,
                executionTarget);
        var memberSeparator =
            executionTarget == ExecutionTarget.PowerShell && field.IsStatic
                ? "::"
                : ".";
        var access = $"{target}{memberSeparator}{field.Name}";
        if (overload.Operation == DotNetApiOperation.Read)
        {
            return executionTarget == ExecutionTarget.PowerShell
                ? $"$value = {access}"
                : $"var value = {access};";
        }

        var valueMarker = AddValueParameter(
            "value",
            field.FieldType,
            $"Value assigned to {field.Name}.",
            parameters,
            usedPlaceholders);
        return executionTarget == ExecutionTarget.PowerShell
            ? $"{access} = {valueMarker}"
            : $"{access} = {valueMarker};";
    }

    private static string BuildEventTemplate(
        DotNetApiTypeDefinition type,
        EventInfo eventInfo,
        DotNetApiOverloadDefinition overload,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders,
        IReadOnlyDictionary<Type, string> genericReplacements,
        ExecutionTarget executionTarget)
    {
        var accessor = overload.Operation == DotNetApiOperation.Subscribe
            ? eventInfo.AddMethod
            : eventInfo.RemoveMethod;
        var isStatic = accessor?.IsStatic == true;
        var target = isStatic
            ? FormatStaticTarget(
                type.RuntimeType,
                genericReplacements,
                executionTarget)
            : AddInstanceParameter(
                type,
                parameters,
                usedPlaceholders,
                executionTarget);
        var handlerMarker = AddValueParameter(
            "handler",
            eventInfo.EventHandlerType ?? typeof(Delegate),
            $"Delegate attached to {eventInfo.Name}.",
            parameters,
            usedPlaceholders);
        var operation = overload.Operation == DotNetApiOperation.Subscribe
            ? "+="
            : "-=";
        if (executionTarget == ExecutionTarget.PowerShell)
        {
            var methodPrefix = overload.Operation
                == DotNetApiOperation.Subscribe
                ? "add_"
                : "remove_";
            var memberSeparator = isStatic ? "::" : ".";
            return $"{target}{memberSeparator}{methodPrefix}" +
                   $"{eventInfo.Name}({handlerMarker})";
        }

        return $"{target}.{eventInfo.Name} {operation} {handlerMarker};";
    }

    private static string AddInstanceParameter(
        DotNetApiTypeDefinition type,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders,
        ExecutionTarget executionTarget)
    {
        var placeholder = CreateUniquePlaceholder(
            "instance",
            usedPlaceholders);
        parameters.Add(new CommandParameterDefinition
        {
            Name = "instance",
            DisplayName = "instance expression",
            Placeholder = placeholder,
            Description =
                $"Existing {type.DisplayName} expression that receives the member call.",
            TypeName = type.FullDisplayName,
            DefaultValue = executionTarget == ExecutionTarget.PowerShell
                ? $"${GetDefaultObjectName(type)}"
                : GetDefaultObjectName(type),
            IsRequired = true,
            IncludeByDefault = true
        });
        return $"{{{placeholder}}}";
    }

    private static List<string> AddMethodParameters(
        IEnumerable<ParameterInfo> methodParameters,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders,
        ExecutionTarget executionTarget)
    {
        var arguments = new List<string>();
        foreach (var parameter in methodParameters)
        {
            var parameterName =
                parameter.Name ?? $"arg{parameter.Position}";
            var placeholder = CreateUniquePlaceholder(
                parameterName,
                usedPlaceholders);
            var parameterType = parameter.ParameterType.IsByRef
                ? parameter.ParameterType.GetElementType()
                  ?? parameter.ParameterType
                : parameter.ParameterType;
            var modifier = parameter.IsOut
                ? "out "
                : parameter.ParameterType.IsByRef
                    ? parameter.IsIn
                        ? "in "
                        : "ref "
                    : string.Empty;
            parameters.Add(new CommandParameterDefinition
            {
                Name = parameterName,
                DisplayName = parameterName,
                Placeholder = placeholder,
                Description =
                    $"Value for the {parameterName} parameter.",
                TypeName = $"{modifier}{DotNetApiReflection.FormatTypeName(
                    parameterType,
                    qualify: false)}".Trim(),
                IsRequired = !parameter.IsOptional
                             && !parameter.HasDefaultValue
                             && !parameter.GetCustomAttributesData().Any(
                                 attribute => string.Equals(
                                     attribute.AttributeType.FullName,
                                     "System.ParamArrayAttribute",
                                     StringComparison.Ordinal)),
                IncludeByDefault = !parameter.IsOptional
                                   && !parameter.HasDefaultValue
                                   && !parameter.GetCustomAttributesData().Any(
                                       attribute => string.Equals(
                                           attribute.AttributeType.FullName,
                                           "System.ParamArrayAttribute",
                                           StringComparison.Ordinal))
            });
            arguments.Add(executionTarget == ExecutionTarget.PowerShell
                && !string.IsNullOrWhiteSpace(modifier)
                    ? $"[ref]{{{placeholder}}}"
                    : $"{modifier}{{{placeholder}}}");
        }

        return arguments;
    }

    private static string AddValueParameter(
        string name,
        Type type,
        string description,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders)
    {
        var placeholder = CreateUniquePlaceholder(name, usedPlaceholders);
        parameters.Add(new CommandParameterDefinition
        {
            Name = name,
            DisplayName = name,
            Placeholder = placeholder,
            Description = description,
            TypeName = DotNetApiReflection.FormatTypeName(
                type,
                qualify: false),
            IsRequired = true,
            IncludeByDefault = true
        });
        return $"{{{placeholder}}}";
    }

    private static string AddObjectNameParameter(
        DotNetApiTypeDefinition type,
        ICollection<CommandParameterDefinition> parameters,
        ISet<string> usedPlaceholders)
    {
        var placeholder = CreateUniquePlaceholder(
            "objectName",
            usedPlaceholders);
        parameters.Add(new CommandParameterDefinition
        {
            Name = "objectName",
            DisplayName = "object variable",
            Placeholder = placeholder,
            Description =
                "Variable name used to store the newly constructed object.",
            TypeName = "identifier",
            DefaultValue = GetDefaultObjectName(type),
            IsRequired = true,
            IncludeByDefault = true
        });
        return $"{{{placeholder}}}";
    }

    private static string GetDefaultObjectName(
        DotNetApiTypeDefinition type)
    {
        var displayName = type.DisplayName;
        var genericSeparator = displayName.IndexOf('<');
        if (genericSeparator >= 0)
        {
            displayName = displayName[..genericSeparator];
        }

        var identifier = new string(displayName
            .Where(char.IsLetterOrDigit)
            .ToArray());
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "instance";
        }

        return char.ToLowerInvariant(identifier[0]) + identifier[1..];
    }

    private static string FormatStaticTarget(
        Type type,
        IReadOnlyDictionary<Type, string> genericReplacements,
        ExecutionTarget executionTarget)
    {
        var reference = FormatTypeReference(
            type,
            genericReplacements,
            executionTarget);
        return executionTarget == ExecutionTarget.PowerShell
            ? $"[{reference}]"
            : reference;
    }

    private static string FormatTypeReference(
        Type type,
        IReadOnlyDictionary<Type, string> genericReplacements,
        ExecutionTarget executionTarget) =>
        executionTarget == ExecutionTarget.PowerShell
            ? DotNetApiReflection.FormatPowerShellTypeName(
                type,
                qualify: true,
                genericReplacements)
            : DotNetApiReflection.FormatTypeName(
                type,
                qualify: true,
                genericReplacements);

    private static string GetSyntaxName(ExecutionTarget executionTarget) =>
        executionTarget == ExecutionTarget.PowerShell
            ? "PowerShell"
            : "C#";

    private static string CreateUniquePlaceholder(
        string preferred,
        ISet<string> used)
    {
        var normalized = new string(preferred
            .Select(character =>
                char.IsLetterOrDigit(character) || character == '_'
                    ? character
                    : '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "value";
        }

        var candidate = normalized;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{normalized}_{suffix++}";
        }

        return candidate;
    }

    private static bool IsNonGenericAwaitable(Type type) =>
        string.Equals(
            type.FullName,
            "System.Threading.Tasks.Task",
            StringComparison.Ordinal)
        || string.Equals(
            type.FullName,
            "System.Threading.Tasks.ValueTask",
            StringComparison.Ordinal);

    private static bool IsGenericAwaitable(Type type) =>
        type.IsGenericType
        && (string.Equals(
                type.GetGenericTypeDefinition().FullName,
                "System.Threading.Tasks.Task`1",
                StringComparison.Ordinal)
            || string.Equals(
                type.GetGenericTypeDefinition().FullName,
                "System.Threading.Tasks.ValueTask`1",
                StringComparison.Ordinal));

    private static string BuildDocumentationUrl(
        DotNetApiTypeDefinition type,
        DotNetApiMemberGroup group)
    {
        var typeSlug = (type.RuntimeType.FullName ?? type.FullDisplayName)
            .Replace('+', '.')
            .ToLowerInvariant();
        for (var arity = 1; arity <= 9; arity++)
        {
            typeSlug = typeSlug.Replace(
                $"`{arity}",
                $"-{arity}",
                StringComparison.Ordinal);
        }

        var memberSuffix = group.Kind == DotNetApiMemberKind.Constructor
            ? string.Empty
            : $".{group.Name.ToLowerInvariant()}";
        return $"https://learn.microsoft.com/dotnet/api/{typeSlug}{memberSuffix}";
    }

    private static int GetMetadataToken(MemberInfo member)
    {
        try
        {
            return member.MetadataToken;
        }
        catch (InvalidOperationException)
        {
            return member.Name.GetHashCode(StringComparison.Ordinal);
        }
    }
}
