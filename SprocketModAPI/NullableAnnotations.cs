// 编译器要求的可空注解特性。BepInEx 与 Il2CppInterop 的程序集把同名特性内嵌为 internal，
// 引用它们后编译器无法复用，只能由本程序集提供。
using System;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method |
        AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event |
        AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter,
        AllowMultiple = false,
        Inherited = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte value) => NullableFlags = new[] { value };

        public NullableAttribute(byte[] value) => NullableFlags = value;

        public byte[] NullableFlags { get; }
    }

    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method |
        AttributeTargets.Interface | AttributeTargets.Delegate | AttributeTargets.Enum,
        AllowMultiple = false,
        Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte value) => Flag = value;

        public byte Flag { get; }
    }

    [AttributeUsage(AttributeTargets.Module, AllowMultiple = false, Inherited = false)]
    internal sealed class NullablePublicOnlyAttribute : Attribute
    {
        public NullablePublicOnlyAttribute(bool value) => Value = value;

        public bool Value { get; }
    }
}
