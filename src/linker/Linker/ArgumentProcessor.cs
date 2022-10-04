// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using ILLink.Shared;

namespace Mono.Linker
{
	internal sealed class ArgumentProcessor
	{
		const string _linker = "IL Linker";

		const string resolvers = "-a|-x";

		readonly string[] args;
		internal Queue<string>? arguments;

		public ArgumentProcessor(string[] args) => this.args = args;

		public bool TryProcess(out LinkerOptions? options)
		{
			options = null;

			if (args.Length == 0) {
				Console.Error.WriteLine ("No parameters specified");
				return false;
			}

			if (!ProcessResponseFile (args, out arguments))
				return false;

			if (!ProcessArguments (out options))
				return false;

			return true;
		}

		internal static bool ProcessResponseFile (string[] args, out Queue<string> result)
		{
			result = new Queue<string> ();
			foreach (string arg in args) {
				if (arg.StartsWith ("@")) {
					try {
						string responseFileName = arg.Substring (1);
						using (var responseFileText = new StreamReader (responseFileName))
							ParseResponseFile (responseFileText, result);
					} catch (Exception e) when (e is IOException or ObjectDisposedException) {
						Console.Error.WriteLine ("Cannot read response file due to '{0}'", e.Message);
						return false;
					}
				} else {
					result.Enqueue (arg);
				}
			}

			return true;
		}

		internal static void ParseResponseFile (TextReader reader, Queue<string> result)
		{
			int cur;
			while ((cur = reader.Read ()) >= 0) {
				// skip whitespace
				while (char.IsWhiteSpace ((char) cur)) {
					if ((cur = reader.Read ()) < 0)
						break;
				}
				if (cur < 0) // EOF
					break;

				StringBuilder argBuilder = new StringBuilder ();
				bool inquote = false;

				// build up an argument one character at a time
				while (true) {
					bool copyChar = true;
					int numBackslash = 0;

					// count backslashes
					while (cur == '\\') {
						numBackslash++;
						if ((cur = reader.Read ()) < 0)
							break;
					}
					if (cur == '"') {
						if ((numBackslash % 2) == 0) {
							if (inquote && (reader.Peek () == '"')) {
								// handle "" escape sequence in a quote
								cur = reader.Read ();
							} else {
								// unescaped " begins/ends a quote
								copyChar = false;
								inquote = !inquote;
							}
						}
						// treat backslashes before " as escapes
						numBackslash /= 2;
					}
					if (numBackslash > 0)
						argBuilder.Append (new String ('\\', numBackslash));
					if (cur < 0 || (!inquote && char.IsWhiteSpace ((char) cur)))
						break;
					if (copyChar)
						argBuilder.Append ((char) cur);
					cur = reader.Read ();
				}
				result.Enqueue (argBuilder.ToString ());
			}
		}

		static void LogError (DiagnosticId id, params string[] args)
		{
			var message = MessageContainer.CreateErrorMessage (null, id, args);
			if (message == MessageContainer.Empty)
				throw new InvalidOperationException();

			if (message.Category == MessageCategory.Diagnostic ||
				message.Category == MessageCategory.Info)
				throw new InvalidOperationException ();

			if (message.IsWarningMessage (out _))
				throw new InvalidOperationException ();
			
			if (message.Category != MessageCategory.Error)
				throw new InvalidOperationException ();

			Console.WriteLine (message.ToString ());
		}

		internal static bool GetOptimizationName (string text, out CodeOptimizations optimization)
		{
			switch (text.ToLowerInvariant ()) {
			case "beforefieldinit":
				optimization = CodeOptimizations.BeforeFieldInit;
				return true;
			case "overrideremoval":
				optimization = CodeOptimizations.OverrideRemoval;
				return true;
			case "unreachablebodies":
				optimization = CodeOptimizations.UnreachableBodies;
				return true;
			case "unusedinterfaces":
				optimization = CodeOptimizations.UnusedInterfaces;
				return true;
			case "unusedtypechecks":
				optimization = CodeOptimizations.UnusedTypeChecks;
				return true;
			case "ipconstprop":
				optimization = CodeOptimizations.IPConstantPropagation;
				return true;
			case "sealer":
				optimization = CodeOptimizations.Sealer;
				return true;
			}

			LogError (DiagnosticId.InvalidOptimizationValue, text);
			optimization = 0;
			return false;
		}



