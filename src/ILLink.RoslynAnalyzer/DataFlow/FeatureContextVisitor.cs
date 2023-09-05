// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ILLink.Shared.DataFlow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using ILLink.Shared.TypeSystemProxy;
using Microsoft.CodeAnalysis.Operations;
namespace ILLink.RoslynAnalyzer.DataFlow
{
	// Visitor which tracks the values of locals in a block. It provides extension points that get called
	// whenever a value that comes from a tracked local reference flows into one of the following:
	// - field
	// - parameter
	// - method return
	public class FeatureContextVisitor
		: OperationWalker<FeatureContext, FeatureContext?>
	{
        // Returning null means that the invocation is not a supported feature check.
		public override FeatureContext? VisitInvocation (IInvocationOperation operation, FeatureContext argument)
		{
            var method = new MethodProxy (operation.TargetMethod);
            if (method.Name is not "IsDynamicCodeSupported" || !method.IsDeclaredOnType ("System.Runtime.CompilerServices.RuntimeFeature"))
                return null;

            // Supported feature check.
            // TODO: use an enum for the features!
            return new FeatureContext (new ValueSet<string> ("DynamicCode"));

            // TODO: should result be impacted by the input context?
		}
		public override FeatureContext? VisitPropertyReference (IPropertyReferenceOperation operation, FeatureContext argument)
		{
			var property = operation.Property;
			if (property.Name == "IsDynamicCodeSupported" && property.ContainingType is INamedTypeSymbol type && type.HasName ("System.Runtime.CompilerServices.RuntimeFeature"))
				return new FeatureContext (new ValueSet<string> ("DynamicCode"));

			return null;
		}
	}
}
