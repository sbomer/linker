// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//
// Driver.cs
//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// (C) 2006 Jb Evain
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using ILLink.Shared;
using Mono.Cecil;
using Mono.Linker.Steps;

namespace Mono.Linker
{

	public partial class Driver : IDisposable
	{
		const string _linker = "IL Linker";

		public static int Main (string[] args)
		{
			LinkerEventSource.Log.LinkerStart (string.Join ("; ", args));
			var argProcessor = new ArgumentProcessor (args);
			if (!argProcessor.TryProcess (out var options)) {
				LinkerEventSource.Log.LinkerStop ();
				return 1;
			}

			if (options == null)
				return 0;

			try {
				using (Driver driver = new Driver (options)) {
					return driver.Run ();
				}
			} catch {
				Console.Error.WriteLine ("Fatal error in {0}", _linker);
				throw;
			} finally {
				LinkerEventSource.Log.LinkerStop ();
			}
		}

		readonly LinkerOptions options;
		LinkContext? context;
		protected LinkContext Context {
			get {
				Debug.Assert (context != null);
				return context;
			}
		}

		public Driver (LinkerOptions options)
		{
			this.options = options;
		}

		// Perform setup of the LinkContext and parse the arguments.
		// Return values:
		// 0 => successfully set up context with all arguments
		// 1 => argument processing stopped early without errors
		// -1 => error setting up context
		protected int SetupContext (LinkerOptions options, ILogger? customLogger = null)
		{
			Pipeline p = GetStandardPipeline ();
			context = GetDefaultContext (p, customLogger);
			// string? reproDirectory = null;

			var body_substituter_steps = new Stack<string> ();
			var custom_steps = new List<string> ();
			var set_optimizations = new List<(CodeOptimizations, string?, bool)> ();
			bool dumpDependencies = false;
			string? dependenciesFileName = null;
			context.StripSecurity = true;
			bool new_mvid_used = false;
			bool deterministic_used = false;
			bool keepCompilersResources = false;
			DependenciesFileFormat fileType = DependenciesFileFormat.Xml;

			List<BaseStep> inputs = CreateDefaultResolvers ();

		
			

					// case "--create-repro":
					// 	if (!GetStringParam (token, out reproDirectory))
					// 		return -1;

					// 	continue;


			if (new_mvid_used && deterministic_used) {
				context.LogError (null, DiagnosticId.NewMvidAndDeterministicCannotBeUsedAtSameTime);
				return -1;
			}

			// Default to deterministic output
			if (!new_mvid_used && !deterministic_used) {
				context.DeterministicOutput = true;
			}
			if (dumpDependencies) {
				switch (fileType) {
				case DependenciesFileFormat.Xml:
					AddXmlDependencyRecorder (context, dependenciesFileName);
					break;
				case DependenciesFileFormat.Dgml:
					AddDgmlDependencyRecorder (context, dependenciesFileName);
					break;
				default:
					context.LogError (null, DiagnosticId.InvalidDependenciesFileFormat);
					break;
				}
			}


			if (set_optimizations.Count > 0) {
				foreach (var (opt, assemblyName, enable) in set_optimizations) {
					if (enable)
						context.Optimizations.Enable (opt, assemblyName);
					else
						context.Optimizations.Disable (opt, assemblyName);
				}
			}

			//
			// Modify the default pipeline
			//

			for (int i = inputs.Count; i != 0; --i)
				p.PrependStep (inputs[i - 1]);

			for (int i = options.LinkAttributesFiles.Length - 1; i >= 0; i--)
				AddLinkAttributesStep (p, options.LinkAttributesFiles[i]);

			for (int i = options.SubstitutionsFiles.Length - 1; i >= 0; i--)
				AddBodySubstituterStep (p, options.SubstitutionsFiles[i]);

			if (context.DeterministicOutput)
				p.RemoveStep (typeof (RegenerateGuidStep));

			if (options.AddReflectionAnnotations)
				p.AddStepAfter (typeof (MarkStep), new ReflectionBlockedStep ());

			if (options.AddBypassNGenStep)
				p.AddStepAfter (typeof (SweepStep), new AddBypassNGenStep ());

			if (keepCompilersResources) {
				p.RemoveStep (typeof (RemoveResourcesStep));
			}

			p.AddStepBefore (typeof (OutputStep), new SealerStep ());

			//
			// Pipeline setup with all steps enabled
			//
			// RootAssemblyInputStep or ResolveFromXmlStep [at least one of them]
			// LinkAttributesStep [optional, possibly many]
			// BodySubstituterStep [optional]
			// MarkStep
			// ReflectionBlockedStep [optional]
			// RemoveResourcesStep [optional]
			// ProcessWarningsStep
			// OutputWarningSuppressions
			// SweepStep
			// AddBypassNGenStep [optional]
			// CodeRewriterStep
			// CleanStep
			// RegenerateGuidStep [optional]
			// SealerStep
			// OutputStep

			if (context.EnableSerializationDiscovery)
				p.MarkHandlers.Add (new DiscoverSerializationHandler ());

			if (!context.DisableOperatorDiscovery)
				p.MarkHandlers.Add (new DiscoverOperatorsHandler ());

			foreach (string custom_step in custom_steps) {
				if (!AddCustomStep (p, custom_step))
					return -1;
			}

			return 0;
		}

