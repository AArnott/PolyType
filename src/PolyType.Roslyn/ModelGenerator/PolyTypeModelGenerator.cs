using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PolyType.Roslyn.Helpers;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace PolyType.Roslyn;

/// <summary>
/// A <see cref="TypeDataModelGenerator"/> that implements PolyType attribute parsing.
/// This class provides sensible defaults for users who want to use PolyType's attribute-based
/// configuration while still being able to customize behavior by overriding methods.
/// </summary>
/// <remarks>
/// Users who want full PolyType behavior can extend this class directly.
/// Users who want custom attributes can extend <see cref="TypeDataModelGenerator"/> instead.
/// </remarks>
public class PolyTypeModelGenerator : TypeDataModelGenerator
{
    private static readonly IEqualityComparer<(ITypeSymbol Type, string Name)> s_ctorParamComparer =
        CommonHelpers.CreateTupleComparer<ITypeSymbol, string>(
            SymbolEqualityComparer.Default,
            CommonHelpers.CamelCaseInvariantComparer.Instance);

    /// <summary>
    /// Creates a new <see cref="PolyTypeModelGenerator"/> instance.
    /// </summary>
    /// <param name="generationScope">The context symbol used to determine accessibility for processed types.</param>
    /// <param name="knownSymbols">The known symbols cache constructed from the current <see cref="Compilation"/>.</param>
    /// <param name="cancellationToken">The cancellation token to be used by the generator.</param>
    public PolyTypeModelGenerator(ISymbol generationScope, KnownSymbols knownSymbols, CancellationToken cancellationToken)
        : base(generationScope, knownSymbols, cancellationToken)
    {
    }

    /// <summary>
    /// Provide no location for diagnostics by default.
    /// </summary>
    public override Location? DefaultLocation => null;

    /// <summary>
    /// We want to flatten System.Tuple types for consistency with
    /// the reflection-based provider (which caters to F# model types).
    /// </summary>
    protected override bool FlattenSystemTupleTypes => true;

    /// <summary>
    /// Full types used as generic parameters so we must exclude ref structs and static types.
    /// </summary>
    protected override bool IsSupportedType(ITypeSymbol type) =>
        base.IsSupportedType(type) && !type.IsRefLikeType && !type.IsStatic;

    /// <summary>
    /// Include delegate parameter types into generated shapes.
    /// </summary>
    protected override bool IncludeDelegateParameters => true;

    /// <summary>
    /// Determines whether the given property should be included in the object data model.
    /// This implementation supports the PropertyShapeAttribute.
    /// </summary>
    protected override bool IncludeProperty(IPropertySymbol property, out string? customName, out int order, out bool includeGetter, out bool includeSetter)
    {
        if (ParsePropertyShapeAttribute(property, out customName, out order, out bool ignore))
        {
            if (ignore)
            {
                includeGetter = includeSetter = false;
                return false;
            }

            // Use the signature of the base property to determine shape.
            property = property.GetBaseProperty();
            includeGetter = property.GetMethod is not null;
            includeSetter = property.SetMethod is not null;
            return true;
        }

        return base.IncludeProperty(property, out customName, out order, out includeGetter, out includeSetter);
    }

    /// <summary>
    /// Determines whether the given field should be included in the object data model.
    /// This implementation supports the PropertyShapeAttribute.
    /// </summary>
    protected override bool IncludeField(IFieldSymbol field, out string? customName, out int order, out bool includeGetter, out bool includeSetter)
    {
        if (ParsePropertyShapeAttribute(field, out customName, out order, out bool ignore))
        {
            if (ignore)
            {
                includeGetter = includeSetter = false;
                return false;
            }

            includeGetter = true;
            includeSetter = !field.IsReadOnly;
            return true;
        }

        return base.IncludeField(field, out customName, out order, out includeGetter, out includeSetter);
    }

    /// <summary>
    /// Determines whether a property should be considered required.
    /// This implementation supports DataMemberAttribute and PropertyShapeAttribute.
    /// </summary>
    protected override bool? IsRequiredByPolicy(IPropertySymbol member)
    {
        if (member.ContainingType.HasPolyTypeAttribute(KnownSymbols.DataContractAttribute) &&
            member.GetPolyTypeAttribute(KnownSymbols.DataMemberAttribute) is AttributeData dataMemberAttribute &&
            dataMemberAttribute.TryGetPolyTypeNamedArgument("IsRequired", out bool isRequiredDataMember))
        {
            return isRequiredDataMember;
        }

        if (member.GetPolyTypeAttribute(KnownSymbols.PropertyShapeAttribute) is AttributeData shapeAttribute &&
            shapeAttribute.TryGetPolyTypeNamedArgument("IsRequired", out bool isRequiredValue))
        {
            return isRequiredValue;
        }

        return base.IsRequiredByPolicy(member);
    }

