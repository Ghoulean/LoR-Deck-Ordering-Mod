using System;
using System.Reflection;

namespace DeckOrdering
{
    internal static class Reflect
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static Func<TOwner, TValue> FieldGetter<TOwner, TValue>(string name)
        {
            FieldInfo field = typeof(TOwner).GetField(name, Instance)
                ?? throw new MissingFieldException(typeof(TOwner).FullName, name);
            return owner => (TValue)field.GetValue(owner);
        }

        internal static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name, Instance) ?? throw new MissingMethodException(type.FullName, name);
        }
    }
}
