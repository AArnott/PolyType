using PolyType.Roslyn;

namespace PolyType.SourceGenerator.Model;

public sealed record TypeShapeProviderModel
{
    public required TypeDeclarationModel ProviderDeclaration { get; init; }
    public required ImmutableEquatableDictionary<TypeId, TypeShapeModel> ProvidedTypes { get; init; }
    public required ImmutableEquatableArray<TypeDeclarationModel> AnnotatedTypes { get; init; }
    public required ImmutableEquatableArray<string> SuppressedDiagnosticIds { get; init; }
    public required bool TargetSupportsIShapeableOfT { get; init; }

    /// <summary>Gets whether the consumer enables the updated C# memory-safety rules.</summary>
    public bool UsesUpdatedMemorySafetyRules { get; init; }

    /// <summary>Gets whether reflection invocation supports suppressing exception wrapping.</summary>
    public bool SupportsDoNotWrapExceptions { get; init; }

    /// <summary>Gets whether the consumer can create a span from a managed reference.</summary>
    public bool SupportsMemoryMarshalCreateSpan { get; init; }

    /// <summary>
    /// Gets whether the target exposes reflection metadata types such as <c>System.Reflection.MethodBase</c>.
    /// Reflection-less targets (e.g. NetWasm's netwasm0.1) get shapes without reflection info factories.
    /// </summary>
    public bool SupportsReflectionMetadata { get; init; } = true;
}