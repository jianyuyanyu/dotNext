using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotNext.Runtime;

public sealed class ValueReferenceTests : Test
{
    [Fact]
    public static void MutableFieldRef()
    {
        var obj = new MyClass() { AnotherField = string.Empty };
        var reference = new ValueReference<int>(obj, ref obj.Field);

        obj.Field = 20;
        Equal(obj.Field, reference.Value);

        reference.Value = 42;
        Equal(obj.Field, reference.Value);
        Empty(obj.AnotherField);
    }
    
    [Fact]
    public static void ImmutableFieldRef()
    {
        var obj = new MyClass() { AnotherField = string.Empty };
        var reference = new ReadOnlyValueReference<int>(obj, in obj.Field);

        obj.Field = 20;
        Equal(obj.Field, reference.Value);
        
        Equal(obj.Field, reference.Value);
        Empty(obj.AnotherField);
    }
    
    [Fact]
    public static void MutableToImmutableRef()
    {
        var obj = new MyClass() { AnotherField = string.Empty };
        var reference = new ValueReference<int>(obj, ref obj.Field);
        ReadOnlyValueReference<int> roReference = reference;

        obj.Field = 20;
        Equal(roReference.Value, reference.Value);

        reference.Value = 42;
        Equal(roReference.Value, reference.Value);
    }
    
    [Fact]
    public static void MutableRefEquality()
    {
        var obj = new MyClass() { AnotherField = string.Empty };
        var reference1 = new ValueReference<int>(obj, ref obj.Field);
        var reference2 = new ValueReference<int>(obj, ref obj.Field);

        Equal(reference1, reference2);
    }

    [Fact]
    public static void ImmutableRefEquality()
    {
        var obj = new MyClass() { AnotherField = string.Empty };
        var reference1 = new ReadOnlyValueReference<int>(obj, in obj.Field);
        var reference2 = new ReadOnlyValueReference<int>(obj, in obj.Field);

        Equal(reference1, reference2);
    }

    [Fact]
    public static void ReferenceToArray()
    {
        var array = new string[1];
        var reference = new ValueReference<string>(array, 0)
        {
            Value = "Hello, world!"
        };

        Same(array[0], reference.Value);
        Same(array[0], reference.ToString());
    }

    [Fact]
    public static void MutableEmptyRef()
    {
        var reference = default(ValueReference<float>);
        True(reference.IsEmpty);
        Null(reference.ToString());
    }
    
    [Fact]
    public static void ImmutableEmptyRef()
    {
        var reference = default(ReadOnlyValueReference<float>);
        True(reference.IsEmpty);
        Null(reference.ToString());
    }

    [Fact]
    public static void AnonymousValue()
    {
        var reference = new ValueReference<int>(42);
        Equal(42, reference.Value);

        ReadOnlyValueReference<int> roRef = reference;
        Equal(42, roRef.Value);
    }

    [Fact]
    public static void StaticObjectAccess()
    {
        var reference = new ValueReference<string>(ref MyClass.StaticObject)
        {
            Value = "Hello, world",
        };

        GC.Collect(3, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        
        True(reference == new ValueReference<string>(ref MyClass.StaticObject));
        Same(MyClass.StaticObject, reference.Value);
    }
    
    [Fact]
    public static void StaticValueTypeAccess()
    {
        var reference = new ReadOnlyValueReference<int>(in MyClass.StaticValueType);
        MyClass.StaticValueType = 42;

        GC.Collect(3, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        
        True(reference == new ReadOnlyValueReference<int>(in MyClass.StaticValueType));
        Equal(MyClass.StaticValueType, reference.Value);
    }

    [Fact]
    public static void IncorrectReference()
    {
        byte[] empty = [];
        Throws<ArgumentOutOfRangeException>(() => new ValueReference<byte>(empty, ref MemoryMarshal.GetArrayDataReference(empty)));
        Throws<ArgumentOutOfRangeException>(() => new ReadOnlyValueReference<byte>(empty, ref MemoryMarshal.GetArrayDataReference(empty)));
    }

    [Fact]
    public static void ReferenceSize()
    {
        Equal(Unsafe.SizeOf<ValueReference<float>>(), nint.Size + nint.Size);
    }

    [Fact]
    public static void BoxedValueInterop()
    {
        var boxedInt = BoxedValue<int>.Box(42);
        ValueReference<int> reference = boxedInt;

        boxedInt.Value = 56;
        Equal(boxedInt.Value, reference.Value);
    }

    private record class MyClass : IResettable
    {
        internal static string StaticObject;
        
        [FixedAddressValueType]
        internal static int StaticValueType;
        
        internal int Field;
        internal string AnotherField;

        public virtual void Reset()
        {
            
        }
    }
}