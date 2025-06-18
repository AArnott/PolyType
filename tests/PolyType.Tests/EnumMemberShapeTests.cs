using PolyType.Utilities;

namespace PolyType.Tests;

public abstract partial class EnumMemberShapeTests(ProviderUnderTest providerUnderTest)
{
    [Fact]
    public void EnumMemberShapeTest()
    {
        var enumShape = (IEnumTypeShape<TestEnum, byte>)providerUnderTest.Provider.Resolve<TestEnum>();
        Assert.Equal(3, enumShape.Members.Count);
        Assert.Equal(0, enumShape.Members["FirstValue"]);
        Assert.Equal(5, enumShape.Members["Second"]);
        Assert.Equal(8, enumShape.Members["3rd"]);
    }

    [Fact]
    public void IsDefinedValueOrCombinationOfValues()
    {
        var enumShape = (IEnumTypeShape<TestEnum, byte>)providerUnderTest.Provider.Resolve<TestEnum>();
        Assert.True(enumShape.IsDefinedValueOrCombinationOfValues(TestEnum.Second));
        Assert.False(enumShape.IsDefinedValueOrCombinationOfValues((TestEnum)3));
        Assert.True(enumShape.IsDefinedValueOrCombinationOfValues(TestEnum.Third));

        var enumFlagsShape = (IEnumTypeShape<TestFlagsEnum, int>)providerUnderTest.Provider.Resolve<TestFlagsEnum>();
        Assert.True(enumFlagsShape.IsDefinedValueOrCombinationOfValues(TestFlagsEnum.One));
        Assert.True(enumFlagsShape.IsDefinedValueOrCombinationOfValues(TestFlagsEnum.Three));
        Assert.True(enumFlagsShape.IsDefinedValueOrCombinationOfValues(TestFlagsEnum.TwoAndThree));
        Assert.False(enumFlagsShape.IsDefinedValueOrCombinationOfValues((TestFlagsEnum)0x10));
    }

    [Fact]
    public void EnumerateContributingFlags()
    {
        var enumShape = (IEnumTypeShape<TestEnum, byte>)providerUnderTest.Provider.Resolve<TestEnum>();
        Assert.Equal([nameof(TestEnum.Second)], enumShape.EnumerateContributingFlags(TestEnum.Second, out byte remainingByte));
        Assert.Equal(0, remainingByte);
        Assert.Equal([], enumShape.EnumerateContributingFlags((TestEnum)3, out remainingByte));
        Assert.Equal(3, remainingByte);
        Assert.Equal(["3rd"], enumShape.EnumerateContributingFlags(TestEnum.Third, out remainingByte));
        Assert.Equal(0, remainingByte);

        var enumFlagsShape = (IEnumTypeShape<TestFlagsEnum, int>)providerUnderTest.Provider.Resolve<TestFlagsEnum>();
        Assert.Equal([nameof(TestFlagsEnum.One)], enumFlagsShape.EnumerateContributingFlags(TestFlagsEnum.One, out int remainingInt));
        Assert.Equal(0, remainingInt);
        Assert.Equal([nameof(TestFlagsEnum.Three)], enumFlagsShape.EnumerateContributingFlags(TestFlagsEnum.Three, out remainingInt));
        Assert.Equal(0, remainingInt);
        Assert.Equal(["two", "Three"], enumFlagsShape.EnumerateContributingFlags(TestFlagsEnum.TwoAndThree, out remainingInt));
        Assert.Equal(0, remainingInt);
        Assert.Equal([], enumFlagsShape.EnumerateContributingFlags((TestFlagsEnum)0x10, out remainingInt));
        Assert.Equal(0x10, remainingInt);
        Assert.Equal(["two"], enumFlagsShape.EnumerateContributingFlags(TestFlagsEnum.Two | (TestFlagsEnum)0x10, out remainingInt));
        Assert.Equal(0x10, remainingInt);
    }

    public enum TestEnum : byte
    {
        [EnumMemberShape(Name = "FirstValue")]
        First,
        Second = 5,
        [EnumMemberShape(Name = "3rd")]
        Third = 8,
    }

    [Flags]
    public enum TestFlagsEnum
    {
        One = 0x1,
        [EnumMemberShape(Name = "two")]
        Two = 0x2,
        Three = 0x4,
        TwoAndThree = Two | Three,
    }

    [GenerateShape<TestEnum>]
    [GenerateShape<TestFlagsEnum>]
    protected partial class Witness;

    public sealed class Reflection() : EnumMemberShapeTests(ReflectionProviderUnderTest.NoEmit);
    public sealed class ReflectionEmit() : EnumMemberShapeTests(ReflectionProviderUnderTest.Emit);
    public sealed class SourceGen() : EnumMemberShapeTests(new SourceGenProviderUnderTest(Witness.ShapeProvider));
}