		internal bool ProcessArguments (out LinkerOptions? options)
		{
			if (arguments == null)
				throw new InvalidOperationException ();
			options = null;
			// TODO: rename context
			var context = new LinkerOptionsBuilder ();
			context.MetadataTrimming = MetadataTrimming.Any;

			while (arguments.Count > 0) {
				string token = arguments.Dequeue ();
				if (token.Length < 2) {
					LogError (DiagnosticId.UnrecognizedCommandLineOption, token);
					return false;
				}

				//
				// Handling of --value like options
				//
				if (token[0] == '-' && token[1] == '-') {
					switch (token) {
					case "--help":
						Usage ();
						return true;
					case "--skip-unresolved":
						if (!GetBoolParam (token, l => context.IgnoreUnresolved = l))
							return false;

						continue;

					case "--verbose":
						context.LogMessages = true;
						continue;

					case "--dependencies-file":
						if (!GetStringParam (token, out var dependenciesFileName))
							return false;
						context.DependenciesFileName = dependenciesFileName;

						continue;

					case "--dump-dependencies":
						context.DumpDependencies = true;
						continue;

					case "--dependencies-file-format":
						if (!GetStringParam (token, out var dependenciesFileFormat))
							return false;

						if (!Enum.TryParse (dependenciesFileFormat, ignoreCase: true, out DependenciesFileFormat fileType)) {
							LogError (DiagnosticId.InvalidDependenciesFileFormat);
							return false;
						}
						context.DependenciesFileFormat = fileType;
						continue;

					case "--reduced-tracing":
						if (!GetBoolParam (token, l => context.EnableReducedTracing = l))
							return false;

						continue;

					case "--used-attrs-only":
						if (!GetBoolParam (token, l => context.KeepUsedAttributeTypesOnly = l))
							return false;

						continue;

					case "--strip-security":
						if (!GetBoolParam (token, l => context.StripSecurity = l))
							return false;

						continue;

					case "--strip-descriptors":
						if (!GetBoolParam (token, l => context.StripDescriptors = l))
							return false;

						continue;

					case "--strip-substitutions":
						if (!GetBoolParam (token, l => context.StripSubstitutions = l))
							return false;

						continue;

					case "--strip-link-attributes":
						if (!GetBoolParam (token, l => context.StripLinkAttributes = l))
							return false;

						continue;

					case "--substitutions":
						if (arguments.Count < 1) {
							ErrorMissingArgument (token);
							return false;
						}

						if (!GetStringParam (token, out string? substitutionFile))
							return false;

						context.SubstitutionsFiles.Add (substitutionFile);

						continue;

					case "--explicit-reflection":
						if (!GetBoolParam (token, l => context.AddReflectionAnnotations = l))
							return false;

						continue;

					case "--action": {
							if (!GetStringParam (token, out string? actionString))
								return false;

							AssemblyAction? action = ParseAssemblyAction (actionString, out bool needAddBypassNGenStep);
							if (action == null)
								return false;

							if (needAddBypassNGenStep)
								context.AddBypassNGenStep = true;

							string? assemblyName = GetNextStringValue ();
							if (assemblyName == null) {
								context.DefaultAction = action.Value;
								continue;
							}

							if (!IsValidAssemblyName (assemblyName)) {
								LogError (DiagnosticId.InvalidAssemblyName, assemblyName);
								return false;
							}

							context.Actions[assemblyName] = action.Value;
							continue;
						}

					case "--trim-mode": {
							if (!GetStringParam (token, out string? actionString))
								return false;

							AssemblyAction? action = ParseAssemblyAction (actionString, out bool needAddBypassNGenStep);
							if (action == null)
								return false;

							if (needAddBypassNGenStep)
								context.AddBypassNGenStep = true;

							context.TrimAction = action.Value;
							continue;
						}

					case "--custom-step":
						if (!GetStringParam (token, out string? custom_step))
							return false;

						context.CustomSteps.Add (custom_step);

						continue;

					case "--custom-data":
						if (arguments.Count < 1) {
							ErrorMissingArgument (token);
							return false;
						}

						var arg = arguments.Dequeue ();
						string[] values = arg.Split ('=');
						if (values?.Length != 2) {
							LogError (DiagnosticId.CustomDataFormatIsInvalid);
							return false;
						}

						context.CustomData[values[0]] = values[1];
						continue;

					case "--keep-compilers-resources":
						if (!GetBoolParam (token, l => context.KeepCompilersResources = l))
							return false;

						continue;

					case "--keep-dep-attributes":
						if (!GetBoolParam (token, l => context.KeepDepAttributes = l))
							return false;

						continue;

					case "--keep-metadata": {
							if (!GetStringParam (token, out string? mname))
								return false;

							if (!TryGetMetadataTrimming (mname, out var type))
								return false;

							context.MetadataTrimming &= ~type;
							continue;
						}

					case "--enable-serialization-discovery":
						if (!GetBoolParam (token, l => context.EnableSerializationDiscovery = l))
							return false;

						continue;

					case "--disable-operator-discovery":
						if (!GetBoolParam (token, l => context.DisableOperatorDiscovery = l))
							return false;

						continue;

					case "--ignore-descriptors":
						if (!GetBoolParam (token, l => context.IgnoreDescriptors = l))
							return false;

						continue;

					case "--ignore-substitutions":
						if (!GetBoolParam (token, l => context.IgnoreSubstitutions = l))
							return false;

						continue;

					case "--ignore-link-attributes":
						if (!GetBoolParam (token, l => context.IgnoreLinkAttributes = l))
							return false;

						continue;

					case "--disable-opt": {
							if (!GetStringParam (token, out string? optName))
								return false;

							if (!GetOptimizationName (optName, out var opt))
								return false;

							string? assemblyName = GetNextStringValue ();
							SetOptimization (context, opt, assemblyName, false);

							continue;
						}
					case "--enable-opt": {
							if (!GetStringParam (token, out string? optName))
								return false;

							if (!GetOptimizationName (optName, out var opt))
								return false;

							string? assemblyName = GetNextStringValue ();
							SetOptimization (context, opt, assemblyName, true);

							continue;
						}

					case "--feature": {
							if (!GetStringParam (token, out string? featureName))
								return false;

							if (!GetBoolParam (token, value => {
								Debug.Assert (!string.IsNullOrEmpty (featureName));
								context.FeatureSettings[featureName] = value;
							}))
								return false;

							continue;
						}

					case "--new-mvid":
						//
						// This is not same as --deterministic which calculates MVID
						// from stable assembly content. This option creates a new random
						// mvid or uses mvid of the source assembly.
						//
						if (!GetBoolParam (token, l => {
							context.NewMvid = true;
						}))
							return false;
						continue;

					case "--deterministic":
						if (!GetBoolParam (token, l => context.Deterministic = l))
							return false;

						continue;

					case "--output-assemblylist":
						if (!GetStringParam (token, out string? assemblyListFile))
							return false;

						context.AssemblyListFile = assemblyListFile;

						continue;

					case "--output-pinvokes":
						if (!GetStringParam (token, out string? pinvokesListFile))
							return false;

						context.PInvokesListFile = pinvokesListFile;

						continue;

					case "--link-attributes":
						if (arguments.Count < 1) {
							ErrorMissingArgument (token);
							return false;
						}

						if (!GetStringParam (token, out string? fileList))
							return false;

						foreach (string file in GetFiles (fileList))
							context.LinkAttributesFiles.Add (file);

						continue;

					case "--generate-warning-suppressions":
						if (!GetStringParam (token, out string? generateWarningSuppressionsArgument))
							return false;

						if (!GetWarningSuppressionWriterFileOutputKind (generateWarningSuppressionsArgument, out var fileOutputKind)) {
							LogError (DiagnosticId.InvalidGenerateWarningSuppressionsValue, generateWarningSuppressionsArgument);
							return false;
						}

						context.WarningSuppressionFileOutputKind = fileOutputKind;
						continue;

					case "--notrimwarn":
						context.NoTrimWarn = true;
						continue;

					case "--nowarn":
						if (!GetStringParam (token, out string? noWarnArgument))
							return false;

						context.NoWarn.UnionWith (ProcessWarningCodes (noWarnArgument));
						continue;

					case "--warnaserror":
					case "--warnaserror+":
						var warningList = GetNextStringValue ();
						if (!string.IsNullOrEmpty (warningList)) {
							foreach (var warning in ProcessWarningCodes (warningList))
								context.WarnAsError[warning] = true;

						} else {
							context.GeneralWarnAsError = true;
							context.WarnAsError.Clear ();
						}

						continue;

					case "--warnaserror-":
						warningList = GetNextStringValue ();
						if (!string.IsNullOrEmpty (warningList)) {
							foreach (var warning in ProcessWarningCodes (warningList))
								context.WarnAsError[warning] = false;

						} else {
							context.GeneralWarnAsError = false;
							context.WarnAsError.Clear ();
						}

						continue;

					case "--warn":
						if (!GetStringParam (token, out string? warnVersionArgument))
							return false;

						if (!GetWarnVersion (warnVersionArgument, out WarnVersion version))
							return false;

						context.WarnVersion = version;

						continue;

					case "--singlewarn":
					case "--singlewarn+": {
							string? assemblyName = GetNextStringValue ();
							if (assemblyName != null) {
								if (!IsValidAssemblyName (assemblyName)) {
									LogError (DiagnosticId.InvalidAssemblyName, assemblyName);
									return false;
								}

								context.SingleWarn[assemblyName] = true;
							} else {
								context.GeneralSingleWarn = true;
								context.SingleWarn.Clear ();
							}

							continue;
						}

					case "--singlewarn-": {
							string? assemblyName = GetNextStringValue ();
							if (assemblyName != null) {
								if (!IsValidAssemblyName (assemblyName)) {
									LogError (DiagnosticId.InvalidAssemblyName, assemblyName);
									return false;
								}

								context.SingleWarn[assemblyName] = false;
							} else {
								context.GeneralSingleWarn = false;
								context.SingleWarn.Clear ();
							}

							continue;
						}

					case "--version":
						Version ();
						return true;

					case "--about":
						About ();
						return true;
					}
				}

				if (token[0] == '-' || token[1] == '/') {

					switch (token.Substring (1)) {
					case "d":
						if (!GetStringParam (token, out string? directory))
							return false;

						DirectoryInfo info = new DirectoryInfo (directory);
						context.SearchDirectories.Add (info.FullName);

						continue;
					case "o":
					case "out":
						if (!GetStringParam (token, out string? outputDirectory))
							return false;

						context.OutputDirectory = outputDirectory;

						continue;
					case "x": {
							if (!GetStringParam (token, out string? xmlFile))
								return false;

							if (!File.Exists (xmlFile)) {
								LogError (DiagnosticId.XmlDescriptorCouldNotBeFound, xmlFile);
								return false;
							}

							// context.XmlDescriptors.Add (xmlFile);
							context.RootInputs.Add ((xmlFile, new(RootFileType.Xml)));
							continue;
						}
					case "a": {
							if (!GetStringParam (token, out string? assemblyFile))
								return false;

							if (!File.Exists (assemblyFile) && assemblyFile.EndsWith (".dll", StringComparison.InvariantCultureIgnoreCase)) {
								LogError (DiagnosticId.RootAssemblyCouldNotBeFound, assemblyFile);
								return false;
							}

							AssemblyRootMode rmode = AssemblyRootMode.Default;
							var rootMode = GetNextStringValue ();
							if (rootMode != null) {
								var parsed_rmode = ParseAssemblyRootsMode (rootMode);
								if (parsed_rmode is null)
									return false;

								rmode = parsed_rmode.Value;
							}

							context.RootInputs.Add ((assemblyFile, new (RootFileType.Assembly, rmode)));
							continue;
						}
					case "b":
						if (!GetBoolParam (token, l => context.LinkSymbols = l))
							return false;

						continue;
					case "g":
						if (!GetBoolParam (token, l => context.Deterministic = !l))
							return false;

						continue;
					case "z":
						if (!GetBoolParam (token, l => context.IgnoreDescriptors = !l))
							return false;

						continue;
					case "?":
					case "h":
					case "help":
						Usage ();
						return true;

					case "reference":
						if (!GetStringParam (token, out string? reference))
							return false;

						context.ReferenceAssemblies.Add (reference);
						continue;
					}
				}

				LogError (DiagnosticId.UnrecognizedCommandLineOption, token);
				return false;
			}

			if (context.RootInputs.Count == 0) {
				LogError (DiagnosticId.NoFilesToLinkSpecified, resolvers);
				return false;
			}

			if (context.NewMvid && context.Deterministic) {
				LogError (DiagnosticId.NewMvidAndDeterministicCannotBeUsedAtSameTime);
				return false;
			}

			options = context.Build ();
			return true;
		}

