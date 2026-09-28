namespace ZeroAlloc.StateMachine.Generator;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
/// Element-wise equatable wrapper around <see cref="ImmutableArray{T}"/>. The default equality
/// of <see cref="ImmutableArray{T}"/> compares the underlying array by reference, so a model
/// holding one never compares equal to the model rebuilt from the same source, and the pipeline
/// never serves it from the cache.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _values;

    public EquatableArray(ImmutableArray<T> values) => _values = values;

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    private ImmutableArray<T> Values => _values.IsDefault ? ImmutableArray<T>.Empty : _values;

    public int Length => Values.Length;

    public bool IsEmpty => Values.IsEmpty;

    public T this[int index] => Values[index];

    /// <summary>
    /// A loop, not LINQ's <c>Any</c>, which would box the array into an enumerator. Also what
    /// <see cref="ImmutableArray{T}"/> offers as an extension, so callers read the same.
    /// </summary>
    public bool Any(Func<T, bool> predicate)
    {
        foreach (var value in Values)
        {
            if (predicate(value)) return true;
        }
        return false;
    }

    public static implicit operator EquatableArray<T>(ImmutableArray<T> values) => new(values);

    public bool Equals(EquatableArray<T> other)
    {
        var left = Values;
        var right = other.Values;
        if (left.Length != right.Length) return false;

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < left.Length; i++)
        {
            if (!comparer.Equals(left[i], right[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var value in Values)
            hash = unchecked((hash * 31) + (value is null ? 0 : value.GetHashCode()));
        return hash;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>A struct enumerator, so <c>foreach</c> over the array does not box.</summary>
    public ImmutableArray<T>.Enumerator GetEnumerator() => Values.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Values).GetEnumerator();
}
