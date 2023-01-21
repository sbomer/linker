// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using ILLink.Shared.DataFlow;

namespace ILLink.RoslynAnalyzer.DataFlow
{
	// We may be in a context where a feature is known to be enabled,
	// for example inside an if (Feature.IsEnabled) block.
	// We may also be in a context where a feature is known to be disabled,
	// for example inside an if (!Feature.IsEnabled) block.
	// We may also be in a context where we don't know anything about the feature,
	// for example in top-level code.
	// Calls to feature-gated APIs are allowed if the feature is known to be enabled,
	// and disallowed if the feature is disabled or unknown.
	// We use a lattice to track the feature context.
	// The values are binary - either the feature is enabled, or it is disabled/unknown.
	// The lattice representation tracks the features that are necessarily enabled on all paths.

	// We start with all features in the disabled/unknown state (represented by the empty set).
	// When we encounter an if (Feature.IsEnabled) block, we meet the current feature context with
	// the feature context where the feature is enabled.

	// The meet operation is set intersection, not union, representing the fact that we are interested
	// in features which are enabled on all paths to a given point in the code.
	// This is in contrast to the local variable lattice, where we are interested in all values that
	// may be set on any path to a given point in the code.

	internal sealed partial record FeatureContextValue : SingleValue
	{
		public readonly FeatureContext FeatureContext;

		public FeatureContextValue (FeatureContext featureContext) => FeatureContext = featureContext;

		public override SingleValue DeepCopy ()
		{
			return new FeatureContextValue (new FeatureContext (new ValueSet<string> (FeatureContext.Features)));
		}

		
	}

	public struct FeatureContext : IEquatable<FeatureContext>
	{
		public ValueSet<string> Features;

		public FeatureContext (ValueSet<string> features)
		{
			Features = features;
		}

		public bool Equals (FeatureContext other) => Features.Equals (other.Features);
	}

	public readonly struct FeatureContextLattice : ILattice<FeatureContext>
	{
		static bool created = false;

		public FeatureContextLattice ()
			: this (new List<string> () {
				"DynamicCode"
			})
		{ //throw new InvalidOperationException("Should not create without parameters!");
		}

		private FeatureContextLattice (List<string> featureNames)
		{
			// if (created)
				// throw new InvalidOperationException ();
			created = true;
			Top = new FeatureContext (new ValueSet<string> (featureNames));
		}

		public FeatureContext Top { get; }

		// Unlike local variable values, where we track all values that may be set on any path,
		// for features we are interested in the feature settings that must be set on all paths.
		// Therefore the Meet operation is set intersection, not union.

		// BUT the zero-intersection should be an identity. So it should represent all possible feature
		// settings as being set.
		public FeatureContext Meet (FeatureContext left, FeatureContext right)
		{
			return new FeatureContext (ValueSet<string>.Intersection (left.Features, right.Features));
		}
	}
}