using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
// using Gma.DataStructures.StringSearch;
using SuffixTree;
using SuffixArray;
using System.Collections.Generic;

namespace Mono.Linker.Steps
{
	public class OutlinerStep : BaseStep
	{
		SuffixTree.SuffixTree SuffixTree {
			get => Context.SuffixTree;
		}

		Dictionary<int, Instruction> InstructionMap {
			get => Context.InstructionMap;
		}

		InstructionAsInt instructionAsInt;

		protected override void Process ()
		{
			Context.InstructionSequence = new List<int> ();
			Context.SuffixTree = new SuffixTree.SuffixTree ();
			Context.InstructionMap = new Dictionary<int, Instruction> ();
			instructionAsInt = new InstructionAsInt ();
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
			if (!method.IsIL || method.IsNative || !method.IsManaged)
				return;
			if (!method.HasBody)
				return;

			if (!(method.ToString ().Contains ("Mono.Linker.Tests.Cases.Outlining") ||
				method.ToString ().Contains(" console")
			))
				return;	

			Console.WriteLine("encoding method " + method.ToString());

			foreach (var instr in method.Body.Instructions) {
				// encode instruction as an int
				var c = instructionAsInt.Get (instr);

				// track mapping from character -> instruction
				if (!Context.InstructionMap.TryAdd (c, instr)) {
//					Debug.Assert (Context.InstructionMap[c].ToString () == instr.ToString ());
				}

				// insert into suffix tree
				//SuffixTree.ExtendTree (c);
				Context.InstructionSequence.Add (c);

				Console.Write(c.ToString("X8"));
				Console.WriteLine(": " + instr.ToString ());
			}

			// insert a unique terminator for each method body
			var terminator = method.GetHashCode ();
//			SuffixTree.ExtendTree (terminator);
			Context.InstructionSequence.Add (terminator);

			Console.WriteLine(terminator.ToString ("X8") + " (terminator)");
//			Convert.ToString(terminator, 16));
			Console.WriteLine();

//			Console.WriteLine("Method: " + method);
//			var p = SuffixTree.PrintTree ();
//			Console.WriteLine(p);
		}

		protected override void EndProcess() {
			// print out longest subsequence

			Context.SuffixArray = new SuffixArray.SuffixArray<int>(Context.InstructionSequence);
		}
	}
}