		// Returns the exit code of the process. 0 indicates success.
		// Known non-recoverable errors (LinkerFatalErrorException) set the exit code
		// to the error code.
		// May propagate exceptions, which will result in the process getting an
		// exit code determined by dotnet.
		public int Run (ILogger? customLogger = null)
		{
			int setupStatus = SetupContext (options, customLogger);
			if (setupStatus > 0)
				return 0;
			if (setupStatus < 0)
				return 1;

			Pipeline p = Context.Pipeline;
			PreProcessPipeline (p);

			try {
				p.Process (Context);
			} catch (Exception e) when (LogFatalError (e)) {
				// Unreachable
				throw;
			}

			Context.FlushCachedWarnings ();
			Context.Tracer.Finish ();
			return Context.ErrorsCount > 0 ? 1 : 0;
		}

		/// <summary>
		/// This method is called in the exception filter for unexpected exceptions.
		/// Prints error messages and returns false to avoid catching in the exception filter.
		/// </summary>
		bool LogFatalError (Exception e)
		{
			switch (e) {
			case LinkerFatalErrorException lex:
				Context.LogMessage (lex.MessageContainer);
				Debug.Assert (lex.MessageContainer.Category == MessageCategory.Error);
				Debug.Assert (lex.MessageContainer.Code != null);
				Debug.Assert (lex.MessageContainer.Code.Value != 0);
				break;
			case ResolutionException re:
				Context.LogError (null, DiagnosticId.FailedToResolveMetadataElement, re.Message);
				break;
			default:
				Context.LogError (null, DiagnosticId.LinkerUnexpectedError);
				break;
			}
			return false;
		}

		partial void PreProcessPipeline (Pipeline pipeline);


		Assembly? GetCustomAssembly (string arg)
		{
			if (Path.IsPathRooted (arg)) {
				var assemblyPath = Path.GetFullPath (arg);
				if (File.Exists (assemblyPath)) {
					// The CLR will return the already-loaded assembly if the same path is requested multiple times
					// (or even if a different path specifies the "same" assembly, based on the MVID).

					// Ignore warning, since we're just enabling analyzer for dogfooding
#pragma warning disable IL2026
					return AssemblyLoadContext.Default.LoadFromAssemblyPath (assemblyPath);
#pragma warning restore IL2026
				}
				Context.LogError (null, DiagnosticId.AssemblyInCustomStepOptionCouldNotBeFound, arg);
			} else
				Context.LogError (null, DiagnosticId.AssemblyPathInCustomStepMustBeFullyQualified, arg);

			return null;
		}

		protected virtual void AddResolveFromXmlStep (Pipeline pipeline, string file)
		{
			pipeline.PrependStep (new ResolveFromXmlStep (File.OpenRead (file), file));
		}

		protected virtual void AddLinkAttributesStep (Pipeline pipeline, string file)
		{
			pipeline.AddStepBefore (typeof (MarkStep), new LinkAttributesStep (File.OpenRead (file), file));
		}

		static void AddBodySubstituterStep (Pipeline pipeline, string file)
		{
			pipeline.AddStepBefore (typeof (MarkStep), new BodySubstituterStep (File.OpenRead (file), file));
		}

		protected virtual void AddXmlDependencyRecorder (LinkContext context, string? fileName)
		{
			context.Tracer.AddRecorder (new XmlDependencyRecorder (context, fileName));
		}

		protected virtual void AddDgmlDependencyRecorder (LinkContext context, string? fileName)
		{
			context.Tracer.AddRecorder (new DgmlDependencyRecorder (context, fileName));
		}

		protected bool AddMarkHandler (Pipeline pipeline, string arg)
		{
			if (!TryGetCustomAssembly (ref arg, out Assembly? custom_assembly))
				return false;

			var step = ResolveStep<IMarkHandler> (arg, custom_assembly);
			if (step == null)
				return false;

			pipeline.AppendMarkHandler (step);
			return true;
		}

		bool TryGetCustomAssembly (ref string arg, [NotNullWhen (true)] out Assembly? assembly)
		{
			assembly = null;
			int pos = arg.IndexOf (",");
			if (pos == -1)
				return false;

			assembly = GetCustomAssembly (arg.Substring (pos + 1));
			if (assembly == null)
				return false;

			arg = arg.Substring (0, pos);
			return true;
		}

