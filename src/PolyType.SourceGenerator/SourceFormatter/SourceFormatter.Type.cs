﻿using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;
using PolyType.Roslyn;
using PolyType.SourceGenerator.Model;

namespace PolyType.SourceGenerator;

internal sealed partial class SourceFormatter
{
    private SourceText FormatProvidedType(TypeShapeProviderModel provider, TypeShapeModel type)
    {
        string generatedPropertyType = $"global::PolyType.Abstractions.ITypeShape<{type.Type.FullyQualifiedName}>";
        string generatedFactoryMethodName = $"__Create_{type.SourceIdentifier}";
        string generatedFieldName = "__" + type.SourceIdentifier;

        var writer = new SourceWriter();
        StartFormatSourceFile(writer, provider.ProviderDeclaration);

        writer.WriteLine(provider.ProviderDeclaration.TypeDeclarationHeader);
        writer.WriteLine('{');
        writer.Indentation++;

        writer.WriteLine("/// <summary>Gets a generated shape for the specified type.</summary>");
        writer.WriteLine("#nullable disable annotations // Use nullable-oblivious property type", disableIndentation: true);
        writer.WriteLine($"public {generatedPropertyType} {type.SourceIdentifier} => {generatedFieldName} ??= {generatedFactoryMethodName}();");
        writer.WriteLine("#nullable enable annotations // Use nullable-oblivious property type", disableIndentation: true);
        writer.WriteLine($"private {generatedPropertyType}? {generatedFieldName};");
        writer.WriteLine();

        switch (type)
        {
            case ObjectShapeModel objectShapeModel:
                FormatObjectTypeShapeFactory(writer, generatedFactoryMethodName, objectShapeModel);
                break;

            case EnumShapeModel enumShapeModel:
                FormatEnumTypeShapeFactory(writer, generatedFactoryMethodName, enumShapeModel);
                break;

            case NullableShapeModel nullableShapeModel:
                FormatNullableTypeShapeFactory(writer, generatedFactoryMethodName, nullableShapeModel);
                break;
            
            case SurrogateShapeModel surrogateShapeModel:
                FormatSurrogateTypeShapeFactory(writer, generatedFactoryMethodName, surrogateShapeModel);
                break;

            case EnumerableShapeModel enumerableShapeModel:
                FormatEnumerableTypeShapeFactory(writer, generatedFactoryMethodName, enumerableShapeModel);
                break;

            case DictionaryShapeModel dictionaryShapeModel:
                FormatDictionaryTypeShapeFactory(writer, generatedFactoryMethodName, dictionaryShapeModel);
                break;

            default:
                Debug.Fail($"Should not be reached {type.GetType().Name}");
                throw new InvalidOperationException();
        }

        writer.Indentation--;
        writer.WriteLine('}');
        EndFormatSourceFile(writer);

        return writer.ToSourceText();
    }
}
