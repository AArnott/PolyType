using PolyType.SourceGenerator.Analyzers;

namespace PolyType.SourceGenerator.UnitTests.Analyzers;

using Microsoft.CodeAnalysis;
using Xunit;
using VerifyCS = CodeFixVerifier<PropertyInitializerDefaultValueAnalyzer, PropertyInitializerDefaultValueCodeFixProvider>;

public class PropertyInitializerDefaultValueAnalyzerTests
{
    [Fact]
    public async Task PropertyWithInitializer_NoDefaultValueAttribute_ReportsDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public int {|PT0023:Value|} { get; set; } = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PropertyWithInitializer_HasDefaultValueAttribute_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                [DefaultValue(42)]
                public int Value { get; set; } = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PropertyWithoutInitializer_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public int Value { get; set; }
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task FieldWithInitializer_NoDefaultValueAttribute_ReportsDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public int {|PT0023:Value|} = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task FieldWithInitializer_HasDefaultValueAttribute_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                [DefaultValue(42)]
                public int Value = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StaticProperty_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public static int Value { get; set; } = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ReadOnlyField_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public readonly int Value = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ConstField_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public const int Value = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PropertyWithoutSetter_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public int Value { get; } = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TypeWithoutGenerateShapeAttribute_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            public class MyClass
            {
                public int Value { get; set; } = 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PropertyWithStringInitializer_ReportsDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public string {|PT0023:Name|} { get; set; } = "default";
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PropertyWithBoolInitializer_ReportsDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public bool {|PT0023:IsActive|} { get; set; } = true;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task MultiplePropertiesWithInitializers_ReportsMultipleDiagnostics()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public int {|PT0023:Value1|} { get; set; } = 1;
                public int {|PT0023:Value2|} { get; set; } = 2;
                [DefaultValue(3)]
                public int Value3 { get; set; } = 3;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PropertyWithNonConstInitializer_NoDiagnostic()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public int Value { get; set; } = GetDefaultValue();

                private static int GetDefaultValue() => 42;
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TypeReferencedByGenerateShapeFor_CanBeAnalyzed()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            [GenerateShapeFor(typeof(MyOtherClass))]
            public partial class Witness { }

            [GenerateShape]
            public partial class MyClass
            {
                public int {|PT0023:Value|} { get; set; } = 42;
            }

            public class MyOtherClass
            {
                public string Name { get; set; } = "test";
            }
            """;

        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task CodeFix_PropertyWithIntInitializer_AddsDefaultValueAttribute()
    {
        string source = /* lang=c#-test */ """
            using PolyType;

            [GenerateShape]
            public partial class MyClass
            {
                public int {|PT0023:Value|} { get; set; } = 42;
            }
            """;

        string fixedSource = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                [DefaultValue(42)]
                public int Value { get; set; } = 42;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task CodeFix_PropertyWithStringInitializer_AddsDefaultValueAttribute()
    {
        string source = /* lang=c#-test */ """
            using PolyType;

            [GenerateShape]
            public partial class MyClass
            {
                public string {|PT0023:Name|} { get; set; } = "default";
            }
            """;

        string fixedSource = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                [DefaultValue("default")]
                public string Name { get; set; } = "default";
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task CodeFix_PropertyWithBoolInitializer_AddsDefaultValueAttribute()
    {
        string source = /* lang=c#-test */ """
            using PolyType;

            [GenerateShape]
            public partial class MyClass
            {
                public bool {|PT0023:IsActive|} { get; set; } = true;
            }
            """;

        string fixedSource = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                [DefaultValue(true)]
                public bool IsActive { get; set; } = true;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task CodeFix_FieldWithIntInitializer_AddsDefaultValueAttribute()
    {
        string source = /* lang=c#-test */ """
            using PolyType;

            [GenerateShape]
            public partial class MyClass
            {
                public int {|PT0023:Value|} = 42;
            }
            """;

        string fixedSource = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                [DefaultValue(42)]
                public int Value = 42;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource);
    }

    [Fact]
    public async Task CodeFix_PreservesExistingUsings()
    {
        string source = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                public int {|PT0023:Value|} { get; set; } = 42;
            }
            """;

        string fixedSource = /* lang=c#-test */ """
            using PolyType;
            using System.ComponentModel;

            [GenerateShape]
            public partial class MyClass
            {
                [DefaultValue(42)]
                public int Value { get; set; } = 42;
            }
            """;

        await VerifyCS.VerifyCodeFixAsync(source, fixedSource);
    }
}
