using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
// using Gma.DataStructures.StringSearch;
using SuffixTree;
using System.Collections.Generic;

namespace Mono.Linker.Steps
{
	public class OutlinerStep : BaseStep
	{
		StringBuilder sb;
		Dictionary<char, Instruction> instruction;

		SuffixTree.SuffixTree SuffixTree {
			get => Context.SuffixTree;
		}

		Dictionary<char, Instruction> InstructionMap {
			get => Context.InstructionMap;
		}

		public OutlinerStep ()
		{
			instruction = new Dictionary<char, Instruction> ();
			sb = new StringBuilder ();
		}

		protected override void Process ()
		{
			Context.SuffixTree = new SuffixTree.SuffixTree ();
			Context.InstructionMap = new Dictionary<char, Instruction> ();
		}

		protected override void ProcessAssembly (AssemblyDefinition assembly)
		{
			if (Annotations.GetAction (assembly) != AssemblyAction.Link)
				return;

			foreach (var type in assembly.MainModule.Types)
				ProcessType (type);
		}

		void ProcessType (TypeDefinition type)
		{
			foreach (var method in type.Methods) {
				if (method.HasBody)
					ProcessMethod (method);
			}

			foreach (var nested in type.NestedTypes)
				ProcessType (nested);
		}

		void ProcessMethod (MethodDefinition method)
		{
			if (!(method.ToString ().Contains ("Mono.Linker.Tests.Cases.Outlining")))
//					method.ToString ().Contains ("Main")))
				return;	

			foreach (var instr in method.Body.Instructions) {
				// encode instruction as a character
				var c = (char) instr.ToString ().GetHashCode ();

				// track mapping from character -> instruction
				if (!instruction.TryAdd (c, instr)) {
					Debug.Assert (instruction[c].ToString () == instr.ToString ());
				}

				// insert into suffix tree
				SuffixTree.ExtendTree (c);
				sb.Append (c);
			}

			// insert a unique terminator for each method body
			var terminator = (char) method.GetHashCode ();
			SuffixTree.ExtendTree (terminator);
			sb.Append (terminator);

			Console.WriteLine("Method: " + method);
			var p = SuffixTree.PrintTree ();
			Console.WriteLine(p);
		}

		protected override void EndProcess() {
			// print out longest subsequence
			var chars = sb.ToString ();
			Context.InstructionSequence = chars;
		}
	}
}