		static void Version ()
		{
			Console.WriteLine ("{0} Version {1}",
				_linker,
				System.Reflection.Assembly.GetExecutingAssembly ().GetName ().Version);
		}

		static void About ()
		{
			Console.WriteLine ("For more information, visit the project Web site");
			Console.WriteLine ("   https://github.com/dotnet/linker");
		}

		static bool GetWarnVersion (string text, out WarnVersion version)
		{
			if (int.TryParse (text, out int versionNum)) {
				version = (WarnVersion) versionNum;
				if (version >= WarnVersion.ILLink0 && version <= WarnVersion.Latest)
					return true;
			}

			LogError (DiagnosticId.InvalidWarningVersion, text);
			version = 0;
			return false;
		}


		private static IEnumerable<int> ProcessWarningCodes (string value)
		{
			string Unquote (string arg)
			{
				if (arg.Length > 1 && arg[0] == '"' && arg[arg.Length - 1] == '"')
					return arg.Substring (1, arg.Length - 2);

				return arg;
			}

			value = Unquote (value);
			string[] values = value.Split (new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string v in values) {
				var id = v.Trim ();
				if (!id.StartsWith ("IL", StringComparison.Ordinal) || !ushort.TryParse (id.AsSpan (2), out ushort code))
					continue;

				yield return code;
			}
		}

