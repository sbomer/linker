// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections; 
using System.Collections.Generic;
using ILLink.Shared.TypeSystemProxy;

using MultiValue = ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>;

namespace ILLink.Shared.DataFlow
{
    struct Maybe<T> : IEquatable<Maybe<T>>
        where T : struct, IEquatable<T>
    {
        public T? MaybeValue;

        public Maybe (T value) => MaybeValue = value;

        public bool Equals (Maybe<T> other) => MaybeValue?.Equals (other.MaybeValue) ?? other.MaybeValue == null;

        public override bool Equals (object? obj) => obj is Maybe<T> other && Equals (other);

        public override int GetHashCode () => MaybeValue?.GetHashCode () ?? 0;

        public Maybe<T> Clone () {
            if (MaybeValue is not T value)
                return default;

            if (value is IDeepCopyValue<T> copyValue)
                return new (copyValue.DeepCopy ());
                
            return new (value);
        }
    }

    struct MaybeLattice<T, TValueLattice> : ILattice<Maybe<T>>
        where T : struct, IEquatable<T>
        where TValueLattice : ILattice<T>
    {
        public readonly TValueLattice ValueLattice;

        public MaybeLattice (TValueLattice valueLattice)
        {
            ValueLattice = valueLattice;
            Top = default;
        }

        public Maybe<T> Top { get; }

        public Maybe<T> Meet (Maybe<T> left, Maybe<T> right)
        {
            if (left.MaybeValue is not T leftValue)
                return right.Clone ();

            if (right.MaybeValue is not T rightValue)
                return left.Clone ();

            return new Maybe<T> (ValueLattice.Meet (leftValue, rightValue));
        }
    }

    // public struct InterproceduralState : IEquatable<InterproceduralState>,
    //     IEnumerable<KeyValuePair<MethodProxy, Maybe<DefaultValueDictionary<HoistedLocalKey, MultiValue>>>>
    // {
    //     DefaultValueDictionary<MethodProxy, Maybe<DefaultValueDictionary<HoistedLocalKey, MultiValue>>> MethodToHoistedLocals;

    //     public InterproceduralState () => MethodToHoistedLocals = new (defaultValue: new (null));

    //     public IEnumerator<KeyValuePair<MethodProxy, Maybe<DefaultValueDictionary<HoistedLocalKey, MultiValue>>>> GetEnumerator ()
    //         => MethodToHoistedLocals.GetEnumerator ();

    //     IEnumerator IEnumerable.GetEnumerator () => GetEnumerator ();
    // }

    // public readonly struct InterproceduralStateLattice : ILattice<InterproceduralState>
    // {
    //     public InterproceduralStateLattice ()
    //     {
            
    //     }

    //     public InterproceduralState Top => new ();

    //     public InterproceduralState Meet (InterproceduralState left, InterproceduralState right) {

    //     }
    // }
}