# Recommendation Report: Issue #363

## Analysis of TypeDataModelGenerator and Parser Class Hierarchy

### Issue Summary

Issue #363 requests reducing the amount of re-invention required to use `PolyType.Roslyn`. The `Parser` class in the source generator overrides many methods from `TypeDataModelGenerator`, and these overrides contain non-trivial policy that is likely to change over time. 

### User Requirements

The library should be factored so that a given user can choose any of:

1. **Keep the logic AND the attributes** - use PolyType's full behavior out of the box
2. **Keep the logic but replace the attributes** - use PolyType's constructor/property heuristics but with custom attributes
3. **Replace both logic and attributes** - fully custom behavior

Currently, the existing code only allows option 3. The goal is to refactor so that all three options are available.

### Analysis of Virtual Methods and Overrides

Below is the complete analysis of all virtual methods in `TypeDataModelGenerator` and how they are overridden in `Parser`:

#### Methods in TypeDataModelGenerator Base Class

| Method | Base Implementation | Parser Override | PolyType Attribute-Specific? |
|--------|---------------------|-----------------|------------------------------|
| `DefaultLocation` | Returns first location of GenerationScope | Returns `null` | **No** - just a different default |
| `NormalizeType` | Returns type unchanged | Erases compiler metadata (nullable annotations, tuple labels) | **No** - general normalization |
| `SymbolComparer` | Returns `SymbolEqualityComparer.Default` | Not overridden | N/A |
| `IsSupportedType` | Excludes void, pointers, errors, generic parameters | Adds exclusion of ref-like types and static types | **No** - general policy |
| `IsAccessibleSymbol` | Checks symbol accessibility | Not overridden | N/A |
| `FlattenSystemTupleTypes` | Returns `false` | Returns `true` (for F# support) | **No** - general F# support |
| `IncludeDelegateParameters` | Returns `false` | Returns `true` | **No** - general preference |
| `SkipObjectMemberResolution` | Skips simple types, etc. | Not overridden | N/A |
| `ResolveConstructors` | Resolves accessible constructors | **Complex override** with ConstructorShapeAttribute support and heuristic selection | **Partially** - attribute parsing is PolyType-specific, but heuristics are general |
| `ResolveProperties` | Resolves public properties/fields | Adds duplicate name validation with diagnostics | **Partially** - diagnostic reporting is PolyType-specific |
| `IncludeProperty` | Public properties only | Adds PropertyShapeAttribute parsing | **Yes** - attribute parsing |
| `IncludeField` | Public fields only | Adds PropertyShapeAttribute parsing | **Yes** - attribute parsing |
| `IsRequiredByPolicy` (property) | Returns `null` | Adds DataMemberAttribute and PropertyShapeAttribute support | **Yes** - attribute parsing |
| `IsRequiredByPolicy` (field) | Returns `null` | Adds DataMemberAttribute and PropertyShapeAttribute support | **Yes** - attribute parsing |
| `ResolveMethods` | Returns empty | Complex override with MethodShapeAttribute support | **Partially** - attribute parsing is PolyType-specific |
| `ResolveEvents` | Returns empty | Complex override with EventShapeAttribute support | **Partially** - attribute parsing is PolyType-specific |
| `ResolveDerivedTypes` | Returns empty | Complex override with DerivedTypeShapeAttribute/KnownTypeAttribute support | **Partially** - attribute parsing is PolyType-specific |
| `MapType` | Core type mapping | Adds TypeShapeAttribute, TypeShapeExtension, F# types, surrogate handling | **Yes** - heavily uses PolyType-specific attributes |
| `MapMethod` | Maps method parameters | Adds generic method rejection + Unit type handling | **Partially** - diagnostic reporting is PolyType-specific |
| `GetEffectiveReturnType` | Unwraps Task/ValueTask | Not overridden | N/A |
| `MapEvent` | Maps event to data model | Not overridden | N/A |
| `ParseCustomAssociatedTypeAttributes` | Returns empty | Complex override for custom attributes | **Yes** - attribute parsing |
| `GetEnumValueName` | Returns field name | Adds EnumMemberShapeAttribute/EnumMemberAttribute support | **Yes** - attribute parsing |

### Key Findings

1. **Attribute Parsing is the Core Differentiator**: Most of the source generator-specific logic involves parsing PolyType's custom attributes (`[PropertyShape]`, `[ConstructorShape]`, `[TypeShape]`, `[MethodShape]`, etc.). These are deeply intertwined with the source generator.

2. **Constructor Resolution is Highly Non-Trivial**: The `ResolveConstructors` override in `Parser` contains complex heuristics for selecting the "best" constructor based on:
   - Constructors with `[ConstructorShape]` attribute
   - Minimizing unmatched required parameters
   - Maximizing matched read-only property parameters
   - Minimizing total parameter count

3. **Known Symbols Dependencies**: The `Parser` class uses `PolyTypeKnownSymbols` which extends `KnownSymbols` with PolyType-specific types (e.g., `GenerateShapeAttribute`, `PropertyShapeAttribute`, `ConstructorShapeAttribute`, etc.). This creates a dependency chain.

4. **Clean Separability is Possible**: Most "general" functionality (constructor heuristics, property resolution, type normalization) could be moved to the base class, with the attribute-specific parsing delegated to additional virtual methods or the derived class.

### Recommendation

**Recommended Approach: Create an intermediate `PolyTypeModelGenerator` class in PolyType.Roslyn**

Based on the requirement to support all three use cases, the cleanest architecture is a three-level hierarchy:

```text
TypeDataModelGenerator (base class - core logic only, no attributes)
    └── PolyTypeModelGenerator (new class - adds PolyType attribute parsing)
        └── Parser (source generator - adds diagnostics and source generation specifics)
```

This architecture supports all three user requirements:

| Use Case | Which Class to Extend |
|----------|----------------------|
| Keep logic + attributes | Extend `PolyTypeModelGenerator` |
| Keep logic, replace attributes | Extend `TypeDataModelGenerator`, override attribute hooks |
| Replace both | Extend `TypeDataModelGenerator`, override everything |

#### Prescription

**Step 1: Enhance `TypeDataModelGenerator` with improved default implementations**

Move the following **logic** (but not attribute parsing) from `Parser` to `TypeDataModelGenerator`:

1. **`NormalizeType`**: Move the `EraseCompilerMetadata` call to the base class
2. **`IsSupportedType`**: Add exclusion of ref-like types and static types  
3. **`FlattenSystemTupleTypes`**: Change default to `true` (better F# support)
4. **`IncludeDelegateParameters`**: Change default to `true`
5. **`ResolveConstructors`**: Move the **heuristic selection** to the base class (minimizing unmatched params, maximizing read-only matches, minimizing total params)
6. **`ResolveMethods`**: Implement default resolution of public ordinary methods (excluding Object methods, compiler-generated, etc.)
7. **`ResolveEvents`**: Implement default resolution of public events

Add **hook methods** that are called by the above methods but return empty/false by default:

```csharp
// Called by ResolveConstructors to find attribute-marked constructors
protected virtual IEnumerable<IMethodSymbol> ResolveAttributeMarkedConstructors(ITypeSymbol type) => [];

// Called by IncludeProperty for custom name/order/ignore from attributes
protected virtual bool TryGetPropertyAttributeMetadata(IPropertySymbol property, 
    out string? customName, out int order, out bool ignore)
{
    customName = null; order = 0; ignore = false; return false;
}

// Called by IncludeField for custom name/order/ignore from attributes
protected virtual bool TryGetFieldAttributeMetadata(IFieldSymbol field,
    out string? customName, out int order, out bool ignore)
{
    customName = null; order = 0; ignore = false; return false;
}

// Called by IsRequiredByPolicy for attribute-based required checking
protected virtual bool? GetRequiredByAttribute(ISymbol member) => null;

// Called by ResolveMethods for method attribute metadata
protected virtual bool TryGetMethodAttributeMetadata(IMethodSymbol method,
    out string? customName, out bool ignore)
{
    customName = null; ignore = false; return false;
}

// Called by ResolveEvents for event attribute metadata
protected virtual bool TryGetEventAttributeMetadata(IEventSymbol eventSymbol,
    out string? customName, out bool ignore)
{
    customName = null; ignore = false; return false;
}

// Called by ResolveDerivedTypes
protected virtual IEnumerable<(ITypeSymbol Type, string? Name, int Tag)> ResolveAttributeDerivedTypes(ITypeSymbol type) => [];
```

**Step 2: Create `PolyTypeModelGenerator` class in PolyType.Roslyn**

This new class extends `TypeDataModelGenerator` and implements the PolyType attribute parsing:

```csharp
public class PolyTypeModelGenerator : TypeDataModelGenerator
{
    // Override all the attribute hook methods to parse PolyType attributes
    protected override IEnumerable<IMethodSymbol> ResolveAttributeMarkedConstructors(ITypeSymbol type)
    {
        return type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(ctor => ctor is { IsStatic: false, MethodKind: MethodKind.Constructor })
            .Where(ctor => ctor.HasAttribute(KnownSymbols.ConstructorShapeAttribute));
    }
    
    protected override bool TryGetPropertyAttributeMetadata(IPropertySymbol property,
        out string? customName, out int order, out bool ignore)
    {
        return ParsePropertyShapeAttribute(property, out customName, out order, out ignore);
    }
    
    // ... similar for other attribute hooks
}
```

**Note:** This requires either:
- Moving `PolyTypeKnownSymbols` attribute definitions to `PolyType.Roslyn` (preferred), OR
- Having `PolyTypeModelGenerator` use string-based attribute lookup (e.g., `HasAttribute("PolyType.ConstructorShapeAttribute")`)

**Step 3: Update `Parser` class to extend `PolyTypeModelGenerator`**

The `Parser` class would then:
1. Inherit from `PolyTypeModelGenerator` instead of `TypeDataModelGenerator`
2. Only override methods for:
   - Diagnostic reporting (e.g., `DuplicateConstructorShape`, `DuplicateMemberName`)
   - F# union/option handling (still needed in source generator)
   - Source generation-specific model mapping

### Benefits of This Approach

1. **Use Case 1 (Keep logic + attributes)**: Users extend `PolyTypeModelGenerator` directly
2. **Use Case 2 (Keep logic, custom attributes)**: Users extend `TypeDataModelGenerator` and override only the attribute hooks
3. **Use Case 3 (Fully custom)**: Users extend `TypeDataModelGenerator` and override any method
4. **No breaking changes**: Existing consumers continue to work
5. **Clean separation**: Logic, attribute parsing, and source generation are cleanly separated

### Moving PolyType Attributes to PolyType.Roslyn

For `PolyTypeModelGenerator` to work, we need access to the PolyType attribute type symbols. Options:

**Option A (Preferred): Move attribute symbol definitions to KnownSymbols**

Add the following to `KnownSymbols` in `PolyType.Roslyn`:

```csharp
public INamedTypeSymbol? ConstructorShapeAttribute => GetOrResolveType("PolyType.ConstructorShapeAttribute", ref _ConstructorShapeAttribute);
public INamedTypeSymbol? PropertyShapeAttribute => GetOrResolveType("PolyType.PropertyShapeAttribute", ref _PropertyShapeAttribute);
public INamedTypeSymbol? MethodShapeAttribute => GetOrResolveType("PolyType.MethodShapeAttribute", ref _MethodShapeAttribute);
public INamedTypeSymbol? EventShapeAttribute => GetOrResolveType("PolyType.EventShapeAttribute", ref _EventShapeAttribute);
public INamedTypeSymbol? DerivedTypeShapeAttribute => GetOrResolveType("PolyType.DerivedTypeShapeAttribute", ref _DerivedTypeShapeAttribute);
// ... etc
```

**Option B: Use string-based lookup in PolyTypeModelGenerator**

```csharp
protected virtual bool HasConstructorShapeAttribute(IMethodSymbol ctor)
{
    return ctor.GetAttributes().Any(a => 
        a.AttributeClass?.ToDisplayString() == "PolyType.ConstructorShapeAttribute");
}
```

### Conclusion

**The recommended approach is to create a `PolyTypeModelGenerator` intermediate class** in `PolyType.Roslyn` that:

1. Extends `TypeDataModelGenerator` 
2. Implements all PolyType attribute parsing via virtual hook methods
3. Allows users who want PolyType behavior to extend it directly
4. Allows users who want custom attributes to extend `TypeDataModelGenerator` and override only the hooks

This cleanly separates:
- **Core logic** (`TypeDataModelGenerator`) - constructor heuristics, property resolution, type normalization
- **Attribute parsing** (`PolyTypeModelGenerator`) - PolyType-specific attribute handling
- **Source generation** (`Parser`) - diagnostics, model export, F# specifics

Implementation effort is moderate - primarily extracting attribute parsing into `PolyTypeModelGenerator` and adding hook methods to `TypeDataModelGenerator`.