		static string[] GetFiles (string param)
		{
			if (param.Length < 1 || param[0] != '@')
				return new string[] { param };

			string file = param.Substring (1);
			return ReadLines (file);
		}

		static void SetOptimization (LinkerOptionsBuilder options, CodeOptimizations opt, string? assemblyName, bool value)
		{
			switch (opt) {
			case CodeOptimizations.BeforeFieldInit:
				if (assemblyName == null)
					options.BeforeFieldInit = value;
				else
					options.AssemblyBeforeFieldInit[assemblyName] = value;
				break;
			case CodeOptimizations.OverrideRemoval:
				if (assemblyName == null)
					options.OverrideRemoval = value;
				else
					options.AssemblyOverrideRemoval[assemblyName] = value;
				break;
			case CodeOptimizations.UnreachableBodies:
				if (assemblyName == null)
					options.UnreachableBodies = value;
				else
					options.AssemblyUnreachableBodies[assemblyName] = value;
				break;
			case CodeOptimizations.UnusedInterfaces:
				if (assemblyName == null)
					options.UnusedInterfaces = value;
				else
					options.AssemblyUnusedInterfaces[assemblyName] = value;
				break;
			case CodeOptimizations.UnusedTypeChecks:
				if (assemblyName == null)
					options.UnusedTypeChecks = value;
				else
					options.AssemblyUnusedTypeChecks[assemblyName] = value;
				break;
			case CodeOptimizations.IPConstantPropagation:
				if (assemblyName == null)
					options.IPConstantPropagation = value;
				else
					options.AssemblyIPConstantPropagation[assemblyName] = value;
				break;
			case CodeOptimizations.Sealer:
				if (assemblyName == null)
					options.Sealer = value;
				else
					options.AssemblySealer[assemblyName] = value;
				break;
			default:
				throw new InvalidOperationException ();
			}
		}

