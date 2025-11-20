using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using PolyType.Roslyn.Helpers;
using PolyType.SourceGenerator.Helpers;
using System.Collections.Immutable;
using System.Diagnostics;

namespace PolyType.SourceGenerator.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PropertyInitializerDefaultValueAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Parser.MissingDefaultValueAttribute);

    public override void Initialize(AnalysisContext context)
    {
        if (!Debugger.IsAttached)
        {
            context.EnableConcurrentExecution();
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(context =>
        {
            PolyTypeKnownSymbols knownSymbols = new(context.Compilation);
            if (knownSymbols.GenerateShapeAttribute is null && knownSymbols.GenerateShapeForAttribute is null)
            {
                return;
            }

            INamedTypeSymbol? defaultValueAttribute = context.Compilation.GetTypeByMetadataName("System.ComponentModel.DefaultValueAttribute");
            if (defaultValueAttribute is null)
            {
                return;
            }

            context.RegisterSymbolStartAction(
                context =>
                {
                    INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                    
                    // Only analyze types that have GenerateShapeAttribute or any of their members have it
                    bool hasGenerateShapeAttribute = type.HasAttribute(knownSymbols.GenerateShapeAttribute);
                    bool hasGenerateShapeForAttribute = type.HasAttribute(knownSymbols.GenerateShapeForAttribute);

                    if (!hasGenerateShapeAttribute && !hasGenerateShapeForAttribute)
                    {
                        return;
                    }

                    context.RegisterSyntaxNodeAction(
                        context =>
                        {
                            AnalyzePropertyOrFieldDeclaration(context, knownSymbols, defaultValueAttribute);
                        },
                        SyntaxKind.PropertyDeclaration,
                        SyntaxKind.FieldDeclaration);
                },
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzePropertyOrFieldDeclaration(
        SyntaxNodeAnalysisContext context,
        PolyTypeKnownSymbols knownSymbols,
        INamedTypeSymbol defaultValueAttribute)
    {
        SyntaxNode node = context.Node;
        
        if (node is PropertyDeclarationSyntax propertySyntax)
        {
            AnalyzeProperty(context, propertySyntax, knownSymbols, defaultValueAttribute);
        }
        else if (node is FieldDeclarationSyntax fieldSyntax)
        {
            AnalyzeField(context, fieldSyntax, knownSymbols, defaultValueAttribute);
        }
    }

    private static void AnalyzeProperty(
        SyntaxNodeAnalysisContext context,
        PropertyDeclarationSyntax propertySyntax,
        PolyTypeKnownSymbols knownSymbols,
        INamedTypeSymbol defaultValueAttribute)
    {
        // Check if the property has an initializer
        if (propertySyntax.Initializer is null)
        {
            return;
        }

        // Get the property symbol
        if (context.SemanticModel.GetDeclaredSymbol(propertySyntax, context.CancellationToken) is not IPropertySymbol propertySymbol)
        {
            return;
        }

        // Skip if property already has DefaultValueAttribute
        if (propertySymbol.HasAttribute(defaultValueAttribute))
        {
            return;
        }

        // Skip static properties
        if (propertySymbol.IsStatic)
        {
            return;
        }

        // Skip properties that don't have a setter (they can't be member initializers)
        if (propertySymbol.SetMethod is null)
        {
            return;
        }

        // Get the initializer value as a string
        string? initializerValue = GetInitializerValueAsString(propertySyntax.Initializer.Value, context.SemanticModel, context.CancellationToken);
        if (initializerValue is null)
        {
            return;
        }

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Parser.MissingDefaultValueAttribute,
            propertySyntax.Identifier.GetLocation(),
            propertySymbol.Name,
            propertySymbol.ContainingType.ToDisplayString(),
            initializerValue);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeField(
        SyntaxNodeAnalysisContext context,
        FieldDeclarationSyntax fieldSyntax,
        PolyTypeKnownSymbols knownSymbols,
        INamedTypeSymbol defaultValueAttribute)
    {
        // Check each variable declarator in the field declaration
        foreach (var variable in fieldSyntax.Declaration.Variables)
        {
            // Check if the field has an initializer
            if (variable.Initializer is null)
            {
                continue;
            }

            // Get the field symbol
            if (context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is not IFieldSymbol fieldSymbol)
            {
                continue;
            }

            // Skip if field already has DefaultValueAttribute
            if (fieldSymbol.HasAttribute(defaultValueAttribute))
            {
                continue;
            }

            // Skip static fields
            if (fieldSymbol.IsStatic)
            {
                continue;
            }

            // Skip const fields (they are not member initializers)
            if (fieldSymbol.IsConst)
            {
                continue;
            }

            // Skip readonly fields without a setter (they can't be deserialized into)
            if (fieldSymbol.IsReadOnly)
            {
                continue;
            }

            // Get the initializer value as a string
            string? initializerValue = GetInitializerValueAsString(variable.Initializer.Value, context.SemanticModel, context.CancellationToken);
            if (initializerValue is null)
            {
                continue;
            }

            // Report diagnostic
            var diagnostic = Diagnostic.Create(
                Parser.MissingDefaultValueAttribute,
                variable.Identifier.GetLocation(),
                fieldSymbol.Name,
                fieldSymbol.ContainingType.ToDisplayString(),
                initializerValue);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static string? GetInitializerValueAsString(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        // Try to evaluate the expression as a constant
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue)
        {
            return Roslyn.Helpers.RoslynHelpers.FormatPrimitiveConstant(semanticModel.GetTypeInfo(expression, cancellationToken).Type, constantValue.Value);
        }

        return null;
    }
}