    /// <summary>
    /// Determines whether a field should be considered required.
    /// This implementation supports DataMemberAttribute and PropertyShapeAttribute.
    /// </summary>
    protected override bool? IsRequiredByPolicy(IFieldSymbol member)
    {
        if (member.ContainingType.HasPolyTypeAttribute(KnownSymbols.DataContractAttribute) &&
            member.GetPolyTypeAttribute(KnownSymbols.DataMemberAttribute) is AttributeData dataMemberAttribute &&
            dataMemberAttribute.TryGetPolyTypeNamedArgument("IsRequired", out bool isRequiredDataMember))
        {
            return isRequiredDataMember;
        }

        if (member.GetPolyTypeAttribute(KnownSymbols.PropertyShapeAttribute) is AttributeData shapeAttribute &&
            shapeAttribute.TryGetPolyTypeNamedArgument("IsRequired", out bool isRequiredValue))
        {
            return isRequiredValue;
        }

        return base.IsRequiredByPolicy(member);
    }

    /// <summary>
    /// Resolves constructors for the given type using PolyType's attribute-based and heuristic selection.
    /// </summary>
    protected override IEnumerable<IMethodSymbol> ResolveConstructors(ITypeSymbol type, ImmutableArray<PropertyDataModel> properties)
    {
        if (type.IsAbstract || type.TypeKind is TypeKind.Interface)
        {
            return [];
        }

        // Search for constructors that have the [ConstructorShape] attribute. Ignore accessibility modifiers in this step.
        IMethodSymbol[] constructors = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(ctor => ctor is { IsStatic: false, MethodKind: MethodKind.Constructor })
            .Where(ctor => ctor.HasPolyTypeAttribute(KnownSymbols.ConstructorShapeAttribute))
            .ToArray();

        if (constructors.Length == 1)
        {
            return constructors; // Found a unique match, return that.
        }

        if (constructors.Length > 1)
        {
            // We have a conflict. Derived classes can override to report a diagnostic.
            // Pick one using the default heuristic.
            OnMultipleConstructorShapeAttributesFound(type, constructors);
        }
        else
        {
            // Otherwise, just resolve the public constructors on the type.
            constructors = base.ResolveConstructors(type, properties)
                .Where(ctor => ctor.DeclaredAccessibility is Accessibility.Public)
                .ToArray();
        }

        // If the type defines more than one constructors, pick one using the following rules:
        // 1. Minimize the number of required parameters not corresponding to any readable property/field.
        // 2. Maximize the number of parameters that match read-only properties/fields.
        // 3. Minimize the total number of constructor parameters.

        Dictionary<(ITypeSymbol, string), bool> readableProperties = new(s_ctorParamComparer);
        foreach (var prop in properties)
        {
            if (prop.IncludeGetter)
            {
                var key = (prop.PropertyType, prop.Name);
                // For each property, check if it's read-only.
                // If we've already seen this property (by type and name), keep it marked as read-only
                // only if ALL occurrences are read-only (e.g., handling shadowing/overrides).
                bool isCurrentPropertyReadOnly = !prop.IncludeSetter;

                if (readableProperties.TryGetValue(key, out bool isReadOnly))
                {
                    // If already exists, AND with current property's read-only status
                    readableProperties[key] = isReadOnly && isCurrentPropertyReadOnly;
                }
                else
                {
                    // First occurrence, just set to current property's read-only status
                    readableProperties[key] = isCurrentPropertyReadOnly;
                }
            }
        }

        return constructors
            .OrderByDescending(ctor =>
            {
                int matchingReadOnlyMemberParamCount = 0;
                int unmatchedRequiredParamCount = 0;
                foreach (IParameterSymbol param in ctor.Parameters)
                {
                    if (readableProperties.TryGetValue((param.Type, param.Name), out bool isReadOnly))
                    {
                        // Do not count settable members as they can be set after any constructor.
                        if (isReadOnly)
                        {
                            matchingReadOnlyMemberParamCount++;
                        }
                    }
                    else if (!param.IsOptional)
                    {
                        unmatchedRequiredParamCount++;
                    }
                }

                return (-unmatchedRequiredParamCount, matchingReadOnlyMemberParamCount, -ctor.Parameters.Length);
            })
            .Take(1);
    }