		string? GetNextStringValue ()
		{
			if (arguments!.Count < 1)
				return null;	

			var arg = arguments!.Peek ();
			if (arg.StartsWith ("-") || arg.StartsWith ("/"))
				return null;

			arguments!.Dequeue ();
			return arg;
		}

		static AssemblyRootMode? ParseAssemblyRootsMode (string s)
		{
			switch (s.ToLowerInvariant ()) {
			case "default":
				return AssemblyRootMode.Default;
			case "all":
				return AssemblyRootMode.AllMembers;
			case "visible":
				return AssemblyRootMode.VisibleMembers;
			case "entrypoint":
				return AssemblyRootMode.EntryPoint;
			case "library":
				return AssemblyRootMode.Library;
			}

			LogError (DiagnosticId.InvalidAssemblyRootMode, s);
			return null;
		}

		static bool TryGetMetadataTrimming (string text, out MetadataTrimming metadataTrimming)
		{
			switch (text.ToLowerInvariant ()) {
			case "all":
				metadataTrimming = MetadataTrimming.Any;
				return true;
			case "none":
				metadataTrimming = MetadataTrimming.None;
				return true;
			case "parametername":
				metadataTrimming = MetadataTrimming.ParameterName;
				return true;
			}

			LogError (DiagnosticId.InvalidMetadataOption, text);
			metadataTrimming = 0;
			return false;
		}