		protected bool AddCustomStep (Pipeline pipeline, string arg)
		{
			if (!TryGetCustomAssembly (ref arg, out Assembly? custom_assembly))
				return false;

			string customStepName;
			string? targetName = null;
			bool before = false;
			if (!arg.Contains (':')) {
				customStepName = arg;
			} else {
				string[] parts = arg.Split (':');
				if (parts.Length != 2) {
					Context.LogError (null, DiagnosticId.InvalidArgForCustomStep, arg);
					return false;
				}
				customStepName = parts[1];

				if (!parts[0].StartsWith ("-") && !parts[0].StartsWith ("+")) {
					Context.LogError (null, DiagnosticId.ExpectedSignToControlNewStepInsertion);
					return false;
				}

				before = parts[0][0] == '-';
				targetName = parts[0].Substring (1);
			}

			var stepType = ResolveStepType (customStepName, custom_assembly);
			if (stepType == null)
				return false;

			if (typeof (IStep).IsAssignableFrom (stepType)) {

				var customStep = (IStep?) Activator.CreateInstance (stepType) ?? throw new InvalidOperationException ();
				if (targetName == null) {
					pipeline.AppendStep (customStep);
					return true;
				}

				IStep? target = FindStep (pipeline, targetName);
				if (target == null) {
					Context.LogError (null, DiagnosticId.PipelineStepCouldNotBeFound, targetName);
					return false;
				}

				if (before)
					pipeline.AddStepBefore (target, customStep);
				else
					pipeline.AddStepAfter (target, customStep);

				return true;
			}

			if (typeof (IMarkHandler).IsAssignableFrom (stepType)) {

				var customStep = (IMarkHandler?) Activator.CreateInstance (stepType) ?? throw new InvalidOperationException ();
				if (targetName == null) {
					pipeline.AppendMarkHandler (customStep);
					return true;
				}

				IMarkHandler? target = FindMarkHandler (pipeline, targetName);
				if (target == null) {
					Context.LogError (null, DiagnosticId.PipelineStepCouldNotBeFound, targetName);
					return false;
				}

				if (before)
					pipeline.AddMarkHandlerBefore (target, customStep);
				else
					pipeline.AddMarkHandlerAfter (target, customStep);

				return true;
			}

			Context.LogError (null, DiagnosticId.CustomStepTypeIsIncompatibleWithLinkerVersion, stepType.ToString ());
			return false;
		}

		protected virtual IStep? FindStep (Pipeline pipeline, string name)
		{
			foreach (IStep step in pipeline.GetSteps ()) {
				Type t = step.GetType ();
				if (t.Name == name)
					return step;
			}

			return null;
		}

		static IMarkHandler? FindMarkHandler (Pipeline pipeline, string name)
		{
			foreach (IMarkHandler step in pipeline.MarkHandlers) {
				Type t = step.GetType ();
				if (t.Name == name)
					return step;
			}

			return null;
		}

		Type? ResolveStepType (string type, Assembly assembly)
		{
			// Ignore warning, since we're just enabling analyzer for dogfooding
#pragma warning disable IL2026
			Type? step = assembly != null ? assembly.GetType (type) : Type.GetType (type, false);
#pragma warning restore IL2026

			if (step == null) {
				Context.LogError (null, DiagnosticId.CustomStepTypeCouldNotBeFound, type);
				return null;
			}

			return step;
		}

		TStep? ResolveStep<TStep> (string type, Assembly assembly) where TStep : class
		{
			// Ignore warning, since we're just enabling analyzer for dogfooding
#pragma warning disable IL2026
			Type? step = assembly != null ? assembly.GetType (type) : Type.GetType (type, false);
#pragma warning restore IL2026

			if (step == null) {
				Context.LogError (null, DiagnosticId.CustomStepTypeCouldNotBeFound, type);
				return null;
			}

			if (!typeof (TStep).IsAssignableFrom (step)) {
				Context.LogError (null, DiagnosticId.CustomStepTypeIsIncompatibleWithLinkerVersion, type);
				return null;
			}

			return (TStep?) Activator.CreateInstance (step);
		}






		protected virtual LinkContext GetDefaultContext (Pipeline pipeline, ILogger? logger)
		{
			return new LinkContext (pipeline, logger ?? new ConsoleLogger (), "output") {
				TrimAction = AssemblyAction.Link,
				DefaultAction = AssemblyAction.Link,
			};
		}

		protected virtual List<BaseStep> CreateDefaultResolvers ()
		{
			return new List<BaseStep> ();
		}

		static Pipeline GetStandardPipeline ()
		{
			Pipeline p = new Pipeline ();
			p.AppendStep (new ProcessReferencesStep ());
			p.AppendStep (new MarkStep ());
			p.AppendStep (new RemoveResourcesStep ());
			p.AppendStep (new ValidateVirtualMethodAnnotationsStep ());
			p.AppendStep (new ProcessWarningsStep ());
			p.AppendStep (new OutputWarningSuppressions ());
			p.AppendStep (new SweepStep ());
			p.AppendStep (new CheckSuppressionsDispatcher ());
			p.AppendStep (new CodeRewriterStep ());
			p.AppendStep (new CleanStep ());
			p.AppendStep (new RegenerateGuidStep ());
			p.AppendStep (new OutputStep ());
			return p;
		}

		public void Dispose ()
		{
			context?.Dispose ();
		}
	}
}
