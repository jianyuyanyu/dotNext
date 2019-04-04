﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DotNext
{
    /// <summary>
    /// Provides strongly typed way to reflect enum type.
    /// </summary>
    /// <typeparam name="E">Enum type to reflect.</typeparam>
    /// <seealso href="https://github.com/dotnet/corefx/issues/34077">EnumMember API</seealso>
    public readonly struct Enum<E>: IEquatable<E>, IComparable<E>, IFormattable, IComparable<Enum<E>>
        where E : struct, Enum
    {
        private readonly struct Tuple: IEquatable<Tuple>
        {
            internal readonly string Name;
            internal readonly E Value;
            
            internal Tuple(string name)
            {
                Name = name;
                Value = default;
            }

            internal Tuple(E value)
            {
                Value = value;
                Name = default;
            }

            public static implicit operator Tuple(string name) => new Tuple(name);
            public static implicit operator Tuple(E value) => new Tuple(value);
            
            public static implicit operator KeyValuePair<string, E>(Tuple tuple) => new KeyValuePair<string, E>(tuple.Name, tuple.Value);
            public static implicit operator KeyValuePair<E, string>(Tuple tuple) => new KeyValuePair<E, string>(tuple.Value, tuple.Name);

            public bool Equals(Tuple other)
                => Name is null ? other.Name is null && EqualityComparer<E>.Default.Equals(Value, other.Value) : Name == other.Name;

            public override bool Equals(object other) => other is Tuple t && Equals(t);
            public override int GetHashCode() => Name is null ? EqualityComparer<E>.Default.GetHashCode() : Name.GetHashCode();
        }

        private sealed class Mapping : Dictionary<Tuple, Enum<E>>
        {
            internal Mapping(out Enum<E> min, out Enum<E> max)
            {
                var names = Enum.GetNames(typeof(E));
                var values = (E[])Enum.GetValues(typeof(E));
                min = max = default;
                for (var index = 0L; index < names.LongLength; index++)
                {
                    var entry = new Enum<E>(values[index], names[index]);
                    Add(new Tuple(entry.Name), entry);
                    Add(new Tuple(entry.Value), entry);
                    //detect min and max
                    min = entry.Min(min);
                    max = entry.Max(max);
                }
            }
        }

        private static readonly ReadOnlyDictionary<Tuple, Enum<E>> mapping;

        /// <summary>
        /// Maximum enum value.
        /// </summary>
        public static readonly Enum<E> MaxValue;
        
        /// <summary>
        /// Minimum enum value.
        /// </summary>
        public static readonly Enum<E> MinValue;

        static Enum()
        {
            mapping = new ReadOnlyDictionary<Tuple, Enum<E>>(new Mapping(out MinValue, out MaxValue));
        }

        public static bool IsDefined(E value) => mapping.ContainsKey(value);

        public static bool IsDefined(string name) => mapping.ContainsKey(name);

        /// <summary>
        /// Gets enum member by its value.
        /// </summary>
        /// <param name="value">The enum value.</param>
        /// <returns>The enum member.</returns>
        public static Enum<E> GetMember(E value) => mapping[value];

        public static bool TryGetMember(E value, out Enum<E> member) => mapping.TryGetValue(value, out member);

        public static bool TryGetMember(string name, out Enum<E> member) => mapping.TryGetValue(name, out member);

        /// <summary>
        /// Gets enum member by its name.
        /// </summary>
        /// <param name="name">The name of the enum value.</param>
        /// <returns>The enum member.</returns>
        public static Enum<E> GetMember(string name) => mapping[name];

        /// <summary>
        /// Gets declared enum members.
        /// </summary>
        public static IReadOnlyCollection<Enum<E>> Members => mapping.Values;

        /// <summary>
        /// Gets the underlying type of the specified enumeration.
        /// </summary>
        public static Type UnderlyingType => Enum.GetUnderlyingType(typeof(E));

        private readonly string name;

        private Enum(E value, string name)
        {
            Value = value;
            this.name = name;
        }

        /// <summary>
        /// Represents value of the enum member.
        /// </summary>
        public E Value { get; }

        /// <summary>
        /// Represents name of the enum member.
        /// </summary>
        public string Name => name ?? Value.ToString();

        /// <summary>
        /// Converts typed enum wrapper into actual enum value.
        /// </summary>
        /// <param name="en">Enum wrapper to convert.</param>
        public static implicit operator E(Enum<E> en) => en.Value;

        /// <summary>
        /// Compares this enum value with other.
        /// </summary>
        /// <param name="other">Other value to compare.</param>
        /// <returns>Comparison result.</returns>
        public int CompareTo(E other) => Comparer<E>.Default.Compare(Value, other);

        int IComparable<Enum<E>>.CompareTo(Enum<E> other) => CompareTo(other);

        /// <summary>
        /// Determines whether this value equals to the other enum value.
        /// </summary>
        /// <param name="other">Other value to compare.</param>
        /// <returns>Equality check result.</returns>
        public bool Equals(E other) => EqualityComparer<E>.Default.Equals(Value, other);

        /// <summary>
        /// Determines whether this value equals to the other enum value.
        /// </summary>
        /// <param name="other">Other value to compare.</param>
        /// <returns>Equality check result.</returns>
        public override bool Equals(object other)
        {
            switch(other)
            {
                case Enum<E> en:
                    return Equals(en);
                case E en:
                    return Equals(en);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets hash code of the enum value.
        /// </summary>
        /// <returns>The hash code of the enum value.</returns>
        public override int GetHashCode() => EqualityComparer<E>.Default.GetHashCode(Value);

        /// <summary>
        /// Returns textual representation of the enum value.
        /// </summary>
        /// <returns>The textual representation of the enum value.</returns>
        public override string ToString() => Value.ToString();

        string IFormattable.ToString(string format, IFormatProvider provider) => Value.ToString();
    }
}