		static bool IsValidAssemblyName (string value)
		{
			return !string.IsNullOrEmpty (value);
		}


		static AssemblyAction? ParseAssemblyAction (string s, out bool needAddBypassNGenStep)
		{
			needAddBypassNGenStep = false;
			switch (s.ToLowerInvariant ()) {
			case "copy":
				return AssemblyAction.Copy;
			case "copyused":
				return AssemblyAction.CopyUsed;
			case "link":
				return AssemblyAction.Link;
			case "skip":
				return AssemblyAction.Skip;

			case "addbypassngen":
				needAddBypassNGenStep = true;
				return AssemblyAction.AddBypassNGen;
			case "addbypassngenused":
				needAddBypassNGenStep = true;
				return AssemblyAction.AddBypassNGenUsed;
			}

			LogError (DiagnosticId.InvalidAssemblyAction, s);
			return null;
		}

		bool GetBoolParam (string token, Action<bool> action)
		{
			if (arguments!.Count == 0) {
				action (true);
				return true;
			}

			var arg = arguments!.Peek ();
			if (bool.TryParse (arg.ToLowerInvariant (), out bool value)) {
				arguments!.Dequeue ();
				action (value);
				return true;
			}

			if (arg.StartsWith ("-") || arg.StartsWith ("/")) {
				action (true);
				return true;
			}

			LogError (DiagnosticId.InvalidArgumentForTokenOption, token);
			return false;
		}

		static bool GetWarningSuppressionWriterFileOutputKind (string text, out WarningSuppressionWriter.FileOutputKind fileOutputKind)
		{
			switch (text.ToLowerInvariant ()) {
			case "cs":
				fileOutputKind = WarningSuppressionWriter.FileOutputKind.CSharp;
				return true;

			case "xml":
				fileOutputKind = WarningSuppressionWriter.FileOutputKind.Xml;
				return true;

			default:
				fileOutputKind = WarningSuppressionWriter.FileOutputKind.CSharp;
				return false;
			}
		}

		bool GetStringParam (string token, [NotNullWhen (true)] out string? value)
		{
			value = null;
			if (arguments!.Count < 1) {
				ErrorMissingArgument (token);
				return false;
			}

			var arg = arguments!.Dequeue ();
			if (!string.IsNullOrEmpty (arg)) {
				value = arg;
				return true;
			}

			ErrorMissingArgument (token);
			return false;
		}

		static string[] ReadLines (string file)
		{
			var lines = new List<string> ();
			using (StreamReader reader = new StreamReader (file)) {
				string? line;
				while ((line = reader.ReadLine ()) != null)
					lines.Add (line);
			}
			return lines.ToArray ();
		}


		static void ErrorMissingArgument (string optionName)
		{
			LogError (DiagnosticId.MissingArgumentForCommanLineOptionName, optionName);
		}

