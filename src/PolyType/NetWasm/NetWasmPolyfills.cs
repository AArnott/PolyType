#if NETWASM
namespace PolyType.ReflectionProvider;

// NetWasm (netwasm0.1) proof of concept: the reflection provider (and Shared/Helpers/ReflectionHelpers.cs) is excluded.
// This is the subset of ReflectionHelpers that non-reflection code paths use.
internal static class ReflectionHelpers
{
    // NetWasm has no Enum.IsDefined; validation is skipped.
    public static bool IsEnumDefined<TEnum>(TEnum value)
        where TEnum : struct, Enum => true;
}
#endif
