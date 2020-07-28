using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
// using Gma.DataStructures.StringSearch;
using SuffixTree;
using System.Collections.Generic;

namespace Mono.Linker.Steps
{
	public class OutlineRewriterStep : BaseStep
	{

		public OutlineRewriterStep ()
		{
		}

		protected override void Process()
		{
			// extract the longest repeated subsequence.
			
			// using suffixtree... didn't get it working
			// var (length, end) = Context.SuffixTree.GetLongestRepeatedSubstring();
			// var start = end - length;
			// Console.WriteLine("longest subsequence:");
			// var decoded = SuffixTree.SuffixTree.DecodeLabel(Context.InstructionSequence.Substring(start, length));
			// Console.WriteLine(decoded);
			// decode the instruction string back into instructions.
			// TODO: this doesn't seem to work yet.
			// a key isn't found in the dictionary...
			// for (var i = start; i < end; i++) {
			// 	var c = Context.InstructionSequence[i];
			// 	var instr = Context.InstructionMap[c];
			// 	Console.WriteLine(instr.ToString ());
			// }

			// using suffix array
			List<int> longestRepeatedSubstring = Context.SuffixArray.GetLongestRepeatedSubstring();
			Console.WriteLine("Longest subsequence:");
			foreach (int i in longestRepeatedSubstring) {
				Console.Write(i.ToString("X8"));
				Console.WriteLine(": " + Context.InstructionMap[i]);
			}


			// just put the extracted sequences into corelib for now
			var targetAsm = Context.Resolve ("System.Private.CoreLib"); // TODO: get this more directly?
			targetType = new TypeDefinition (
				"", "__OutlinedInstructionSequences__",
				TypeAttributes.NotPublic | TypeAttributes.Sealed // TODO: pick proper TypeAttributes?
			);
			targetAsm.MainModule.Types.Add (targetType);


			// pretend that we have identified an eligible subsequence to extract.
			// this hard-codes a known common subsequence in a testcase assembly.
			targetAssembly = Context.Resolve ("test");
			if (targetAssembly == null)
				return;
			var t = targetAssembly.FindType ("Mono.Linker.Tests.Cases.Outlining.OutliningWorks");
			var sequenceDef = t.Methods.Where (m => m.Name == "A").Single ();
			// just one instruction, a ldc.i4.s
			(MethodDefinition method, int start, int end, int nargs) subsequence = (sequenceDef, 0, sequenceDef.Body.Instructions.Count - 1, 0);
			var duplicateSequences = new List<(MethodDefinition method, int start)> {
				(t.Methods.Where (m => m.Name == "A").Single (), 0),
				(t.Methods.Where (m => m.Name == "B").Single (), 0)
			};


			var outlinedMethod = CreateOutlinedMethod (subsequence);
			foreach (var s in duplicateSequences) {
				ReplaceOutlinedInstructions (s, subsequence, outlinedMethod);
			}

			// TODO: factor this
			// do the same for another set of methods.
			sequenceDef = t.Methods.Where (m => m.Name == "AddA").Single ();
			subsequence = (sequenceDef, 2, 3, 2); // instruction range 2-3 (add), takes 2 integers from stack
			duplicateSequences = new List<(MethodDefinition method, int start)> {
				(t.Methods.Where (m => m.Name == "AddA").Single (), 2),
				(t.Methods.Where (m => m.Name == "AddB").Single (), 2)
			};
			outlinedMethod = CreateOutlinedMethod (subsequence);
			foreach (var s in duplicateSequences) {
				ReplaceOutlinedInstructions (s, subsequence, outlinedMethod);
			}
		}

		// DANGER: this assumes that the duplicate method has the same instructions as
		// the given sequence, with the specified stack effects (see notes on CreateOutlinedMethod)
		void ReplaceOutlinedInstructions (
			(MethodDefinition method, int start) duplicate,
			(MethodDefinition method, int start, int end, int nargs) sequence,
			MethodDefinition outlinedMethod) {

			// TODO: make this more efficient

			var instructions = duplicate.method.Body.Instructions;
			var outlinedMethodRef = duplicate.method.Module.ImportReference (outlinedMethod);

			// remove the extracted instruction sequence
			for (int i = 0; i < sequence.end - sequence.start; i++) {
				instructions.RemoveAt (duplicate.start);
			}

			// insert a call to the outlined method
			instructions.Insert (duplicate.start, Instruction.Create (OpCodes.Call, outlinedMethodRef));
		}

		AssemblyDefinition targetAssembly;
		TypeDefinition targetType;
		int intrinsicId = 0;

		// DANGER: this assumes that the instruction sequence has the following stack effect:
		//   pops nargs integers
		//   leaves 1 integer on the stack.
		// The produced method will set up the stack with nargs integers (from method arguments),
		// and will return the integer left on the stack.
		MethodDefinition CreateOutlinedMethod((MethodDefinition method, int start, int end, int nargs) sequence) {
			// create a method to contain the outlined instructions
			var targetMethod = new MethodDefinition(
				intrinsicId.ToString (),
				MethodAttributes.Public, // TODO: pick propert MethodAttributes?
				targetAssembly.MainModule.TypeSystem.Void // TODO: pick proper return type?
			);
			targetType.Methods.Add (targetMethod);
			var instructions = targetMethod.Body.Instructions;

			// push arguments onto the stack
			// TODO: is the order correct? I think the arguments are in order on the stack
			// before the call, so they need to be pushed in order.
			for (int i = 0; i < sequence.nargs; i++) {
				var parameter = new ParameterDefinition (targetAssembly.MainModule.TypeSystem.Int32);
				targetMethod.Parameters.Add (parameter);
				instructions.Add (
					i switch {
					0 => Instruction.Create (OpCodes.Ldarg_0),
					1 => Instruction.Create (OpCodes.Ldarg_1),
					2 => Instruction.Create (OpCodes.Ldarg_2),
					3 => Instruction.Create (OpCodes.Ldarg_3),
					_ => Instruction.Create (OpCodes.Ldarg, parameter),
					}
				);
			}

			// copy the outlined subsequence
			for (int i = sequence.start; i < sequence.end; i++) {
				instructions.Add (sequence.method.Body.Instructions [i]);
			}

			// return the integer left on the stack
			instructions.Add (Instruction.Create (OpCodes.Ret));

			return targetMethod;
		}
	}
}