		static void Usage ()
		{
			Console.WriteLine (_linker);

			Console.WriteLine ($"illink [options] {resolvers}");
			Console.WriteLine ("  -a FILE [MODE]      Assembly file used as root assembly with optional MODE value to alter default root mode");
			Console.WriteLine ("                      Mode can be one of the following values");
			Console.WriteLine ("                        all: Keep all members in root assembly");
			Console.WriteLine ("                        default: Use entry point for applications and all members for libraries");
			Console.WriteLine ("                        entrypoint: Use assembly entry point as only root in the assembly");
			Console.WriteLine ("                        library: All assembly members and data needed for secondary trimming are retained");
			Console.WriteLine ("                        visible: Keep all members and types visible outside of the assembly");
			Console.WriteLine ("  -x FILE             XML descriptor file with members to be kept");

			Console.WriteLine ();
			Console.WriteLine ("Options");
			Console.WriteLine ("  -d PATH             Specify additional directory to search in for assembly references");
			Console.WriteLine ("  -reference FILE     Specify additional file location used to resolve assembly references");
			Console.WriteLine ("  -b                  Update debug symbols for all modified files. Defaults to false");
			Console.WriteLine ("  -out PATH           Specify the output directory. Defaults to 'output'");
			Console.WriteLine ("  -h                  Lists all {0} options", _linker);
			Console.WriteLine ("  @FILE               Read response file for more options");
			Console.WriteLine ("  --create-repro DIR  Create a repro directory in DIR containing all input assemblies and the command line used to run the linker");

			Console.WriteLine ();
			Console.WriteLine ("Actions");
			Console.WriteLine ("  --trim-mode ACTION  Sets action for assemblies annotated with IsTrimmable attribute. Defaults to 'link'");
			Console.WriteLine ("                          copy: Analyze whole assembly and save it to the output");
			Console.WriteLine ("                          copyused: Same as copy but only for assemblies which are needed");
			Console.WriteLine ("                          link: Remove any unused IL or metadata and optimizes the assembly");
			Console.WriteLine ("                          skip: Do not process the assembly");
			Console.WriteLine ("                          addbypassngen: Add BypassNGenAttribute to unused methods");
			Console.WriteLine ("                          addbypassngenused: Same as addbypassngen but unused assemblies are removed");
			Console.WriteLine ("  --action ACTION       Sets action for assemblies that have no IsTrimmable attribute. Defaults to 'link'");
			Console.WriteLine ("  --action ACTION ASM   Overrides the default action for specific assembly name");

			Console.WriteLine ();
			Console.WriteLine ("Advanced Options");
			Console.WriteLine ("  --about                   About the {0}", _linker);
			Console.WriteLine ("  --custom-step CFG         Add a custom step <config> to the existing pipeline");
			Console.WriteLine ("                            Step can use one of following configurations");
			Console.WriteLine ("                            TYPE,PATH_TO_ASSEMBLY: Add user defined type as last step to the pipeline");
			Console.WriteLine ("                            -NAME:TYPE,PATH_TO_ASSEMBLY: Inserts step type before existing step with name");
			Console.WriteLine ("                            +NAME:TYPE,PATH_TO_ASSEMBLY: Add step type after existing step");
			Console.WriteLine ("  --custom-data KEY=VALUE   Populates context data set with user specified key-value pair");
			Console.WriteLine ("  --deterministic           Produce a deterministic output for modified assemblies");
			Console.WriteLine ("  --ignore-descriptors      Skips reading embedded descriptors (short -z). Defaults to false");
			Console.WriteLine ("  --skip-unresolved         Ignore unresolved types, methods, and assemblies. Defaults to false");
			Console.WriteLine ("  --output-pinvokes PATH    Output a JSON file with all modules and entry points of the P/Invokes found");
			Console.WriteLine ("  --verbose                 Log messages indicating progress and warnings");
			Console.WriteLine ("  --nowarn WARN             Disable specific warning messages");
			Console.WriteLine ("  --warn VERSION            Only print out warnings with version <= VERSION. Defaults to '9999'");
			Console.WriteLine ("                              VERSION is an integer in the range 0-9999.");
			Console.WriteLine ("  --warnaserror[+|-]        Report all warnings as errors");
			Console.WriteLine ("  --warnaserror[+|-] WARN   Report specific warnings as errors");
			Console.WriteLine ("  --singlewarn[+|-]         Show at most one analysis warning per assembly");
			Console.WriteLine ("  --singlewarn[+|-] ASM     Show at most one analysis warning for a specific assembly");
			Console.WriteLine ("  --version                 Print the version number of the {0}", _linker);

			Console.WriteLine ();
			Console.WriteLine ("Trimming");
			Console.WriteLine ("  --disable-opt NAME [ASM]   Disable one of the default optimizations globaly or for a specific assembly name");
			Console.WriteLine ("                               beforefieldinit: Unused static fields are removed if there is no static ctor");
			Console.WriteLine ("                               ipconstprop: Interprocedural constant propagation on return values");
			Console.WriteLine ("                               overrideremoval: Overrides of virtual methods on types that are never instantiated are removed");
			Console.WriteLine ("                               unreachablebodies: Instance methods that are marked but not executed are converted to throws");
			Console.WriteLine ("                               unusedinterfaces: Removes interface types from declaration when not used");
			Console.WriteLine ("                               unusedtypechecks: Inlines never successful type checks");
			Console.WriteLine ("  --enable-opt NAME [ASM]    Enable one of the additional optimizations globaly or for a specific assembly name");
			Console.WriteLine ("                               sealer: Any method or type which does not have override is marked as sealed");
			Console.WriteLine ("  --explicit-reflection      Adds to members never used through reflection DisablePrivateReflection attribute. Defaults to false");
			Console.WriteLine ("  --feature FEATURE VALUE    Apply any optimizations defined when this feature setting is a constant known at link time");
			Console.WriteLine ("  --keep-compilers-resources Keep assembly resources used for F# compilation resources. Defaults to false");
			Console.WriteLine ("  --keep-dep-attributes      Keep attributes used for manual dependency tracking. Defaults to false");
			Console.WriteLine ("  --keep-metadata NAME       Keep metadata which would otherwise be removed if not used");
			Console.WriteLine ("                               all: Metadata for any member are all kept");
			Console.WriteLine ("                               parametername: All parameter names are kept");
			Console.WriteLine ("  --new-mvid                 Generate a new guid for each linked assembly (short -g). Defaults to true");
			Console.WriteLine ("  --strip-descriptors        Remove XML descriptor resources for linked assemblies. Defaults to true");
			Console.WriteLine ("  --strip-security           Remove metadata and code related to Code Access Security. Defaults to true");
			Console.WriteLine ("  --substitutions FILE       Configuration file with field or methods substitution rules");
			Console.WriteLine ("  --ignore-substitutions     Skips reading embedded substitutions. Defaults to false");
			Console.WriteLine ("  --strip-substitutions      Remove XML substitution resources for linked assemblies. Defaults to true");
			Console.WriteLine ("  --used-attrs-only          Attribute usage is removed if the attribute type is not used. Defaults to false");
			Console.WriteLine ("  --link-attributes FILE     Supplementary custom attribute definitions for attributes controlling the linker behavior.");
			Console.WriteLine ("  --ignore-link-attributes   Skips reading embedded attributes. Defaults to false");
			Console.WriteLine ("  --strip-link-attributes    Remove XML link attributes resources for linked assemblies. Defaults to true");

			Console.WriteLine ();
			Console.WriteLine ("Analyzer");
			Console.WriteLine ("  --dependencies-file FILE              Specify the dependencies output. Defaults to 'output/linker-dependencies.xml'");
			Console.WriteLine ("                                        if 'xml' is file format, 'output/linker-dependencies.dgml if 'dgml' is file format");
			Console.WriteLine ("  --dump-dependencies                   Dump dependencies for the linker analyzer tool");
			Console.WriteLine ("  --dependencies-file-format FORMAT     Specify output file type. Defaults to 'xml'");
			Console.WriteLine ("                                          xml: outputs an .xml file");
			Console.WriteLine ("                                          dgml: outputs a .dgml file");
			Console.WriteLine ("  --reduced-tracing                     Reduces dependency output related to assemblies that will not be modified");
			Console.WriteLine ("");
		}
	}
}