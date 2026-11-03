using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundation.TypedIds.Generator
{
    /// <summary>
    /// Generates the boilerplate of strongly typed ids. A declaration like
    /// <c>[StableId] public partial struct ScreenId { }</c> gets a serialized string value,
    /// equality, ordering, a debugger display and (when MemoryPack is referenced) a formatter.
    /// <c>[RuntimeId]</c> does the same around a <c>Guid</c>.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class TypedIdGenerator : IIncrementalGenerator
    {
        private const string StableIdAttributeName = "Foundation.TypedIds.StableIdAttribute";
        private const string RuntimeIdAttributeName = "Foundation.TypedIds.RuntimeIdAttribute";

        private static readonly DiagnosticDescriptor NotPartial = new DiagnosticDescriptor(
            "TID001", "Typed id must be a partial struct",
            "'{0}' is marked as a typed id and must be declared 'partial struct'",
            "TypedIds", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor ReadOnlyStruct = new DiagnosticDescriptor(
            "TID002", "Typed id must not be readonly",
            "'{0}' must not be a 'readonly struct': Unity does not serialize readonly fields. The generated members are readonly",
            "TypedIds", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor Unsupported = new DiagnosticDescriptor(
            "TID003", "Unsupported typed id declaration",
            "'{0}' must be a non-generic top-level struct",
            "TypedIds", DiagnosticSeverity.Error, true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var stable = context.SyntaxProvider.ForAttributeWithMetadataName(
                StableIdAttributeName,
                static (node, _) => node is StructDeclarationSyntax,
                static (ctx, _) => CreateModel(ctx, IdKind.Stable));

            var runtime = context.SyntaxProvider.ForAttributeWithMetadataName(
                RuntimeIdAttributeName,
                static (node, _) => node is StructDeclarationSyntax,
                static (ctx, _) => CreateModel(ctx, IdKind.Runtime));

            var capabilities = context.CompilationProvider.Select(static (compilation, _) =>
            {
                var formatter = compilation.GetTypeByMetadataName("MemoryPack.MemoryPackFormatter`1");
                var supportsScoped = compilation is CSharpCompilation csharp && (int)csharp.LanguageVersion >= 1100;
                return new Capabilities(
                    formatter != null,
                    compilation.GetTypeByMetadataName("UnityEngine.SerializeField") != null,
                    supportsScoped && HasScopedValue(formatter, "Serialize"),
                    supportsScoped && HasScopedValue(formatter, "Deserialize"),
                    compilation.AssemblyName ?? "Assembly");
            });

            var models = stable.Collect().Combine(runtime.Collect())
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));

            context.RegisterSourceOutput(models.Combine(capabilities), static (spc, input) =>
            {
                var (list, caps) = input;
                var emitted = new List<IdModel>();
                foreach (var model in list)
                {
                    if (!Validate(spc, model))
                        continue;

                    spc.AddSource(model.HintName, Emit(model, caps));
                    emitted.Add(model);
                }

                if (caps.HasMemoryPack && emitted.Exists(static m => m.Kind == IdKind.Stable))
                    spc.AddSource("TypedIdRegistration.g.cs", EmitRegistration(emitted, caps));
            });
        }

        private static bool HasScopedValue(INamedTypeSymbol formatter, string methodName)
        {
            if (formatter == null)
                return false;

            foreach (var member in formatter.GetMembers(methodName))
            {
                if (member is not IMethodSymbol method || method.Parameters.Length != 2)
                    continue;

                var parameter = method.Parameters[1];
                var scopedKind = typeof(IParameterSymbol).GetProperty("ScopedKind")?.GetValue(parameter);
                if (scopedKind?.ToString() == "ScopedRef")
                    return true;

                foreach (var attribute in parameter.GetAttributes())
                {
                    if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.ScopedRefAttribute")
                        return true;
                }
            }

            return false;
        }

        private static IdModel CreateModel(GeneratorAttributeSyntaxContext ctx, IdKind kind)
        {
            var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
            var syntax = (StructDeclarationSyntax)ctx.TargetNode;
            var isPartial = false;
            foreach (var modifier in syntax.Modifiers)
            {
                if (modifier.Text == "partial")
                    isPartial = true;
            }

            var isSupported = symbol.ContainingType == null
                && !symbol.IsGenericType
                && (symbol.DeclaredAccessibility == Accessibility.Public
                    || symbol.DeclaredAccessibility == Accessibility.Internal);

            return new IdModel(
                kind,
                symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString(),
                symbol.Name,
                symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
                isPartial,
                symbol.IsReadOnly,
                isSupported,
                syntax.Identifier.GetLocation());
        }

        private static bool Validate(SourceProductionContext spc, IdModel model)
        {
            if (!model.IsSupported)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Unsupported, model.Location, model.Name));
                return false;
            }

            if (!model.IsPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(NotPartial, model.Location, model.Name));
                return false;
            }

            if (model.IsReadOnly)
            {
                spc.ReportDiagnostic(Diagnostic.Create(ReadOnlyStruct, model.Location, model.Name));
                return false;
            }

            return true;
        }

        private static string Emit(IdModel model, Capabilities caps)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("// <auto-generated> Foundation.TypedIds.Generator </auto-generated>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine("#pragma warning disable");
            var indent = string.Empty;
            if (model.Namespace.Length > 0)
            {
                sb.Append("namespace ").AppendLine(model.Namespace);
                sb.AppendLine("{");
                indent = "    ";
            }

            if (model.Kind == IdKind.Stable)
                EmitStable(sb, model, caps, indent);
            else
                EmitRuntime(sb, model, caps, indent);

            if (model.Namespace.Length > 0)
                sb.AppendLine("}");

            return sb.ToString();
        }

        private static void EmitStable(StringBuilder sb, IdModel m, Capabilities caps, string i)
        {
            var n = m.Name;
            var serialize = caps.HasSerializeField ? "[global::UnityEngine.SerializeField] " : string.Empty;
            var serializeScope = caps.ScopedSerialize ? "scoped " : string.Empty;
            var deserializeScope = caps.ScopedDeserialize ? "scoped " : string.Empty;
            sb.AppendLine($"{i}[global::System.Serializable]");
            sb.AppendLine($"{i}[global::System.Diagnostics.DebuggerDisplay(\"{{DebuggerDisplay,nq}}\")]");
            sb.AppendLine($"{i}{m.Accessibility} partial struct {n} : global::Foundation.TypedIds.IStableId, global::System.IEquatable<{n}>, global::System.IComparable<{n}>");
            sb.AppendLine($"{i}{{");
            sb.AppendLine($"{i}    {serialize}private string value;");
            sb.AppendLine();
            sb.AppendLine($"{i}    public {n}(string value) {{ this.value = value; }}");
            sb.AppendLine();
            sb.AppendLine($"{i}    public static {n} None => default;");
            sb.AppendLine($"{i}    public static {n} From(string value) => new {n}(value);");
            sb.AppendLine($"{i}    public readonly string Value => value ?? string.Empty;");
            sb.AppendLine($"{i}    public readonly bool IsEmpty => string.IsNullOrEmpty(value);");
            sb.AppendLine($"{i}    public readonly bool Equals({n} other) => string.Equals(Value, other.Value, global::System.StringComparison.Ordinal);");
            sb.AppendLine($"{i}    public override readonly bool Equals(object obj) => obj is {n} other && Equals(other);");
            sb.AppendLine($"{i}    public override readonly int GetHashCode() => Value.GetHashCode();");
            sb.AppendLine($"{i}    public readonly int CompareTo({n} other) => string.CompareOrdinal(Value, other.Value);");
            sb.AppendLine($"{i}    public override readonly string ToString() => DebuggerDisplay;");
            sb.AppendLine($"{i}    public static bool operator ==({n} left, {n} right) => left.Equals(right);");
            sb.AppendLine($"{i}    public static bool operator !=({n} left, {n} right) => !left.Equals(right);");
            sb.AppendLine();
            sb.AppendLine($"{i}    private readonly string DebuggerDisplay => \"{n}(\" + (IsEmpty ? \"<none>\" : value) + \")\";");
            sb.AppendLine($"{i}}}");

            if (!caps.HasMemoryPack)
                return;

            sb.AppendLine();
            if (caps.HasSerializeField)
                sb.AppendLine($"{i}[global::UnityEngine.Scripting.Preserve]");
            sb.AppendLine($"{i}internal sealed class {n}MemoryPackFormatter : global::MemoryPack.MemoryPackFormatter<{n}>");
            sb.AppendLine($"{i}{{");
            if (caps.HasSerializeField)
                sb.AppendLine($"{i}    [global::UnityEngine.Scripting.Preserve]");
            sb.AppendLine($"{i}    public override void Serialize<TBufferWriter>(ref global::MemoryPack.MemoryPackWriter<TBufferWriter> writer, {serializeScope}ref {n} value)");
            sb.AppendLine($"{i}    {{");
            sb.AppendLine($"{i}        writer.WriteString(value.IsEmpty ? null : value.Value);");
            sb.AppendLine($"{i}    }}");
            sb.AppendLine();
            if (caps.HasSerializeField)
                sb.AppendLine($"{i}    [global::UnityEngine.Scripting.Preserve]");
            sb.AppendLine($"{i}    public override void Deserialize(ref global::MemoryPack.MemoryPackReader reader, {deserializeScope}ref {n} value)");
            sb.AppendLine($"{i}    {{");
            sb.AppendLine($"{i}        value = new {n}(reader.ReadString());");
            sb.AppendLine($"{i}    }}");
            sb.AppendLine($"{i}}}");
        }

        private static void EmitRuntime(StringBuilder sb, IdModel m, Capabilities caps, string i)
        {
            var n = m.Name;
            sb.AppendLine($"{i}[global::System.Serializable]");
            sb.AppendLine($"{i}[global::System.Diagnostics.DebuggerDisplay(\"{{DebuggerDisplay,nq}}\")]");
            sb.AppendLine($"{i}{m.Accessibility} partial struct {n} : global::Foundation.TypedIds.IRuntimeId, global::System.IEquatable<{n}>, global::System.IComparable<{n}>");
            sb.AppendLine($"{i}{{");
            sb.AppendLine($"{i}    private global::System.Guid value;");
            sb.AppendLine();
            sb.AppendLine($"{i}    public {n}(global::System.Guid value) {{ this.value = value; }}");
            sb.AppendLine();
            sb.AppendLine($"{i}    public static {n} None => default;");
            sb.AppendLine($"{i}    public static {n} New(global::Foundation.TypedIds.IGuidGenerator generator) => new {n}(generator.Next());");
            sb.AppendLine($"{i}    public static bool TryParse(string text, out {n} result)");
            sb.AppendLine($"{i}    {{");
            sb.AppendLine($"{i}        if (global::System.Guid.TryParse(text, out var guid)) {{ result = new {n}(guid); return true; }}");
            sb.AppendLine($"{i}        result = default;");
            sb.AppendLine($"{i}        return false;");
            sb.AppendLine($"{i}    }}");
            sb.AppendLine();
            sb.AppendLine($"{i}    public readonly global::System.Guid Value => value;");
            sb.AppendLine($"{i}    public readonly bool IsEmpty => value == global::System.Guid.Empty;");
            sb.AppendLine($"{i}    public readonly bool Equals({n} other) => value.Equals(other.value);");
            sb.AppendLine($"{i}    public override readonly bool Equals(object obj) => obj is {n} other && Equals(other);");
            sb.AppendLine($"{i}    public override readonly int GetHashCode() => value.GetHashCode();");
            sb.AppendLine($"{i}    public readonly int CompareTo({n} other) => value.CompareTo(other.value);");
            sb.AppendLine($"{i}    public override readonly string ToString() => DebuggerDisplay;");
            sb.AppendLine($"{i}    public static bool operator ==({n} left, {n} right) => left.Equals(right);");
            sb.AppendLine($"{i}    public static bool operator !=({n} left, {n} right) => !left.Equals(right);");
            sb.AppendLine();
            sb.AppendLine($"{i}    private readonly string DebuggerDisplay => \"{n}(\" + (IsEmpty ? \"<none>\" : value.ToString()) + \")\";");
            sb.AppendLine($"{i}}}");
        }

        private static string EmitRegistration(List<IdModel> models, Capabilities caps)
        {
            var assemblyNamespace = Sanitize(caps.AssemblyName) + ".Generated";
            var sb = new StringBuilder(1024);
            sb.AppendLine("// <auto-generated> Foundation.TypedIds.Generator </auto-generated>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine($"namespace {assemblyNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Registers the MemoryPack formatters of every [StableId] declared in this assembly.");
            if (caps.HasSerializeField)
            {
                sb.AppendLine("    /// Runs automatically in the Editor and in standalone players; call it explicitly from assemblies");
                sb.AppendLine("    /// whose static initialization does not run on load (for example hot-update assemblies).</summary>");
            }
            else
            {
                sb.AppendLine("    /// Call it explicitly from assemblies without UnityEngine.</summary>");
            }
            sb.AppendLine("    public static class TypedIdRegistration");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void RegisterMemoryPackFormatters()");
            sb.AppendLine("        {");
            foreach (var model in models)
            {
                if (model.Kind != IdKind.Stable)
                    continue;

                var full = (model.Namespace.Length > 0 ? "global::" + model.Namespace + "." : "global::") + model.Name;
                sb.AppendLine($"            if (!global::MemoryPack.MemoryPackFormatterProvider.IsRegistered<{full}>())");
                sb.AppendLine($"                global::MemoryPack.MemoryPackFormatterProvider.Register(new {full}MemoryPackFormatter());");
            }

            sb.AppendLine("        }");
            if (caps.HasSerializeField)
            {
                sb.AppendLine();
                sb.AppendLine("        [global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
                sb.AppendLine("        private static void AutoRegisterAtRuntime() => RegisterMemoryPackFormatters();");
                sb.AppendLine();
                sb.AppendLine("#if UNITY_EDITOR");
                sb.AppendLine("        [global::UnityEditor.InitializeOnLoadMethod]");
                sb.AppendLine("        private static void AutoRegisterInEditor() => RegisterMemoryPackFormatters();");
                sb.AppendLine("#endif");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Sanitize(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
                sb.Append(char.IsLetterOrDigit(c) || c == '.' ? c : '_');
            return sb.ToString();
        }

        private enum IdKind
        {
            Stable,
            Runtime,
        }

        private readonly struct Capabilities : IEquatable<Capabilities>
        {
            public Capabilities(bool hasMemoryPack, bool hasSerializeField, bool scopedSerialize, bool scopedDeserialize, string assemblyName)
            {
                HasMemoryPack = hasMemoryPack;
                HasSerializeField = hasSerializeField;
                ScopedSerialize = scopedSerialize;
                ScopedDeserialize = scopedDeserialize;
                AssemblyName = assemblyName;
            }

            public bool HasMemoryPack { get; }
            public bool HasSerializeField { get; }
            public bool ScopedSerialize { get; }
            public bool ScopedDeserialize { get; }
            public string AssemblyName { get; }

            public bool Equals(Capabilities other) => HasMemoryPack == other.HasMemoryPack
                && HasSerializeField == other.HasSerializeField && ScopedSerialize == other.ScopedSerialize
                && ScopedDeserialize == other.ScopedDeserialize && AssemblyName == other.AssemblyName;

            public override bool Equals(object obj) => obj is Capabilities other && Equals(other);

            public override int GetHashCode() => (AssemblyName?.GetHashCode() ?? 0) ^ (HasMemoryPack ? 1 : 0)
                ^ (HasSerializeField ? 2 : 0) ^ (ScopedSerialize ? 4 : 0) ^ (ScopedDeserialize ? 8 : 0);
        }

        private sealed class IdModel : IEquatable<IdModel>
        {
            public IdModel(
                IdKind kind, string ns, string name, string accessibility,
                bool isPartial, bool isReadOnly, bool isSupported, Location location)
            {
                Kind = kind;
                Namespace = ns;
                Name = name;
                Accessibility = accessibility;
                IsPartial = isPartial;
                IsReadOnly = isReadOnly;
                IsSupported = isSupported;
                Location = location;
            }

            public IdKind Kind { get; }
            public string Namespace { get; }
            public string Name { get; }
            public string Accessibility { get; }
            public bool IsPartial { get; }
            public bool IsReadOnly { get; }
            public bool IsSupported { get; }
            public Location Location { get; }

            public string HintName => (Namespace.Length > 0 ? Namespace + "." : string.Empty) + Name + ".g.cs";

            public bool Equals(IdModel other) => other != null && Kind == other.Kind && Namespace == other.Namespace
                && Name == other.Name && Accessibility == other.Accessibility && IsPartial == other.IsPartial
                && IsReadOnly == other.IsReadOnly && IsSupported == other.IsSupported;

            public override bool Equals(object obj) => Equals(obj as IdModel);

            public override int GetHashCode() => (Namespace + "." + Name).GetHashCode() ^ (int)Kind;
        }
    }
}
