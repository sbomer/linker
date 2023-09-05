// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Helpers;

namespace Mono.Linker.Tests.Cases.DataFlow
{
	[SkipKeptItemsValidation]
	[ExpectedNoWarnings]
	public class FeatureSwitchDataFlow
	{
		public static void Main ()
		{
            CallFeatureUnguarded ();
            CallFeatureGuarded ();
		}

        [ExpectedWarning ("IL3050", "--RequiresDynamicCode--")]
        static void CallFeatureUnguarded ()
        {
            RequiresDynamicCode ();
        }

        static void CallFeatureGuarded ()
        {
            if (RuntimeFeature.IsDynamicCodeSupported)
                RequiresDynamicCode ();
        }

        [RequiresDynamicCode ("--RequiresDynamicCode--")]
        static void RequiresDynamicCode () {}
	}
}