    /// <summary>
    /// Called when multiple constructors with the ConstructorShapeAttribute are found.
    /// Derived classes can override to report a diagnostic.
    /// </summary>
    /// <param name="type">The type with multiple constructors.</param>
    /// <param name="constructors">The constructors found.</param>
    protected virtual void OnMultipleConstructorShapeAttributesFound(ITypeSymbol type, IMethodSymbol[] constructors)
    {
        // Default implementation does nothing. Derived classes can report diagnostics.
    }

    /// <summary>
    /// Resolves the method symbols that should be included for the given type.
    /// This implementation supports the MethodShapeAttribute.
    /// </summary>
    protected override IEnumerable<ResolvedMethodSymbol> ResolveMethods(ITypeSymbol type, BindingFlags bindingFlags)
    {
        if (type is not INamedTypeSymbol)
        {
            yield break;
        }

        foreach ((IMethodSymbol method, bool isAmbiguous) in type.ResolveVisibleMembers<IMethodSymbol>())
        {
            if (IncludeMethod(method, bindingFlags, out string? customName))
            {
                yield return new() { CustomName = customName, MethodSymbol = method, IsAmbiguous = isAmbiguous };
            }
        }
    }

    /// <summary>
    /// Determines whether a method should be included in the object data model.
    /// </summary>
    protected virtual bool IncludeMethod(IMethodSymbol method, BindingFlags bindingFlags, out string? customName)
    {
        customName = null;

        if (method.MethodKind is not MethodKind.Ordinary ||
            !SyntaxFacts.IsValidIdentifier(method.Name) ||
            method.IsImplicitlyDeclared ||
            method.HasPolyTypeAttribute(KnownSymbols.CompilerGeneratedAttribute))
        {
            return false; // Skip methods that are special names (getters, setters, events) or compiler-generated.
        }

        if (ParseMethodShapeAttribute(method, out customName, out bool? ignore))
        {
            return ignore is null or false; // Skip methods explicitly marked as ignored.
        }

        if (method.DeclaredAccessibility is not Accessibility.Public || method.MethodKind is not MethodKind.Ordinary)
        {
            return false; // Skip methods that are not public when not annotated.
        }

        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, KnownSymbols.Compilation.ObjectType) ||
            SymbolEqualityComparer.Default.Equals(method.ContainingType, KnownSymbols.SystemValueType))
        {
            return false; // Skip GetHashCode, ToString, Equals, and other object methods.
        }

        BindingFlags requiredFlags = method.IsStatic ? BindingFlags.Public | BindingFlags.Static : BindingFlags.Public | BindingFlags.Instance;
        if ((bindingFlags & requiredFlags) == 0)
        {
            return false; // Skip methods that are not included in the shape by default.
        }

        return true;
    }

    /// <summary>
    /// Resolves the event symbols that should be included for the given type.
    /// This implementation supports the EventShapeAttribute.
    /// </summary>
    protected override IEnumerable<ResolvedEventSymbol> ResolveEvents(ITypeSymbol type, BindingFlags bindingFlags)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            yield break;
        }

        foreach ((IEventSymbol eventSymbol, bool isAmbiguous) in namedType.ResolveVisibleMembers<IEventSymbol>())
        {
            if (IncludeEvent(eventSymbol, bindingFlags, out string? customName))
            {
                yield return new() { CustomName = customName, Event = eventSymbol, IsAmbiguous = isAmbiguous };
            }
        }
    }

    /// <summary>
    /// Determines whether an event should be included in the object data model.
    /// </summary>
    protected virtual bool IncludeEvent(IEventSymbol eventSymbol, BindingFlags bindingFlags, out string? customName)
    {
        customName = null;

        if (!SyntaxFacts.IsValidIdentifier(eventSymbol.Name) ||
            eventSymbol.IsImplicitlyDeclared ||
            eventSymbol.HasPolyTypeAttribute(KnownSymbols.CompilerGeneratedAttribute))
        {
            return false; // Skip events that are explicit interface implementations or compiler-generated.
        }

        if (ParseEventShapeAttribute(eventSymbol, out customName, out bool? ignore))
        {
            return ignore is not true; // Skip events explicitly marked as ignored.
        }

        if (eventSymbol.DeclaredAccessibility is not Accessibility.Public)
        {
            return false; // Skip events that are not public when not annotated.
        }

        BindingFlags requiredFlags = eventSymbol.IsStatic ? BindingFlags.Public | BindingFlags.Static : BindingFlags.Public | BindingFlags.Instance;
        if ((bindingFlags & requiredFlags) == 0)
        {
            return false; // Skip events that are not included in the shape by default.
        }

        return true;
    }

    /// <summary>
    /// Returns the derived types of the given type.
    /// This implementation supports DerivedTypeShapeAttribute and KnownTypeAttribute.
    /// </summary>
    protected override IEnumerable<DerivedTypeModel> ResolveDerivedTypes(ITypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Interface))
        {
            yield break;
        }

        int i = 0;
        ITypeSymbol[]? baseTypeArgs = null;
        HashSet<ITypeSymbol> types = new(SymbolEqualityComparer.Default);
        HashSet<int> tags = new();
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (AttributeData attribute in type.GetAttributes())
        {
            ITypeSymbol? derivedType = null;
            string? name = null;
            int tag = -1;

            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, KnownSymbols.DerivedTypeShapeAttribute))
            {
                ParseDerivedTypeShapeAttribute(attribute, out derivedType, out name, out tag);
            }
            else if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, KnownSymbols.KnownTypeAttribute) &&
                     type.HasPolyTypeAttribute(KnownSymbols.DataContractAttribute))
            {
                if (attribute.ConstructorArguments is not [{ Value: ITypeSymbol dt }])
                {
                    continue;
                }

                derivedType = dt;
            }
            else
            {
                continue;
            }

            if (derivedType is INamedTypeSymbol { IsUnboundGenericType: true } namedDerivedType)
            {
                baseTypeArgs ??= ((INamedTypeSymbol)type).GetRecursiveTypeArguments();
                INamedTypeSymbol? specializedDerivedType = namedDerivedType.OriginalDefinition.ConstructRecursive(baseTypeArgs);
                if (specializedDerivedType is null || !type.IsAssignableFrom(specializedDerivedType))
                {
                    OnDerivedTypeUnsupportedGenerics(type, derivedType, attribute);
                    continue;
                }

                derivedType = specializedDerivedType;
            }
            else if (derivedType is not null && !type.IsAssignableFrom(derivedType))
            {
                OnDerivedTypeNotAssignableToBase(type, derivedType, attribute);
                continue;
            }

            if (derivedType is null)
            {
                continue;
            }

            bool isTagSpecified = tag >= 0;
            tag = isTagSpecified ? tag : i;
            name ??= derivedType.GetDerivedTypeShapeName();

            if (!types.Add(derivedType))
            {
                OnDerivedTypeDuplicateMetadata(type, "type", derivedType.ToDisplayString(), attribute);
                continue;
            }

            if (!tags.Add(tag))
            {
                OnDerivedTypeDuplicateMetadata(type, "tag", tag.ToString(CultureInfo.InvariantCulture), attribute);
                continue;
            }

            if (!names.Add(name))
            {
                OnDerivedTypeDuplicateMetadata(type, "name", name, attribute);
                continue;
            }

            yield return new DerivedTypeModel
            {
                Type = derivedType,
                Name = name,
                Tag = tag,
                IsTagSpecified = isTagSpecified,
                Index = i,
                IsBaseType = SymbolEqualityComparer.Default.Equals(derivedType, type),
            };

            i++;
        }
    }

    /// <summary>
    /// Called when a derived type has unsupported generics.
    /// Derived classes can override to report a diagnostic.
    /// </summary>
    protected virtual void OnDerivedTypeUnsupportedGenerics(ITypeSymbol baseType, ITypeSymbol derivedType, AttributeData attribute)
    {
        // Default implementation does nothing. Derived classes can report diagnostics.
    }

    /// <summary>
    /// Called when a derived type is not assignable to the base type.
    /// Derived classes can override to report a diagnostic.
    /// </summary>
    protected virtual void OnDerivedTypeNotAssignableToBase(ITypeSymbol baseType, ITypeSymbol derivedType, AttributeData attribute)
    {
        // Default implementation does nothing. Derived classes can report diagnostics.
    }

    /// <summary>
    /// Called when duplicate derived type metadata is found.
    /// Derived classes can override to report a diagnostic.
    /// </summary>
    protected virtual void OnDerivedTypeDuplicateMetadata(ITypeSymbol baseType, string metadataType, string duplicateValue, AttributeData attribute)
    {
        // Default implementation does nothing. Derived classes can report diagnostics.
    }

    /// <summary>
    /// Gets the name of given enum value, supporting EnumMemberShapeAttribute and EnumMemberAttribute.
    /// </summary>
    protected override string GetEnumValueName(IFieldSymbol field)
    {
        if (field.GetPolyTypeAttribute(KnownSymbols.EnumMemberShapeAttribute) is { } enumMemberShapeAttr &&
            enumMemberShapeAttr.TryGetPolyTypeNamedArgument("Name", out string? enumMemberShapeName) &&
            enumMemberShapeName is not null)
        {
            return enumMemberShapeName;
        }

        if (field.GetPolyTypeAttribute(KnownSymbols.EnumMemberAttribute) is { } enumMemberAttr &&
            enumMemberAttr.TryGetPolyTypeNamedArgument("Value", out string? enumMemberName) &&
            enumMemberName is not null)
        {
            return enumMemberName;
        }

        return base.GetEnumValueName(field);
    }

    /// <summary>
    /// Parses the PropertyShapeAttribute from a property or field.
    /// </summary>
    protected bool ParsePropertyShapeAttribute(ISymbol propertyOrField, out string? name, out int order, out bool ignore)
    {
        name = null;
        order = 0;
        ignore = false;

        if (propertyOrField.GetPolyTypeAttribute(KnownSymbols.PropertyShapeAttribute) is not AttributeData propertyShapeAttr)
        {
            return false;
        }

        foreach (KeyValuePair<string, TypedConstant> namedArgument in propertyShapeAttr.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "Name":
                    name = (string)namedArgument.Value.Value!;
                    break;
                case "Order":
                    order = (int)namedArgument.Value.Value!;
                    break;
                case "Ignore":
                    ignore = (bool)namedArgument.Value.Value!;
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses the MethodShapeAttribute from a method.
    /// </summary>
    protected bool ParseMethodShapeAttribute(IMethodSymbol method, out string? name, out bool? ignore)
    {
        name = null;
        ignore = null;
        if (method.GetPolyTypeAttribute(KnownSymbols.MethodShapeAttribute) is not AttributeData methodShapeAttr)
        {
            return false;
        }

        foreach (KeyValuePair<string, TypedConstant> namedArgument in methodShapeAttr.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "Name":
                    name = (string)namedArgument.Value.Value!;
                    break;
                case "Ignore":
                    ignore = (bool)namedArgument.Value.Value!;
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses the EventShapeAttribute from an event.
    /// </summary>
    protected bool ParseEventShapeAttribute(IEventSymbol eventSymbol, out string? name, out bool? ignore)
    {
        name = null;
        ignore = null;
        if (eventSymbol.GetPolyTypeAttribute(KnownSymbols.EventShapeAttribute) is not AttributeData eventShapeAttr)
        {
            return false;
        }

        foreach (KeyValuePair<string, TypedConstant> namedArgument in eventShapeAttr.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "Name":
                    name = (string)namedArgument.Value.Value!;
                    break;
                case "Ignore":
                    ignore = (bool)namedArgument.Value.Value!;
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses the DerivedTypeShapeAttribute.
    /// </summary>
    protected static void ParseDerivedTypeShapeAttribute(
        AttributeData attributeData,
        out ITypeSymbol? derivedType,
        out string? name,
        out int tag)
    {
        // TypedConstant is a struct, so FirstOrDefault() on an empty collection returns default
        TypedConstant firstArg = attributeData.ConstructorArguments.FirstOrDefault();
        derivedType = firstArg.Value as ITypeSymbol;
        name = null;
        tag = -1;

        foreach (KeyValuePair<string, TypedConstant> namedArgument in attributeData.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "Name":
                    name = (string)namedArgument.Value.Value!;
                    break;
                case "Tag":
                    tag = (int)namedArgument.Value.Value!;
                    break;
            }
        }
    }
}
