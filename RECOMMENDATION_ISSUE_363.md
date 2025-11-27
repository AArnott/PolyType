# Recommendation Report: Issue #363

## Analysis of TypeDataModelGenerator and Parser Class Hierarchy

### Issue Summary

Issue #363 requests reducing the amount of re-invention required to use `PolyType.Roslyn`. The `Parser` class in the source generator overrides many methods from `TypeDataModelGenerator`, and these overrides contain non-trivial policy that is likely to change over time. The issue author proposes two potential solutions:

1. Provide default behavior that matches PolyType while leaving the methods virtual
2. Provide a derived type that the source generator uses such that consumers can derive from it

### Analysis of Virtual Methods and Overrides

Below is the complete analysis of all virtual methods in `TypeDataModelGenerator` and how they are overridden in `Parser`:

#### Methods in TypeDataModelGenerator Base Class

| Method | Base Implementation | Parser Override | Source-Generator Specific? |
|--------|---------------------|-----------------|---------------------------|
| `DefaultLocation` | Returns first location of GenerationScope | Returns `null` | **No** - just a different default |
| `NormalizeType` | Returns type unchanged | Erases compiler metadata (nullable annotations, tuple labels) | **No** - general normalization |
| `SymbolComparer` | Returns `SymbolEqualityComparer.Default` | Not overridden | N/A |
| `IsSupportedType` | Excludes void, pointers, errors, generic parameters | Adds exclusion of ref-like types and static types | **No** - general policy |
| `IsAccessibleSymbol` | Checks symbol accessibility | Not overridden | N/A |
| `FlattenSystemTupleTypes` | Returns `false` | Returns `true` (for F# support) | **No** - general F# support |
| `IncludeDelegateParameters` | Returns `false` | Returns `true` | **No** - general preference |
| `SkipObjectMemberResolution` | Skips simple types, etc. | Not overridden | N/A |
| `ResolveConstructors` | Resolves accessible constructors | **Complex override** with ConstructorShapeAttribute support and heuristic selection | **Partially** - attribute parsing is source-gen specific, but heuristics are general |
| `ResolveProperties` | Resolves public properties/fields | Adds duplicate name validation with diagnostics | **Partially** - diagnostic reporting is source-gen specific |
| `IncludeProperty` | Public properties only | Adds PropertyShapeAttribute parsing | **Yes** - attribute parsing |
| `IncludeField` | Public fields only | Adds PropertyShapeAttribute parsing | **Yes** - attribute parsing |
| `IsRequiredByPolicy` (property) | Returns `null` | Adds DataMemberAttribute and PropertyShapeAttribute support | **Yes** - attribute parsing |
| `IsRequiredByPolicy` (field) | Returns `null` | Adds DataMemberAttribute and PropertyShapeAttribute support | **Yes** - attribute parsing |
| `ResolveMethods` | Returns empty | Complex override with MethodShapeAttribute support | **Partially** - attribute parsing is source-gen specific |
| `ResolveEvents` | Returns empty | Complex override with EventShapeAttribute support | **Partially** - attribute parsing is source-gen specific |
| `ResolveDerivedTypes` | Returns empty | Complex override with DerivedTypeShapeAttribute/KnownTypeAttribute support | **Partially** - attribute parsing is source-gen specific |
| `MapType` | Core type mapping | Adds TypeShapeAttribute, TypeShapeExtension, F# types, surrogate handling | **Yes** - heavily uses PolyType-specific attributes |
| `MapMethod` | Maps method parameters | Adds generic method rejection + Unit type handling | **Partially** - diagnostic reporting is source-gen specific |
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

**Recommended Approach: Option 1 - Enhanced Base Class with Good Default Implementations**

The analysis shows that the functionality can be cleanly separated into:

1. **General functionality** (can move to `TypeDataModelGenerator`):
   - Constructor selection heuristics (without attribute parsing)
   - Type normalization (erasing compiler metadata)
   - Method/event resolution (public members, excluding Object methods)
   - Duplicate name validation

2. **Attribute-specific functionality** (remain in `Parser`):
   - Parsing of PolyType-specific attributes
   - Diagnostic reporting
   - F# union/option handling (which depends on `PolyTypeKnownSymbols`)

#### Prescription

**Step 1: Enhance `TypeDataModelGenerator` base class with improved default implementations**

Move the following implementations from `Parser` to `TypeDataModelGenerator`:

1. **`NormalizeType`**: Move the `EraseCompilerMetadata` call to the base class
2. **`IsSupportedType`**: Add exclusion of ref-like types and static types
3. **`FlattenSystemTupleTypes`**: Change default to `true` (better F# support)
4. **`IncludeDelegateParameters`**: Change default to `true`
5. **`ResolveConstructors`**: Move the selection heuristic to the base class:
   ```csharp
   protected virtual IEnumerable<IMethodSymbol> ResolveConstructors(...)
   {
       // Base implementation provides the heuristic-based selection
       // without any attribute parsing
       // See current Parser implementation for the logic
   }
   ```
6. **`ResolveMethods`**: Implement default resolution of public ordinary methods (excluding Object methods, compiler-generated, etc.)
7. **`ResolveEvents`**: Implement default resolution of public events

**Step 2: Add new virtual hook methods for attribute parsing**

Add the following virtual methods to `TypeDataModelGenerator` that return default values but can be overridden:

```csharp
// Called by ResolveConstructors to check for attribute-based constructor selection
protected virtual IMethodSymbol? ResolveAttributeMarkedConstructor(ITypeSymbol type)
    => null;

// Called by IncludeProperty to check for custom name/order/ignore
protected virtual bool TryGetPropertyMetadata(IPropertySymbol property, 
    out string? customName, out int order, out bool ignore)
{
    customName = null;
    order = 0;
    ignore = false;
    return false;
}

// Called by IncludeField to check for custom name/order/ignore
protected virtual bool TryGetFieldMetadata(IFieldSymbol field,
    out string? customName, out int order, out bool ignore)
{
    customName = null;
    order = 0;
    ignore = false;
    return false;
}

// Called by ResolveMethods to check for method attributes
protected virtual bool TryGetMethodMetadata(IMethodSymbol method,
    out string? customName, out bool ignore)
{
    customName = null;
    ignore = false;
    return false;
}

// Called by ResolveEvents to check for event attributes  
protected virtual bool TryGetEventMetadata(IEventSymbol eventSymbol,
    out string? customName, out bool ignore)
{
    customName = null;
    ignore = false;
    return false;
}
```

**Step 3: Update `Parser` class to use the new hooks**

The `Parser` class would then:
1. Inherit the improved default implementations
2. Override only the attribute parsing hooks
3. Override `MapType` for F# support and surrogate handling (still needed)
4. Override `ResolveDerivedTypes` for attribute parsing (still needed)

### Benefits of This Approach

1. **Third-party consumers can use `TypeDataModelGenerator` directly** with sensible defaults matching PolyType's behavior
2. **The `Parser` class becomes smaller** and focused only on PolyType-specific attribute handling
3. **No intermediate class needed** - the separation is clean at the method level
4. **No breaking changes** - existing consumers of `TypeDataModelGenerator` continue to work
5. **PolyType.Roslyn remains independent** of the source generator project

### Alternative Consideration

If the above refactoring proves complex due to the tight integration of attribute parsing with the core logic, an alternative would be:

**Option 2: Create an intermediate `PolyTypeModelGenerator` class**

Create a new class in `PolyType.Roslyn`:

```
TypeDataModelGenerator (base - minimal)
    └── PolyTypeModelGenerator (new - with good defaults)
        └── Parser (source generator - with attribute parsing and diagnostics)
```

This would:
- Keep `TypeDataModelGenerator` minimal and stable
- Provide `PolyTypeModelGenerator` with all the "good default" implementations
- Allow `Parser` to focus solely on attribute parsing

However, this would require either:
- Moving `PolyTypeKnownSymbols` to `PolyType.Roslyn` (coupling concerns)
- Or keeping the F# support and surrogate handling in `Parser`

### Conclusion

**The recommended approach is Option 1 (Enhanced Base Class)** because:

1. The functionality is cleanly separable based on the analysis
2. Most overrides in `Parser` either set different defaults or add attribute parsing on top of base functionality
3. The constructor resolution heuristic is the most complex piece and is fully general-purpose
4. No new intermediate classes are needed

Implementation effort is moderate - primarily refactoring existing code from `Parser` to `TypeDataModelGenerator` and adding hook methods for attribute parsing.
