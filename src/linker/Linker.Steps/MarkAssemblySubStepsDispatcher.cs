﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using Mono.Cecil;
using Mono.Collections.Generic;

namespace Mono.Linker.Steps
{
	//
	// Dispatcher for SubSteps which only need to run on marked assemblies.
	// This simplifies the implementation of linker custom steps, in the same
	// way that SubStepsDispatcher does, but it implements IMarkAssemblyStep
	// and is invoked during MarkStep when an assembly gets marked.
	//
	public abstract class MarkAssemblySubStepsDispatcher : IMarkAssemblyStep
	{
		readonly List<ISubStep> substeps;

		List<ISubStep> on_assemblies;
		List<ISubStep> on_types;
		List<ISubStep> on_fields;
		List<ISubStep> on_methods;
		List<ISubStep> on_properties;
		List<ISubStep> on_events;

		protected MarkAssemblySubStepsDispatcher ()
		{
			substeps = new List<ISubStep> ();
		}

		protected MarkAssemblySubStepsDispatcher (IEnumerable<ISubStep> subSteps)
		{
			substeps = new List<ISubStep> (subSteps);
		}

		public void Add (ISubStep substep)
		{
			substeps.Add (substep);
		}

		void IMarkAssemblyStep.Initialize (LinkContext context)
		{
			InitializeSubSteps (context);
		}

		void IMarkAssemblyStep.ProcessAssembly (AssemblyDefinition assembly)
		{
			BrowseAssembly (assembly);
		}

		static bool HasSubSteps (List<ISubStep> substeps) => substeps?.Count > 0;

		void BrowseAssembly (AssemblyDefinition assembly)
		{
			CategorizeSubSteps (assembly);

			if (HasSubSteps (on_assemblies))
				DispatchAssembly (assembly);

			if (!ShouldDispatchTypes ())
				return;

			BrowseTypes (assembly.MainModule.Types);
		}

		bool ShouldDispatchTypes ()
		{
			return HasSubSteps (on_types)
				|| HasSubSteps (on_fields)
				|| HasSubSteps (on_methods)
				|| HasSubSteps (on_properties)
				|| HasSubSteps (on_events);
		}

		void BrowseTypes (Collection<TypeDefinition> types)
		{
			foreach (TypeDefinition type in types) {
				DispatchType (type);

				if (type.HasFields && HasSubSteps (on_fields)) {
					foreach (FieldDefinition field in type.Fields)
						DispatchField (field);
				}

				if (type.HasMethods && HasSubSteps (on_methods)) {
					foreach (MethodDefinition method in type.Methods)
						DispatchMethod (method);
				}

				if (type.HasProperties && HasSubSteps (on_properties)) {
					foreach (PropertyDefinition property in type.Properties)
						DispatchProperty (property);
				}

				if (type.HasEvents && HasSubSteps (on_events)) {
					foreach (EventDefinition @event in type.Events)
						DispatchEvent (@event);
				}

				if (type.HasNestedTypes)
					BrowseTypes (type.NestedTypes);
			}
		}

		void DispatchAssembly (AssemblyDefinition assembly)
		{
			foreach (var substep in on_assemblies) {
				substep.ProcessAssembly (assembly);
			}
		}

		void DispatchType (TypeDefinition type)
		{
			foreach (var substep in on_types) {
				substep.ProcessType (type);
			}
		}

		void DispatchField (FieldDefinition field)
		{
			foreach (var substep in on_fields) {
				substep.ProcessField (field);
			}
		}

		void DispatchMethod (MethodDefinition method)
		{
			foreach (var substep in on_methods) {
				substep.ProcessMethod (method);
			}
		}

		void DispatchProperty (PropertyDefinition property)
		{
			foreach (var substep in on_properties) {
				substep.ProcessProperty (property);
			}
		}

		void DispatchEvent (EventDefinition @event)
		{
			foreach (var substep in on_events) {
				substep.ProcessEvent (@event);
			}
		}

		void InitializeSubSteps (LinkContext context)
		{
			foreach (var substep in substeps)
				substep.Initialize (context);
		}

		void CategorizeSubSteps (AssemblyDefinition assembly)
		{
			on_assemblies = null;
			on_types = null;
			on_fields = null;
			on_methods = null;
			on_properties = null;
			on_events = null;

			foreach (var substep in substeps)
				CategorizeSubStep (substep, assembly);
		}

		void CategorizeSubStep (ISubStep substep, AssemblyDefinition assembly)
		{
			if (!substep.IsActiveFor (assembly))
				return;

			CategorizeTarget (substep, SubStepTargets.Assembly, ref on_assemblies);
			CategorizeTarget (substep, SubStepTargets.Type, ref on_types);
			CategorizeTarget (substep, SubStepTargets.Field, ref on_fields);
			CategorizeTarget (substep, SubStepTargets.Method, ref on_methods);
			CategorizeTarget (substep, SubStepTargets.Property, ref on_properties);
			CategorizeTarget (substep, SubStepTargets.Event, ref on_events);
		}

		static void CategorizeTarget (ISubStep substep, SubStepTargets target, ref List<ISubStep> list)
		{
			if (!Targets (substep, target))
				return;

			if (list == null)
				list = new List<ISubStep> ();

			list.Add (substep);
		}

		static bool Targets (ISubStep substep, SubStepTargets target) => (substep.Targets & target) == target;
	}
}