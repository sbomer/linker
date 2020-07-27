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
		SuffixTree.SuffixTree suffixTree;
		StringBuilder sb;
		Random random;
		Dictionary<char, Instruction> instruction;

		public OutlinerStep ()
		{
			suffixTree = new SuffixTree.SuffixTree ();
			random = new Random (23445903);
			instruction = new Dictionary<char, Instruction> ();
			sb = new StringBuilder ();
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
			if (!(method.ToString ().Contains ("Mono.Linker.Tests.Cases.Outlining"))
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
				suffixTree.ExtendTree (c);
				sb.Append (c);
			}

			// insert a unique terminator for each method body
			var terminator = (char) method.GetHashCode ();
			suffixTree.ExtendTree (terminator);
			sb.Append (terminator);

			Console.WriteLine("Method: " + method);
			var p = suffixTree.PrintTree ();
			Console.WriteLine(p);
		}
		private string DecodeLabel(string chars) {
			// utf-16 encoded string. I just want to get out one byte at a time, and
			// represent it as a string. ignore endianness for now.
			var bytes = Encoding.Unicode.GetBytes (chars);
			return BitConverter.ToString(bytes).Replace("-", "");
		}

		protected override void EndProcess() {
			// print out longest subsequence
			var chars = sb.ToString ();
			var (length, end) = suffixTree.GetLongestRepeatedSubstring();
			var start = end - length;
			Console.WriteLine("longest subsequence:");
			var decoded = DecodeLabel(chars.Substring(start, length));
			Console.WriteLine(decoded);
			for (var i = start; i < end; i++) {
				var c = chars[i];
				var instr = instruction[c];
				Console.WriteLine(instr.ToString ());
			}
		}
	}
}
