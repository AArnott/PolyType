using PolyType.Abstractions;
using PolyType.ReflectionProvider;

namespace PolyType.Utilities;

/// <summary>
/// Extension methods for working with enum values.
/// </summary>
public static class CompositeEnum
{
    /// <summary>
    /// Breaks up a potentially composite enum value into its contributing flags.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <typeparam name="TUnderlying">The underlying type of the enum.</typeparam>
    /// <param name="shape">The enum shape.</param>
    /// <param name="value">The value of the enum.</param>
    /// <param name="remainingFlags">Receives any leftover bits that could not be attributed to a defined flag.</param>
    /// <returns>A string array of contributing flags.</returns>
    public static string[] EnumerateContributingFlags<TEnum, TUnderlying>(this IEnumTypeShape<TEnum, TUnderlying> shape, TEnum value, out TUnderlying remainingFlags)
        where TEnum : struct, Enum
        where TUnderlying : unmanaged
    {
        ulong valueAsULong = ConvertToUInt64<TEnum, TUnderlying>(value);
        if (EnumData<TEnum>.IsFlagsEnum)
        {
            ulong remainingBits = valueAsULong;
            List<string> contributingFlags = [];

            foreach (KeyValuePair<string, TUnderlying> member in shape.Members)
            {
                ulong fieldValue = ConvertToUInt64(member.Value);
                if (fieldValue == 0 ? valueAsULong == 0 : (remainingBits & fieldValue) == fieldValue)
                {
                    remainingBits &= ~fieldValue;
                    contributingFlags.Add(member.Key);

                    if (remainingBits == 0)
                    {
                        remainingFlags = default;
                        return contributingFlags.ToArray();
                    }
                }
            }

            remainingFlags = ConvertFromUInt64<TUnderlying>(remainingBits);
            return contributingFlags.ToArray();
        }
        else
        {
            foreach (KeyValuePair<string, TUnderlying> member in shape.Members)
            {
                ulong fieldValue = ConvertToUInt64(member.Value);
                if (fieldValue == valueAsULong)
                {
                    remainingFlags = default;
                    return [member.Key];
                }
            }

            remainingFlags = (TUnderlying)(object)value;
            return [];
        }
    }

    /// <summary>
    /// Gets a value indicating whether the specified enum value is defined in the enum type shape.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <typeparam name="TUnderlying">The underlying type of the enum.</typeparam>
    /// <param name="shape">The enum shape.</param>
    /// <param name="value">The value of the enum.</param>
    /// <returns><see langword="true" /> if <paramref name="value"/> is composed exclusively of defined values; otherwise <see langword="false" />.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the enum is not backed by an integral type.</exception>
    public static bool IsDefinedValueOrCombinationOfValues<TEnum, TUnderlying>(this IEnumTypeShape<TEnum, TUnderlying> shape, TEnum value)
        where TEnum : struct, Enum
        where TUnderlying : unmanaged
    {
        if (!EnumData<TEnum>.IsInteger)
        {
            throw new InvalidOperationException($"The type {typeof(TEnum).FullName} must be backed by an integral type to use this method.");
        }

        ulong valueAsULong = ConvertToUInt64<TEnum, TUnderlying>(value);
        if (EnumData<TEnum>.IsFlagsEnum)
        {
            ulong remainingBits = valueAsULong;

            foreach (KeyValuePair<string, TUnderlying> member in shape.Members)
            {
                ulong fieldValue = ConvertToUInt64(member.Value);
                if (fieldValue == 0 ? valueAsULong == 0 : (remainingBits & fieldValue) == fieldValue)
                {
                    remainingBits &= ~fieldValue;

                    if (remainingBits == 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        else
        {
            foreach (KeyValuePair<string, TUnderlying> member in shape.Members)
            {
                ulong fieldValue = ConvertToUInt64(member.Value);
                if (fieldValue == valueAsULong)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static ulong ConvertToUInt64<TEnum, TUnderlying>(TEnum value)
        where TEnum : struct, Enum
        where TUnderlying : unmanaged
        => ConvertToUInt64((TUnderlying)(object)value);

    private static ulong ConvertToUInt64<TUnderlying>(TUnderlying value)
        where TUnderlying : unmanaged
    {
        unchecked
        {
            if (typeof(TUnderlying) == typeof(ulong))
            {
                return (ulong)(object)value;
            }
            else if (typeof(TUnderlying) == typeof(long))
            {
                return (ulong)(long)(object)value;
            }
            else if (typeof(TUnderlying) == typeof(uint))
            {
                return (uint)(object)value;
            }
            else if (typeof(TUnderlying) == typeof(int))
            {
                return (uint)(int)(object)value;
            }
            else if (typeof(TUnderlying) == typeof(ushort))
            {
                return (ushort)(object)value;
            }
            else if (typeof(TUnderlying) == typeof(short))
            {
                return (ushort)(short)(object)value;
            }
            else if (typeof(TUnderlying) == typeof(byte))
            {
                return (byte)(object)value;
            }
            else if (typeof(TUnderlying) == typeof(sbyte))
            {
                return (byte)(sbyte)(object)value;
            }
            else
            {
                throw new NotSupportedException();
            }
        }
    }

    private static TUnderlying ConvertFromUInt64<TUnderlying>(ulong value)
        where TUnderlying : unmanaged
    {
        unchecked
        {
            if (typeof(TUnderlying) == typeof(ulong))
            {
                return (TUnderlying)(object)value;
            }
            else if (typeof(TUnderlying) == typeof(long))
            {
                return (TUnderlying)(object)(long)value;
            }
            else if (typeof(TUnderlying) == typeof(uint))
            {
                return (TUnderlying)(object)(uint)value;
            }
            else if (typeof(TUnderlying) == typeof(int))
            {
                return (TUnderlying)(object)(int)value;
            }
            else if (typeof(TUnderlying) == typeof(ushort))
            {
                return (TUnderlying)(object)(ushort)value;
            }
            else if (typeof(TUnderlying) == typeof(short))
            {
                return (TUnderlying)(object)(short)value;
            }
            else if (typeof(TUnderlying) == typeof(byte))
            {
                return (TUnderlying)(object)(byte)value;
            }
            else if (typeof(TUnderlying) == typeof(sbyte))
            {
                return (TUnderlying)(object)(sbyte)value;
            }
            else
            {
                throw new NotSupportedException();
            }
        }
    }

    private static class EnumData<TEnum>
        where TEnum : struct, Enum
    {
        /// <summary>
        /// Indicates whether the enum is backed by an integer type.
        /// </summary>
        /// <remarks>
        /// Although C# enums can only be backed by integral types, <see href="https://github.com/stakx/ecma-335/blob/master/docs/ii.14.3-enums.md">ECMA-335 allows enums</see>
        /// to be backed by <see cref="bool"/>, <see cref="char"/>, <see cref="nint"/> and <see cref="nuint"/> as well.
        /// </remarks>
        internal static readonly bool IsInteger = Type.GetTypeCode(typeof(TEnum)) is >= TypeCode.SByte and <= TypeCode.UInt64;

        internal static readonly bool IsFlagsEnum = typeof(TEnum).IsDefined<FlagsAttribute>();
    }
}
