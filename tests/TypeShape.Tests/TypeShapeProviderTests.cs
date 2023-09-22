﻿using System.Collections;
using System.Diagnostics;
using System.Reflection;
using TypeShape.Applications.RandomGenerator;
using TypeShape.ReflectionProvider;
using Xunit;

namespace TypeShape.Tests;

public abstract class TypeShapeProviderTests
{
    protected abstract ITypeShapeProvider Provider { get; }
    protected abstract bool SupportsNonPublicMembers { get; }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void TypeShapeReportsExpectedInfo<T>(TestCase<T> testCase)
    {
        _ = testCase; // not used here
        ITypeShape<T>? shape = Provider.GetShape<T>();

        Assert.NotNull(shape);
        Assert.Same(Provider, shape.Provider);
        Assert.Equal(typeof(T), shape.Type);
        Assert.Equal(typeof(T), shape.AttributeProvider);

        TypeKind expectedKind = GetExpectedTypeKind(testCase.Value, Provider is ReflectionTypeShapeProvider);
        Assert.Equal(expectedKind, shape.Kind);

        static TypeKind GetExpectedTypeKind(T value, bool isReflectionProvider)
        {
            if (typeof(T).IsEnum)
            {
                return TypeKind.Enum;
            }
            else if (typeof(T).IsValueType && default(T) is null)
            {
                return TypeKind.Nullable;
            }

            if (value is IEnumerable && value is not string)
            {
                if (typeof(T).GetDictionaryKeyValueTypes() != null)
                {
                    return isReflectionProvider ? TypeKind.Dictionary | TypeKind.Enumerable : TypeKind.Dictionary;
                }

                return TypeKind.Enumerable;
            }

            return TypeKind.None;
        }
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void GetProperties<T>(TestCase<T> testCase)
    {
        _ = testCase; // not used here
        ITypeShape<T>? shape = Provider.GetShape<T>();
        Assert.NotNull(shape);

        var visitor = new PropertyTestVisitor();
        foreach (IPropertyShape property in shape.GetProperties(nonPublic: SupportsNonPublicMembers, includeFields: true))
        {
            Assert.Equal(typeof(T), property.DeclaringType.Type);
            property.Accept(visitor, testCase.Value);
        }
    }

    private sealed class PropertyTestVisitor : TypeShapeVisitor
    {
        public override object? VisitProperty<TDeclaringType, TPropertyType>(IPropertyShape<TDeclaringType, TPropertyType> property, object? state)
        {
            TDeclaringType obj = (TDeclaringType)state!;
            TPropertyType propertyType = default!;

            if (property.HasGetter)
            {
                var getter = property.GetGetter();
                propertyType = getter(ref obj);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => property.GetGetter());
            }

            if (property.HasSetter)
            {
                var setter = property.GetSetter();
                setter(ref obj, propertyType);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => property.GetSetter());
            }

            return null;
        }
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void GetConstructors<T>(TestCase<T> testCase)
    {
        _ = testCase; // not used here
        ITypeShape<T>? shape = Provider.GetShape<T>();
        Assert.NotNull(shape);

        var visitor = new ConstructorTestVisitor();
        foreach (IConstructorShape ctor in shape.GetConstructors(nonPublic: SupportsNonPublicMembers))
        {
            Assert.Equal(typeof(T), ctor.DeclaringType.Type);
            ctor.Accept(visitor, typeof(T));
        }
    }

    private sealed class ConstructorTestVisitor : TypeShapeVisitor
    {
        public override object? VisitConstructor<TDeclaringType, TArgumentState>(IConstructorShape<TDeclaringType, TArgumentState> constructor, object? state)
        {
            var expectedType = (Type)state!;
            Assert.Equal(typeof(TDeclaringType), expectedType);

            int parameterCount = constructor.ParameterCount;
            IConstructorParameterShape[] parameters = constructor.GetParameters().ToArray();
            Assert.Equal(parameterCount, parameters.Length);

            if (parameterCount == 0)
            {
                var defaultCtor = constructor.GetDefaultConstructor();
                TDeclaringType defaultValue = defaultCtor();
                Assert.NotNull(defaultValue);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => constructor.GetDefaultConstructor());
            }

            int i = 0;
            TArgumentState argumentState = constructor.GetArgumentStateConstructor().Invoke();
            foreach (IConstructorParameterShape parameter in parameters)
            {
                Assert.Equal(i++, parameter.Position);
                argumentState = (TArgumentState)parameter.Accept(this, argumentState)!;
            }

