// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;

namespace Mono.Linker
{
	public enum DependenciesFileFormat { Xml, Dgml };

	public enum RootFileType { Xml, Assembly };

	public readonly record struct RootType {

		public readonly AssemblyRootMode? Mode { get; init; }
		public readonly RootFileType Type { get; init; }

		public RootType(RootFileType type, AssemblyRootMode? mode = null) {
			if (type == RootFileType.Assembly && mode == null)
				throw new ArgumentException ("mode must be specified for RootFileType.Assembly");
		}
	}

	// Represents the options passed to the linker.
	// From an instance of this class, it should be possible to reconstruct
	// command line arguments that would produce the same options.
	public class LinkerOptions
	{
		public bool IgnoreUnresolved { get; init; }
		public bool LogMessages { get; init; }
		public bool EnableReducedTracing { get; init; }
		public bool KeepUsedAttributeTypesOnly	{ get; init; }
		public bool StripSecurity { get; init; }
		public string? DependenciesFileName { get; init; }
		public bool DumpDependencies { get; init; }
		public DependenciesFileFormat DependenciesFileFormat { get; init; }
		public bool AddReflectionAnnotations { get; init; }
		public AssemblyAction DefaultAction { get; init; }
		public ImmutableDictionary<string, AssemblyAction> Actions { get; init; } = ImmutableDictionary<string, AssemblyAction>.Empty;

		// public List<(CodeOptimizations, string? bool)> SetOptimizations { get; } = new ();

		public bool? StripDescriptors { get; init; }
		public bool? StripSubstitutions { get; init; }
		public bool? StripLinkAttributes { get; init; }
		public ImmutableArray<string> SubstitutionsFiles { get; init; } = ImmutableArray<string>.Empty;
		public AssemblyAction TrimAction { get; init; }
		public ImmutableArray<string> CustomSteps { get; init; } = ImmutableArray<string>.Empty;
		public ImmutableDictionary<string, string> CustomData { get; init; } = ImmutableDictionary<string, string>.Empty;
		public bool KeepCompilersResources { get; init; }
		public bool? KeepDepAttributes { get; init; }
		public MetadataTrimming MetadataTrimming { get; init; }
		public bool EnableSerializationDiscovery { get; init; }
		public bool DisableOperatorDiscovery { get; init; }
		public bool IgnoreDescriptors { get; init; }
		public bool IgnoreSubstitutions { get; init; }
		public bool IgnoreLinkAttributes { get; init; }
		// enable-opt and disable-opt
		public bool? BeforeFieldInit { get; init; }
		public bool? OverrideRemoval { get; init; }
		public bool? UnreachableBodies { get; init; }
		public bool? UnusedInterfaces { get; init; }
		public bool? UnusedTypeChecks { get; init; }
		public bool? IPConstantPropagation { get; init; }
		public bool? Sealer { get; init; }
		public ImmutableDictionary<string, bool> AssemblyBeforeFieldInit { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public ImmutableDictionary<string, bool> AssemblyOverrideRemoval { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public ImmutableDictionary<string, bool> AssemblyUnreachableBodies { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public ImmutableDictionary<string, bool> AssemblyUnusedInterfaces { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public ImmutableDictionary<string, bool> AssemblyUnusedTypeChecks { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public ImmutableDictionary<string, bool> AssemblyIPConstantPropagation { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public ImmutableDictionary<string, bool> AssemblySealer { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public ImmutableDictionary<string, bool> FeatureSettings { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public bool NewMvid { get; init; }
		public bool Deterministic { get; init; }
		public string? AssemblyListFile { get; init; }
		public string? PInvokesListFile { get; init; }
		public ImmutableArray<string> LinkAttributesFiles { get; init; } = ImmutableArray<string>.Empty;
		public WarningSuppressionWriter.FileOutputKind? WarningSuppressionFileOutputKind { get; init; }

		// Warning options
		public bool NoTrimWarn { get; init; }
		public ImmutableHashSet<int> NoWarn { get; init; } = ImmutableHashSet<int>.Empty;
		public bool? GeneralWarnAsError { get; init; }
		public ImmutableDictionary<int, bool> WarnAsError { get; init; } = ImmutableDictionary<int, bool>.Empty;
		public WarnVersion WarnVersion { get; init; }
		public bool? GeneralSingleWarn { get; init; }
		public ImmutableDictionary<string, bool> SingleWarn { get; init; } = ImmutableDictionary<string, bool>.Empty;
		public ImmutableArray<string> SearchDirectories { get; init; } = ImmutableArray<string>.Empty;
		public ImmutableArray<(string, RootType)> RootInputs { get; init; } = ImmutableArray<(string, RootType)>.Empty;
		// public List<string> XmlDescriptors { get; } = new ();
		// public List<(string, AssemblyRootMode)> AssemblyRoots { get; } = new ();

		public bool LinkSymbols { get; init; }
		public ImmutableArray<string> ReferenceAssemblies { get; init; } = ImmutableArray<string>.Empty;
		public bool AddBypassNGenStep { get; init; }



		public string? OutputDirectory { get; init; }
	}
}