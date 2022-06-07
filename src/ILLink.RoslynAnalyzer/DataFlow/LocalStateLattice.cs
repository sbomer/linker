// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using ILLink.RoslynAnalyzer.DataFlow;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace ILLink.Shared.DataFlow
{
    public partial struct LocalState<TValue> : IEquatable<LocalState<TValue>>
		where TValue : IEquatable<TValue>
	{
		public DefaultValueDictionary<LocalKey, TValue> Dictionary;

		// Stores any operations which are captured by reference in a FlowCaptureOperation.
		// Only stores captures which are assigned through. Captures of the values of operations
		// are tracked as part of the dictionary of values, keyed by LocalKey.
		public DefaultValueDictionary<CaptureId, CapturedReferenceValue> CapturedReferences;

		public LocalState (DefaultValueDictionary<LocalKey, TValue> dictionary, DefaultValueDictionary<CaptureId, CapturedReferenceValue> capturedReferences)
		{
			Dictionary = dictionary;
			CapturedReferences = capturedReferences;
		}

		public LocalState (DefaultValueDictionary<LocalKey, TValue> dictionary)
			: this (dictionary, new DefaultValueDictionary<CaptureId, CapturedReferenceValue> (new CapturedReferenceValue ()))
		{
		}
    }

	public readonly partial struct LocalStateLattice<TValue, TValueLattice> : ILattice<LocalState<TValue>>
		where TValue : struct, IEquatable<TValue>
		where TValueLattice : ILattice<TValue>
	{
		public readonly DictionaryLattice<LocalKey, TValue, TValueLattice> Lattice;

		public readonly DictionaryLattice<CaptureId, CapturedReferenceValue, CapturedReferenceLattice> CapturedReferenceLattice;

		public LocalState<TValue> Top { get; }

		public LocalStateLattice (TValueLattice valueLattice)
		{
			Lattice = new DictionaryLattice<LocalKey, TValue, TValueLattice> (valueLattice);
			CapturedReferenceLattice = new DictionaryLattice<CaptureId, CapturedReferenceValue, CapturedReferenceLattice> (new CapturedReferenceLattice ());
			Top = new (Lattice.Top);
		}

		public partial LocalState<TValue> Meet (LocalState<TValue> left, LocalState<TValue> right)
		{
			var dictionary = Lattice.Meet (left.Dictionary, right.Dictionary);
			var capturedProperties = CapturedReferenceLattice.Meet (left.CapturedReferences, right.CapturedReferences);
			return new LocalState<TValue> (dictionary, capturedProperties);
		}
	}
}