            var parameterizedCtor = constructor.GetParameterizedConstructor();
            Assert.NotNull(parameterizedCtor);

            if (typeof(TDeclaringType).Assembly == Assembly.GetExecutingAssembly())
            {
                TDeclaringType value = parameterizedCtor.Invoke(argumentState);
                Assert.NotNull(value);
            }
            return null;
        }

        public override object? VisitConstructorParameter<TArgumentState, TParameter>(IConstructorParameterShape<TArgumentState, TParameter> parameter, object? state)
        {
            var argState = (TArgumentState)state!;
            var setter = parameter.GetSetter();

            TParameter? value = parameter.HasDefaultValue ? parameter.DefaultValue : default;
            setter(ref argState, value!);
            return argState;
        }
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void GetEnumType<T>(TestCase<T> testCase)
    {
        _ = testCase; // not used here
        ITypeShape<T>? shape = Provider.GetShape<T>();
        Assert.NotNull(shape);

        if (shape.Kind.HasFlag(TypeKind.Enum))
        {
            IEnumShape enumType = shape.GetEnumShape();
            Assert.Equal(typeof(T), enumType.Type.Type);
            Assert.Equal(typeof(T).GetEnumUnderlyingType(), enumType.UnderlyingType.Type);
            var visitor = new EnumTestVisitor();
            enumType.Accept(visitor, typeof(T));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => shape.GetEnumShape());
        }
    }

    private sealed class EnumTestVisitor : TypeShapeVisitor
    {
        public override object? VisitEnum<TEnum, TUnderlying>(IEnumShape<TEnum, TUnderlying> enumType, object? state)
        {
            var type = (Type)state!;
            Assert.Equal(typeof(TEnum), type);
            Assert.Equal(typeof(TUnderlying), type.GetEnumUnderlyingType());
            return null;
        }
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void GetNullableType<T>(TestCase<T> testCase)
    {
        _ = testCase; // not used here
        ITypeShape<T>? shape = Provider.GetShape<T>();
        Assert.NotNull(shape);

        if (shape.Kind.HasFlag(TypeKind.Nullable))
        {
            INullableShape nullableType = shape.GetNullableShape();
            Assert.Equal(typeof(T), nullableType.Type.Type);
            Assert.Equal(typeof(T).GetGenericArguments()[0], nullableType.ElementType.Type);
            var visitor = new NullableTestVisitor();
            nullableType.Accept(visitor, typeof(T));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => shape.GetNullableShape());
        }
    }

    private sealed class NullableTestVisitor : TypeShapeVisitor
    {
        public override object? VisitNullable<T>(INullableShape<T> nullable, object? state) where T : struct
        {
            var type = (Type)state!;
            Assert.Equal(typeof(T?), type);
            Assert.Equal(typeof(T), nullable.ElementType.Type);
            return null;
        }
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void GetDictionaryType<T>(TestCase<T> testCase)
    {
        ITypeShape<T>? shape = Provider.GetShape<T>();
        Assert.NotNull(shape);

        if (shape.Kind.HasFlag(TypeKind.Dictionary))
        {
            IDictionaryShape dictionaryType = shape.GetDictionaryShape();
            Assert.Equal(typeof(T), dictionaryType.Type.Type);

            Type[]? keyValueTypes = typeof(T).GetDictionaryKeyValueTypes();
            Assert.NotNull(keyValueTypes);
            Assert.Equal(keyValueTypes[0], dictionaryType.KeyType.Type);
            Assert.Equal(keyValueTypes[1], dictionaryType.ValueType.Type);

            var visitor = new DictionaryTestVisitor();
            dictionaryType.Accept(visitor, testCase.Value);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => shape.GetDictionaryShape());
        }
    }

    private sealed class DictionaryTestVisitor : TypeShapeVisitor
    {
        public override object? VisitDictionary<TDictionary, TKey, TValue>(IDictionaryShape<TDictionary, TKey, TValue> dictionaryShape, object? state)
        {
            var dictionary = (TDictionary)state!;
            var getter = dictionaryShape.GetGetDictionary();
            int count = getter(dictionary).Count();

            if (dictionaryShape.IsMutable)
            {
                var adder = dictionaryShape.GetAddKeyValuePair();
                RandomGenerator<TKey> keyGenerator = RandomGenerator.Create((ITypeShape<TKey>)dictionaryShape.KeyType);
                TKey newKey = keyGenerator.GenerateValue(size: 1000, seed: 42);
                adder(ref dictionary, new(newKey, default!));
                Assert.Equal(count + 1, getter(dictionary).Count());
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => dictionaryShape.GetAddKeyValuePair());
            }

            return null;
        }
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void GetEnumerableType<T>(TestCase<T> testCase)
    {
        _ = testCase; // not used here
        ITypeShape<T>? shape = Provider.GetShape<T>();
        Assert.NotNull(shape);

        if (shape.Kind.HasFlag(TypeKind.Enumerable))
        {
            IEnumerableShape enumerableType = shape.GetEnumerableShape();
            Assert.Equal(typeof(T), enumerableType.Type.Type);

            if (typeof(T).GetCompatibleGenericInterface(typeof(IEnumerable<>)) is { } enumerableImplementation)
            {
                Assert.Equal(enumerableImplementation.GetGenericArguments()[0], enumerableType.ElementType.Type);
            }
            else if (typeof(T).IsArray)
            {
                Assert.Equal(typeof(T).GetElementType(), enumerableType.ElementType.Type);
            }
            else
            {
                Assert.Equal(typeof(object), enumerableType.ElementType.Type);
            }

            var visitor = new EnumerableTestVisitor();
            enumerableType.Accept(visitor, testCase.Value);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => shape.GetEnumerableShape());
        }
    }

    private sealed class EnumerableTestVisitor : TypeShapeVisitor
    {
        public override object? VisitEnumerable<TEnumerable, TElement>(IEnumerableShape<TEnumerable, TElement> enumerableShape, object? state)
        {
            var enumerable = (TEnumerable)state!;
            var getter = enumerableShape.GetGetEnumerable();
            int count = getter(enumerable).Count();

            if (enumerableShape.IsMutable)
            {
                var adder = enumerableShape.GetAddElement();
                RandomGenerator<TElement> elementGenerator = RandomGenerator.Create((ITypeShape<TElement>)enumerableShape.ElementType);
                TElement newElement = elementGenerator.GenerateValue(size: 1000, seed: 42);
                adder(ref enumerable, newElement);
                Assert.Equal(count + 1, getter(enumerable).Count());
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => enumerableShape.GetAddElement());
            }

            return null;
        }
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void ReturnsExpectedAttributeProviders<T>(TestCase<T> testCase)
    {
        if (testCase.IsTuple)
        {
            return; // tuples don't report attribute metadata.
        }

        ITypeShape<T> shape = Provider.GetShape<T>()!;

        foreach (IPropertyShape property in shape.GetProperties(nonPublic: SupportsNonPublicMembers, includeFields: true))
        {
            ICustomAttributeProvider? attributeProvider = property.AttributeProvider;
            Assert.NotNull(attributeProvider);

            if (property.IsField)
            {
                FieldInfo fieldInfo = Assert.IsAssignableFrom<FieldInfo>(attributeProvider);
                Assert.Equal(typeof(T), fieldInfo.ReflectedType);
                Assert.Equal(property.Name, fieldInfo.Name);
                Assert.Equal(property.PropertyType.Type, fieldInfo.FieldType);
            }
            else
            {
                PropertyInfo propertyInfo = Assert.IsAssignableFrom<PropertyInfo>(attributeProvider);
                Assert.True(propertyInfo.DeclaringType!.IsAssignableFrom(typeof(T)));
                Assert.Equal(property.Name, propertyInfo.Name);
                Assert.Equal(property.PropertyType.Type, propertyInfo.PropertyType);
                Assert.True(!property.HasGetter || propertyInfo.CanRead);
                Assert.True(!property.HasSetter || propertyInfo.CanWrite);
            }
        }

        foreach (IConstructorShape constructor in shape.GetConstructors(nonPublic: SupportsNonPublicMembers))
        {
            ICustomAttributeProvider? attributeProvider = constructor.AttributeProvider;
            if (attributeProvider is null)
            {
                continue;
            }

            MethodBase ctorInfo = Assert.IsAssignableFrom<MethodBase>(attributeProvider);
            Assert.True(ctorInfo is MethodInfo { IsStatic: true } or ConstructorInfo);
            Assert.True(typeof(T).IsAssignableFrom(ctorInfo is MethodInfo m ? m.ReturnType : ctorInfo.DeclaringType));
            ParameterInfo[] parameters = ctorInfo.GetParameters();
            Assert.True(parameters.Length <= constructor.ParameterCount);

            int i = 0;
            foreach (IConstructorParameterShape ctorParam in constructor.GetParameters())
            {
                if (i < parameters.Length)
                {
                    ParameterInfo actualParameter = parameters[i];
                    Assert.Equal(actualParameter.Position, ctorParam.Position);
                    Assert.Equal(actualParameter.ParameterType, ctorParam.ParameterType.Type);
                    Assert.Equal(actualParameter.Name, ctorParam.Name);

                    ParameterInfo paramInfo = Assert.IsAssignableFrom<ParameterInfo>(ctorParam.AttributeProvider);
                    Assert.Equal(actualParameter.Position, paramInfo.Position);
                    Assert.Equal(actualParameter.Name, paramInfo.Name);
                    Assert.Equal(actualParameter.ParameterType, paramInfo.ParameterType);
                }
                else
                {
                    MemberInfo memberInfo = Assert.IsAssignableFrom<MemberInfo>(ctorParam.AttributeProvider);

                    Assert.Equal(typeof(T), memberInfo.DeclaringType);
                    Assert.Equal(memberInfo.Name, ctorParam.Name);
                    Assert.False(ctorParam.HasDefaultValue);
                    Assert.Equal(i, ctorParam.Position);
                    Assert.True(memberInfo is PropertyInfo or FieldInfo);

                    if (memberInfo is PropertyInfo p)
                    {
                        Assert.Equal(p.PropertyType, ctorParam.ParameterType.Type);
                        Assert.NotNull(p.SetMethod);
                    }
                    else if (memberInfo is FieldInfo f)
                    {
                        Assert.Equal(f.FieldType, ctorParam.ParameterType.Type);
                        Assert.False(f.IsInitOnly);
                    }
                }

                i++;
            }
        }
    }

    [Theory]
    [MemberData(nameof(TestTypes.GetTestCases), MemberType = typeof(TestTypes))]
    public void ReturnsExpectedNullabilityAnnotations<T>(TestCase<T> testCase)
    {
        if (testCase.IsTuple)
        {
            return; // tuples don't report attribute metadata.
        }

        ITypeShape<T>? shape = Provider.GetShape<T>();
        Assert.NotNull(shape);

        foreach (IPropertyShape property in shape.GetProperties(nonPublic: SupportsNonPublicMembers, includeFields: true))
        {
            MemberInfo memberInfo = Assert.IsAssignableFrom<MemberInfo>(property.AttributeProvider);

            memberInfo.GetNonNullableReferenceInfo(out bool isGetterNonNullable, out bool isSetterNonNullable);
            Assert.Equal(isGetterNonNullable, property.IsGetterNonNullableReferenceType);
            Assert.Equal(isSetterNonNullable, property.IsSetterNonNullableReferenceType);
        }

        foreach (IConstructorShape constructor in shape.GetConstructors(nonPublic: SupportsNonPublicMembers))
        {
            ICustomAttributeProvider? attributeProvider = constructor.AttributeProvider;
            if (attributeProvider is null)
            {
                continue;
            }

            MethodBase ctorInfo = Assert.IsAssignableFrom<MethodBase>(attributeProvider);
            ParameterInfo[] parameters = ctorInfo.GetParameters();
            Assert.True(parameters.Length <= constructor.ParameterCount);

            foreach (IConstructorParameterShape ctorParam in constructor.GetParameters())
            {
                if (ctorParam.AttributeProvider is ParameterInfo pInfo)
                {
                    bool isNonNullableReferenceType = pInfo.IsNonNullableReferenceType();
                    Assert.Equal(isNonNullableReferenceType, ctorParam.IsNonNullableReferenceType);
                }
                else
                {
                    MemberInfo memberInfo = Assert.IsAssignableFrom<MemberInfo>(ctorParam.AttributeProvider);
                    memberInfo.GetNonNullableReferenceInfo(out _, out bool isSetterNonNullable);
                    Assert.Equal(isSetterNonNullable, ctorParam.IsNonNullableReferenceType);
                }
            }
        }
    }
}

