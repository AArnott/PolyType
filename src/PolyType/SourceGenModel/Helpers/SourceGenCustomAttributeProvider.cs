using PolyType.Abstractions;
using PolyType.SourceGenModel.Helpers;

namespace PolyType.SourceGenModel;

/// <summary>
/// Provides a source-generator based implementation of <see cref="IGenericCustomAttributeProvider"/>.
/// </summary>
internal sealed class SourceGenCustomAttributeProvider(Func<SourceGenAttributeInfo[]> attributeFactory) : IGenericCustomAttributeProvider
{
    internal static IGenericCustomAttributeProvider Create(Func<SourceGenAttributeInfo[]>? attributeFactory) =>
        attributeFactory is null ? EmptyCustomAttributeProvider.Instance : new SourceGenCustomAttributeProvider(attributeFactory);

    public TAttribute? GetCustomAttribute<TAttribute>(bool inherit = true) where TAttribute : Attribute
    {
        foreach (SourceGenAttributeInfo attributeInfo in attributeFactory())
        {
            if ((inherit || !attributeInfo.IsInherited) && attributeInfo.Attribute is TAttribute typedAttribute)
            {
                return typedAttribute;
            }
        }

        return null;
    }

    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>(bool inherit = true) where TAttribute : Attribute
    {
        foreach (SourceGenAttributeInfo attributeInfo in attributeFactory())
        {
            if ((inherit || !attributeInfo.IsInherited) && attributeInfo.Attribute is TAttribute typedAttribute)
            {
                yield return typedAttribute;
            }
        }
    }

    public object[] GetCustomAttributes(bool inherit)
    {
        List<Attribute> list = [];
        foreach (SourceGenAttributeInfo attributeInfo in attributeFactory())
        {
            if (inherit || !attributeInfo.IsInherited)
            {
                list.Add(attributeInfo.Attribute);
            }
        }

        return list.ToArray();
    }

    public object[] GetCustomAttributes(Type attributeType, bool inherit)
    {
        Throw.IfNull(attributeType);

        List<Attribute> list = [];
        foreach (SourceGenAttributeInfo attributeInfo in attributeFactory())
        {
            if ((inherit || !attributeInfo.IsInherited) && IsInstanceOf(attributeType, attributeInfo.Attribute))
            {
                list.Add(attributeInfo.Attribute);
            }
        }

        return list.ToArray();
    }

    public bool IsDefined<TAttribute>(bool inherit = true) where TAttribute : Attribute
    {
        foreach (SourceGenAttributeInfo attributeInfo in attributeFactory())
        {
            if ((inherit || !attributeInfo.IsInherited) && attributeInfo.Attribute is TAttribute)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInstanceOf(Type attributeType, Attribute attribute)
#if NETWASM
        // NetWasm: System.Type has no IsInstanceOfType/IsAssignableFrom, so only exact type matches are found.
        => attributeType == typeof(Attribute) || attribute.GetType() == attributeType;
#else
        => attributeType.IsInstanceOfType(attribute);
#endif

    public bool IsDefined(Type attributeType, bool inherit)
    {
        Throw.IfNull(attributeType);

        foreach (SourceGenAttributeInfo attributeInfo in attributeFactory())
        {
            if ((inherit || !attributeInfo.IsInherited) && IsInstanceOf(attributeType, attributeInfo.Attribute))
            {
                return true;
            }
        }

        return false;
    }
}