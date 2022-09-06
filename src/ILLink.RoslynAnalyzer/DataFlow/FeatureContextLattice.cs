// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using ILLink.Shared.DataFlow;

namespace ILLink.RoslynAnalyzer.DataFlow
{
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
        public readonly ValueSetLattice<string> FeatureLattice;

        public FeatureContextLattice (ValueSetLattice<string> featureLattice)
        {
            FeatureLattice = featureLattice;
        }

        public FeatureContext Top { get; }

        public FeatureContext Meet (FeatureContext left, FeatureContext right)
        {
            return new FeatureContext {
                Features = FeatureLattice.Meet (left.Features, right.Features)
            };
        }
    }
}