public static class ReflectionHelpers
{
    public static Type[]? GetDictionaryKeyValueTypes(this Type type)
    {
        if (type.GetCompatibleGenericInterface(typeof(IReadOnlyDictionary<,>)) is { } rod)
        {
            return rod.GetGenericArguments();
        }

        if (type.GetCompatibleGenericInterface(typeof(IDictionary<,>)) is { } d)
        {
            return d.GetGenericArguments();
        }

        if (typeof(IDictionary).IsAssignableFrom(type))
        {
            return new [] { typeof(object), typeof(object) };
        }

        return null;
    }

    public static Type? GetCompatibleGenericInterface(this Type type, Type genericInterface)
    {
        if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface)
        {
            return type;
        }

        foreach (Type interfaceTy in type.GetInterfaces())
        {
            if (interfaceTy.IsGenericType && interfaceTy.GetGenericTypeDefinition() == genericInterface)
            {
                return interfaceTy;
            }
        }

        return null;
    }

    public static void GetNonNullableReferenceInfo(this MemberInfo memberInfo, out bool isGetterNonNullable, out bool isSetterNonNullable)
    {
        if (GetNullabilityInfo(memberInfo) is NullabilityInfo info)
        {
            isGetterNonNullable = info.ReadState is NullabilityState.NotNull;
            isSetterNonNullable = info.WriteState is NullabilityState.NotNull;
        }
        else
        {
            isGetterNonNullable = false;
            isSetterNonNullable = false;
        }
    }

    public static bool IsNonNullableReferenceType(this ParameterInfo parameterInfo)
    {
        if (GetNullabilityInfo(parameterInfo) is NullabilityInfo info)
        {
            // Workaround for https://github.com/dotnet/runtime/issues/92487
            if (parameterInfo.Member.TryGetGenericMethodDefinition() is MethodBase genericMethod &&
                genericMethod.GetParameters()[parameterInfo.Position] is { ParameterType: { IsGenericParameter: true } typeParam })
            {
                Attribute? attr = typeParam.GetCustomAttributes().FirstOrDefault(attr =>
                {
                    Type attrType = attr.GetType();
                    return attrType.Namespace == "System.Runtime.CompilerServices" && attrType.Name == "NullableAttribute";
                });

                byte[]? nullableFlags = (byte[])attr?.GetType().GetField("NullableFlags")?.GetValue(attr)!;
                return nullableFlags[0] == 1;
            }

            return info.WriteState is NullabilityState.NotNull;
        }
        else
        {
            return false;
        }
    }

    public static MethodBase? TryGetGenericMethodDefinition(this MemberInfo methodBase)
    {
        Debug.Assert(methodBase is MethodInfo or ConstructorInfo);

        if (methodBase.DeclaringType!.IsGenericType)
        {
            Type genericTypeDef = methodBase.DeclaringType.GetGenericTypeDefinition();
            MethodBase[] methods = methodBase.MemberType is MemberTypes.Constructor
                ? genericTypeDef.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                : genericTypeDef.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            MethodBase match = methods.First(m => m.MetadataToken == methodBase.MetadataToken);
            return ReferenceEquals(match, methodBase) ? null : match;
        }

        if (methodBase is MethodInfo { IsGenericMethod: true } methodInfo)
        {
            return methodInfo.GetGenericMethodDefinition();
        }

        return null;
    }

    private static NullabilityInfo? GetNullabilityInfo(ICustomAttributeProvider memberInfo)
    {
        Debug.Assert(memberInfo is PropertyInfo or FieldInfo or ParameterInfo);

        switch (memberInfo)
        {
            case PropertyInfo prop:
                return prop.PropertyType.IsValueType ? null : new NullabilityInfoContext().Create(prop);

            case FieldInfo field:
                return field.FieldType.IsValueType ? null : new NullabilityInfoContext().Create(field);

            case ParameterInfo parameter:
                return parameter.ParameterType.IsValueType ? null : new NullabilityInfoContext().Create(parameter);

            default:
                return null;
        }
    }
}

public sealed class TypeShapeProviderTests_Reflection : TypeShapeProviderTests
{
    protected override ITypeShapeProvider Provider { get; } = new ReflectionTypeShapeProvider(useReflectionEmit: false);
    protected override bool SupportsNonPublicMembers => true;
}

public sealed class TypeShapeProviderTests_ReflectionEmit : TypeShapeProviderTests
{
    protected override ITypeShapeProvider Provider { get; } = new ReflectionTypeShapeProvider(useReflectionEmit: true);
    protected override bool SupportsNonPublicMembers => true;
}

public sealed class TypeShapeProviderTests_SourceGen : TypeShapeProviderTests
{
    protected override ITypeShapeProvider Provider { get; } = SourceGenTypeShapeProvider.Default;
    protected override bool SupportsNonPublicMembers => false;
}
