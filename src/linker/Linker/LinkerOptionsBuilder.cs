// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Mono.Linker
{
	public sealed class LinkerOptionsBuilder
	{
		public bool? IgnoreUnresolved { get; set; }
		public bool? LogMessages { get; set; }
		public bool? EnableReducedTracing { get; set; }
		public bool? KeepUsedAttributeTypesOnly	{ get; set; }
		public bool? StripSecurity { get; set; }
		public string? DependenciesFileName { get; set; }
		public bool? DumpDependencies { get; set; }
		public DependenciesFileFormat? DependenciesFileFormat { get; set; }
		public bool? AddReflectionAnnotations { get; set; }
		public AssemblyAction? DefaultAction { get; set; }
		public ImmutableDictionary<string, AssemblyAction>.Builder Actions { get; } = ImmutableDictionary.CreateBuilder<string, AssemblyAction> ();

		// public List<(CodeOptimizations, string? bool)> SetOptimizations { get; } = new ();

		public bool? StripDescriptors { get; set; }
		public bool? StripSubstitutions { get; set; }
		public bool? StripLinkAttributes { get; set; }
		public ImmutableArray<string>.Builder SubstitutionsFiles { get; } = ImmutableArray.CreateBuilder<string> ();
		public AssemblyAction? TrimAction { get; set; }
		public ImmutableArray<string>.Builder CustomSteps { get; } = ImmutableArray.CreateBuilder<string> ();
		public ImmutableDictionary<string, string>.Builder CustomData { get; } = ImmutableDictionary.CreateBuilder<string, string> ();
		public bool? KeepCompilersResources { get; set; }
		public bool? KeepDepAttributes { get; set; }
		public MetadataTrimming? MetadataTrimming { get; set; }
		public bool? EnableSerializationDiscovery { get; set; }
		public bool? DisableOperatorDiscovery { get; set; }
		public bool? IgnoreDescriptors { get; set; }
		public bool? IgnoreSubstitutions { get; set; }
		public bool? IgnoreLinkAttributes { get; set; }
		// enable-opt and disable-opt
		public bool? BeforeFieldInit { get; set; }
		public bool? OverrideRemoval { get; set; }
		public bool? UnreachableBodies { get; set; }
		public bool? UnusedInterfaces { get; set; }
		public bool? UnusedTypeChecks { get; set; }
		public bool? IPConstantPropagation { get; set; }
		public bool? Sealer { get; set; }
		public ImmutableDictionary<string, bool>.Builder AssemblyBeforeFieldInit { get; } = ImmutableDictionary.CreateBuilder<string, bool> ();
		public ImmutableDictionary<string, bool>.Builder AssemblyOverrideRemoval { get; } = ImmutableDictionary.CreateBuilder<string, bool> ();
		public ImmutableDictionary<string, bool>.Builder AssemblyUnreachableBodies { get; } = ImmutableDictionary.CreateBuilder<string, bool> ();
		public ImmutableDictionary<string, bool>.Builder AssemblyUnusedInterfaces { get; } = ImmutableDictionary.CreateBuilder<string, bool> ();
		public ImmutableDictionary<string, bool>.Builder AssemblyUnusedTypeChecks { get; } = ImmutableDictionary.CreateBuilder<string, bool> ();
		public ImmutableDictionary<string, bool>.Builder AssemblyIPConstantPropagation { get; } = ImmutableDictionary.CreateBuilder<string, bool> ();
		public ImmutableDictionary<string, bool>.Builder AssemblySealer { get; } = ImmutableDictionary.CreateBuilder<string, bool> ();
		public ImmutableDictionary<string, bool>.Builder FeatureSettings { get; } = ImmutableDictionary.CreateBuilder<string, bool> (StringComparer.Ordinal);

		public bool? NewMvid { get; set; }
		public bool? Deterministic { get; set; }
		public string? AssemblyListFile { get; set; }
		public string? PInvokesListFile { get; set; }
		public ImmutableArray<string>.Builder LinkAttributesFiles { get; } = ImmutableArray.CreateBuilder<string> ();
		public WarningSuppressionWriter.FileOutputKind? WarningSuppressionFileOutputKind { get; set; }

		// Warning options
		public bool? NoTrimWarn { get; set; }
		public ImmutableHashSet<int>.Builder NoWarn { get; } = ImmutableHashSet.CreateBuilder<int> ();
		public bool? GeneralWarnAsError { get; set; }
		public ImmutableDictionary<int, bool>.Builder WarnAsError { get; } = ImmutableDictionary.CreateBuilder<int, bool> ();
		public WarnVersion? WarnVersion { get; set; }
		public bool? GeneralSingleWarn { get; set; }
		public ImmutableDictionary<string, bool>.Builder SingleWarn { get; } = ImmutableDictionary.CreateBuilder<string, bool> ();
		public ImmutableArray<string>.Builder SearchDirectories { get; } = ImmutableArray.CreateBuilder<string> ();
		public string? OutputDirectory { get; set; }
		public ImmutableArray<(string, RootType)>.Builder RootInputs { get; } = ImmutableArray.CreateBuilder<(string, RootType)> ();
		// public List<string> XmlDescriptors { get; } = new ();
		// public List<(string, AssemblyRootMode)> AssemblyRoots { get; } = new ();

		public bool LinkSymbols { get; set; }
		public ImmutableArray<string>.Builder ReferenceAssemblies { get; } = ImmutableArray.CreateBuilder<string> ();
		public bool AddBypassNGenStep { get; set; }




			const CodeOptimizations defaultOptimizations =
				CodeOptimizations.BeforeFieldInit |
				CodeOptimizations.OverrideRemoval |
				CodeOptimizations.UnusedInterfaces |
				CodeOptimizations.UnusedTypeChecks |
				CodeOptimizations.IPConstantPropagation |
				CodeOptimizations.UnreachableBodies |
				CodeOptimizations.RemoveDescriptors |
				CodeOptimizations.RemoveLinkAttributes |
				CodeOptimizations.RemoveSubstitutions |
				CodeOptimizations.RemoveDynamicDependencyAttribute |
				CodeOptimizations.OptimizeTypeHierarchyAnnotations;


		public LinkerOptions Build ()
		{
			return new LinkerOptions {
				IgnoreUnresolved = IgnoreUnresolved ?? false,
				LogMessages = LogMessages ?? false,
				EnableReducedTracing = EnableReducedTracing ?? false,
				KeepUsedAttributeTypesOnly = KeepUsedAttributeTypesOnly ?? false,
				StripSecurity = StripSecurity ?? true,
				DependenciesFileName = DependenciesFileName,
				DumpDependencies = DumpDependencies,
				DependenciesFileFormat = DependenciesFileFormat,
				AddReflectionAnnotations = AddReflectionAnnotations,
				DefaultAction = DefaultAction,
				Actions = Actions.ToImmutable (),
				StripDescriptors = StripDescriptors,
				StripSubstitutions = StripSubstitutions,
				StripLinkAttributes = StripLinkAttributes,
				SubstitutionsFiles = SubstitutionsFiles.ToImmutable (),
				TrimAction = TrimAction ?? AssemblyAction.Link,
				CustomSteps = CustomSteps.ToImmutable (),
				CustomData = CustomData.ToImmutable (),
				KeepCompilersResources = KeepCompilersResources,
				KeepDepAttributes = KeepDepAttributes,
				MetadataTrimming = MetadataTrimming,
				EnableSerializationDiscovery = EnableSerializationDiscovery,
				DisableOperatorDiscovery = DisableOperatorDiscovery,
				IgnoreDescriptors = IgnoreDescriptors,
				IgnoreSubstitutions = IgnoreSubstitutions,
				IgnoreLinkAttributes = IgnoreLinkAttributes,
				BeforeFieldInit = BeforeFieldInit,
				OverrideRemoval = OverrideRemoval,
				UnreachableBodies = UnreachableBodies,
				UnusedInterfaces = UnusedInterfaces,
				UnusedTypeChecks = UnusedTypeChecks,
				IPConstantPropagation = IPConstantPropagation,
				Sealer = Sealer,
				AssemblyBeforeFieldInit = AssemblyBeforeFieldInit.ToImmutable (),
				AssemblyOverrideRemoval = AssemblyOverrideRemoval.ToImmutable (),
				AssemblyUnreachableBodies = AssemblyUnreachableBodies.ToImmutable (),
				AssemblyUnusedInterfaces = AssemblyUnusedInterfaces.ToImmutable (),
				AssemblyUnusedTypeChecks = AssemblyUnusedTypeChecks.ToImmutable (),
				AssemblyIPConstantPropagation = AssemblyIPConstantPropagation.ToImmutable (),
				AssemblySealer = AssemblySealer.ToImmutable (),
				FeatureSettings = FeatureSettings.ToImmutable (),
				NewMvid = NewMvid ?? false,
				Deterministic = Deterministic ?? false,
				AssemblyListFile = AssemblyListFile,
				PInvokesListFile = PInvokesListFile,
				LinkAttributesFiles = LinkAttributesFiles.ToImmutable (),
				WarningSuppressionFileOutputKind = WarningSuppressionFileOutputKind,
				NoTrimWarn = NoTrimWarn,
				NoWarn = NoWarn.ToImmutable (),
				GeneralWarnAsError = GeneralWarnAsError,
				WarnAsError = WarnAsError.ToImmutable (),
				WarnVersion = WarnVersion,
				GeneralSingleWarn = GeneralSingleWarn,
				SingleWarn = SingleWarn.ToImmutable (),
				SearchDirectories = SearchDirectories.ToImmutable (),
				RootInputs = RootInputs.ToImmutable (),
				// XmlDescriptors = XmlDescriptors,
				// AssemblyRoots = AssemblyRoots,
				LinkSymbols = LinkSymbols,
				ReferenceAssemblies = ReferenceAssemblies.ToImmutable(),
				AddBypassNGenStep = AddBypassNGenStep,

				OutputDirectory = OutputDirectory,
			};
		}
	}
}