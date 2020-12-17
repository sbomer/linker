//
// Blacklist.cs
//
// Author:
//   Jb Evain (jb@nurv.fr)
//
// (C) 2007 Novell Inc.
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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.XPath;
using Mono.Cecil;

namespace Mono.Linker.Steps
{
	public class EmbeddedXmlStep : BaseAssemblyStep
	{
		MarkStep MarkStep { get; }

		public EmbeddedXmlStep (AssemblyDefinition assembly, MarkStep markStep) : base (assembly)
		{
			MarkStep = markStep;
		}

		protected override void Process ()
		{
			List<ProcessLinkerXmlStepBase> steps = new List<ProcessLinkerXmlStepBase> ();
#if !ILLINK
			foreach (string name in Assembly.GetExecutingAssembly ().GetManifestResourceNames ()) {
				if (!name.EndsWith (".xml", StringComparison.OrdinalIgnoreCase) || !ShouldProcessRootDescriptorResource (_assembly, name))
					continue;

				try {
					Context.LogMessage ($"Processing resource linker descriptor: {name}");
					steps.Add (GetResolveStep (name));
				} catch (XmlException ex) {
					/* This could happen if some broken XML file is included. */
					Context.LogError ($"Error processing {name}: {ex}", 1003);
				}
			}
#endif

			if (Annotations.GetAction (_assembly) != AssemblyAction.Skip) {

				var embeddedXml = _assembly.Modules
					.SelectMany (mod => mod.Resources)
					.Where (res => res.ResourceType == ResourceType.Embedded)
					.Where (res => res.Name.EndsWith (".xml", StringComparison.OrdinalIgnoreCase))
					.ToArray ();

				foreach (var rsc in embeddedXml
									.Where (res => ShouldProcessRootDescriptorResource (_assembly, res.Name))
									.Cast<EmbeddedResource> ()) {
					try {
						Context.LogMessage ($"Processing embedded linker descriptor {rsc.Name} from {_assembly.Name}");
						steps.Add (GetExternalResolveStep (rsc, _assembly));
					} catch (XmlException ex) {
						/* This could happen if some broken XML file is embedded. */
						Context.LogError ($"Error processing {rsc.Name}: {ex}", 1003);
					}
				}

				foreach (var rsc in embeddedXml
									.Where (res => res.Name.Equals ("ILLink.Substitutions.xml", StringComparison.OrdinalIgnoreCase))
									.Cast<EmbeddedResource> ()) {
					try {
						Context.LogMessage ($"Processing embedded substitution descriptor {rsc.Name} from {_assembly.Name}");
						steps.Add (GetExternalSubstitutionStep (rsc, _assembly));
					} catch (XmlException ex) {
						Context.LogError ($"Error processing {rsc.Name}: {ex}", 1003);
					}
				}

				foreach (var rsc in embeddedXml
									.Where (res => res.Name.Equals ("ILLink.LinkAttributes.xml", StringComparison.OrdinalIgnoreCase))
									.Cast<EmbeddedResource> ()) {
					try {
						Context.LogMessage ($"Processing embedded {rsc.Name} from {_assembly.Name}");
						steps.Add (GetExternalLinkAttributesStep (rsc, _assembly));
					} catch (XmlException ex) {
						Context.LogError ($"Error processing {rsc.Name} from {_assembly.Name}: {ex}", 1003);
					}
				}
			}

			foreach (var step in steps) {
				step.Process (Context);
			}
		}

		static string GetAssemblyName (string descriptor)
		{
			int pos = descriptor.LastIndexOf ('.');
			if (pos == -1)
				return descriptor;

			return descriptor.Substring (0, pos);
		}

		bool ShouldProcessRootDescriptorResource (AssemblyDefinition assembly, string resourceName)
		{
			if (resourceName.Equals ("ILLink.Descriptors.xml", StringComparison.OrdinalIgnoreCase))
				return true;

			if (GetAssemblyName (resourceName) != assembly.Name.Name)
				return false;

			switch (Context.Annotations.GetAction (assembly)) {
			case AssemblyAction.Link:
			case AssemblyAction.AddBypassNGen:
			case AssemblyAction.AddBypassNGenUsed:
			case AssemblyAction.Copy:
				return true;
			default:
				return false;
			}

		}

		protected virtual ResolveFromXmlStep GetExternalResolveStep (EmbeddedResource resource, AssemblyDefinition assembly)
		{
			return new ResolveFromXmlStep (MarkStep, GetExternalDescriptor (resource), resource, assembly, "resource " + resource.Name + " in " + assembly.FullName);
		}

		static BodySubstituterStep GetExternalSubstitutionStep (EmbeddedResource resource, AssemblyDefinition assembly)
		{
			return new BodySubstituterStep (GetExternalDescriptor (resource), resource, assembly, "resource " + resource.Name + " in " + assembly.FullName);
		}

		static LinkAttributesStep GetExternalLinkAttributesStep (EmbeddedResource resource, AssemblyDefinition assembly)
		{
			return new LinkAttributesStep (GetExternalDescriptor (resource), resource, assembly, "resource " + resource.Name + " in " + assembly.FullName);
		}

		ResolveFromXmlStep GetResolveStep (string descriptor)
		{
			return new ResolveFromXmlStep (MarkStep, GetDescriptor (descriptor), "descriptor " + descriptor + " from " + Assembly.GetExecutingAssembly ().FullName);
		}

		protected static XPathDocument GetExternalDescriptor (EmbeddedResource resource)
		{
			using (var sr = new StreamReader (resource.GetResourceStream ())) {
				return new XPathDocument (sr);
			}
		}

		static XPathDocument GetDescriptor (string descriptor)
		{
			using (StreamReader sr = new StreamReader (GetResource (descriptor))) {
				return new XPathDocument (sr);
			}
		}

		static Stream GetResource (string descriptor)
		{
			return Assembly.GetExecutingAssembly ().GetManifestResourceStream (descriptor);
		}
	}
}
