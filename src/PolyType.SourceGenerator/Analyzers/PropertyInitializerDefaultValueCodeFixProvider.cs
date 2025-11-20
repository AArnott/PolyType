using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;

namespace PolyType.SourceGenerator.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PropertyInitializerDefaultValueCodeFixProvider)), Shared]
public class PropertyInitializerDefaultValueCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add DefaultValueAttribute";

    public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("PT0023");

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the property or field declaration identified by the diagnostic.
        var node = root.FindToken(diagnosticSpan.Start).Parent;
        if (node is null)
        {
            return;
        }

        // Get semantic model
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return;
        }

        // Navigate to the property or field declaration
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            if (ancestor is PropertyDeclarationSyntax propertyDecl && propertyDecl.Initializer is not null)
            {
                // Check if the initializer is a constant value
                var constantValue = semanticModel.GetConstantValue(propertyDecl.Initializer.Value, context.CancellationToken);
                if (!constantValue.HasValue)
                {
                    // Only offer code fix for const initializers
                    return;
                }

                string? initializerValue = Roslyn.Helpers.RoslynHelpers.FormatPrimitiveConstant(
                    semanticModel.GetTypeInfo(propertyDecl.Initializer.Value, context.CancellationToken).Type,
                    constantValue.Value);

                if (initializerValue is null)
                {
                    return;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Title,
                        createChangedDocument: c => AddDefaultValueAttributeToPropertyAsync(context.Document, propertyDecl, initializerValue, c),
                        equivalenceKey: Title),
                    diagnostic);
                return;
            }
            else if (ancestor is VariableDeclaratorSyntax fieldDecl && fieldDecl.Initializer is not null)
            {
                // Check if the initializer is a constant value
                var constantValue = semanticModel.GetConstantValue(fieldDecl.Initializer.Value, context.CancellationToken);
                if (!constantValue.HasValue)
                {
                    // Only offer code fix for const initializers
                    return;
                }

                string? initializerValue = Roslyn.Helpers.RoslynHelpers.FormatPrimitiveConstant(
                    semanticModel.GetTypeInfo(fieldDecl.Initializer.Value, context.CancellationToken).Type,
                    constantValue.Value);

                if (initializerValue is null)
                {
                    return;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: Title,
                        createChangedDocument: c => AddDefaultValueAttributeToFieldAsync(context.Document, fieldDecl, initializerValue, c),
                        equivalenceKey: Title),
                    diagnostic);
                return;
            }
        }
    }

    private static async Task<Document> AddDefaultValueAttributeToPropertyAsync(
        Document document,
        PropertyDeclarationSyntax propertyDecl,
        string initializerValue,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Create the DefaultValue attribute
        var attributeArgument = SyntaxFactory.AttributeArgument(
            SyntaxFactory.ParseExpression(initializerValue));

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("DefaultValue"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(attributeArgument)));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attribute));

        // Add the attribute to the property
        var newPropertyDecl = propertyDecl.AddAttributeLists(attributeList);

        // Replace the old property with the new one
        var newRoot = root.ReplaceNode(propertyDecl, newPropertyDecl);

        // Ensure we have the necessary using directive
        newRoot = EnsureUsingDirective(newRoot, "System.ComponentModel");

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> AddDefaultValueAttributeToFieldAsync(
        Document document,
        VariableDeclaratorSyntax fieldDecl,
        string initializerValue,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Find the field declaration syntax
        var fieldDeclaration = fieldDecl.Ancestors().OfType<FieldDeclarationSyntax>().FirstOrDefault();
        if (fieldDeclaration is null)
        {
            return document;
        }

        // Create the DefaultValue attribute
        var attributeArgument = SyntaxFactory.AttributeArgument(
            SyntaxFactory.ParseExpression(initializerValue));

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("DefaultValue"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(attributeArgument)));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attribute));

        // Add the attribute to the field
        var newFieldDeclaration = fieldDeclaration.AddAttributeLists(attributeList);

        // Replace the old field with the new one
        var newRoot = root.ReplaceNode(fieldDeclaration, newFieldDeclaration);

        // Ensure we have the necessary using directive
        newRoot = EnsureUsingDirective(newRoot, "System.ComponentModel");

        return document.WithSyntaxRoot(newRoot);
    }

    private static SyntaxNode EnsureUsingDirective(SyntaxNode root, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return root;
        }

        // Check if the using directive already exists
        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));
        bool hasUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName);

        if (!hasUsing)
        {
            // Add the using directive
            var newCompilationUnit = compilationUnit.AddUsings(usingDirective);
            return newCompilationUnit;
        }

        return root;
    }
}
