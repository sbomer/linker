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

		void ShowLongestSubsequence ()
		{
			// extract the longest repeated subsequence.
			// using suffix array
			List<int> longestRepeatedSubstring = Context.SuffixArray.GetLongestRepeatedSubstring();
			Console.WriteLine("Longest subsequence:");
			foreach (int i in longestRepeatedSubstring) {
				Console.Write(i.ToString("X8"));
				Console.WriteLine(": " + Context.InstructionMap[i]);
			}
		}

		protected override void Process()
		{

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
			if (targetAssembly != null) {

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


			// dedupe identical methods A and B in console test app

			// TODO: debug this. why doesn't getting Object from the targetAsm's typesystem work?
			var objectType = Context.Resolve("System.Private.CoreLib").FindType("System.Object");


			targetAsm = Context.Resolve ("console"); // TODO: get this more directly?
			targetType = new TypeDefinition (
				"", "__OutlinedInstructionSequences__",
				TypeAttributes.Public | TypeAttributes.Sealed, // TODO: pick proper TypeAttributes?
				targetAsm.MainModule.ImportReference (objectType)
			);
			targetAsm.MainModule.Types.Add (targetType);

#if false
			var duplicateMethods = Context.Resolve("console").FindType("console.Program").Methods.Where (m => m.Name == "Dup1" || m.Name == "Dup2");
			var outlined = CreateEntireOutlinedMethod (duplicateMethods.First ());
			foreach (var dup in duplicateMethods) {
				var instructions = dup.Body.Instructions;
				var outlinedRef = dup.Module.ImportReference (outlined);
				instructions.Clear ();
				dup.Body.Variables.Clear ();

				instructions.Add (Instruction.Create (OpCodes.Jmp, outlinedRef));
			}
#endif

			// look for duplicates by hash
// 			targetAsm = Context.Resolve ("System.Private.CoreLib");
// 			targetType = new TypeDefinition (
// 				"", "__OutlinedInstructionSequences__",
// 				TypeAttributes.Public | TypeAttributes.Sealed, // TODO: pick proper TypeAttributes?
// 				targetAsm.MainModule.ImportReference (objectType)
// 			);
// 			targetAsm.MainModule.Types.Add (targetType);

			var numOutlinedMethods = 0;
			var numOutlinedInstructions = 0;
			var numReplacedMethods = 0;
			var numReplacedInstructions = 0;
			foreach (var (hash, methods) in Context.IdenticalMethods) {
				if (methods.Count () < 2)
					continue;

				var outlined = CreateEntireOutlinedMethod (methods.First ());
				numOutlinedMethods++;
				numOutlinedInstructions += outlined.Body.Instructions.Count;

				foreach (var method in methods) {
					var outlinedRef = method.Module.ImportReference (outlined);
					var instructions = method.Body.Instructions;
					instructions.Clear ();
					method.Body.Variables.Clear ();
					instructions.Add (Instruction.Create (OpCodes.Jmp, outlinedRef));

					numReplacedMethods++;
					numReplacedInstructions += outlined.Body.Instructions.Count;
				}
			};

			var instructionCountDiff =
				-numReplacedInstructions // removed instructions from callees
				+ numReplacedMethods // one jmp instruction per callee
				+ numOutlinedInstructions; // instructions of outlined methods

			Console.WriteLine ("outlined " + numOutlinedMethods + " methods with " + numOutlinedInstructions + " instructions");
			Console.WriteLine ("replaced " + numReplacedInstructions + " intsructions across " + numReplacedMethods + " methods");
			Console.WriteLine ("diff in #instructions: " + instructionCountDiff);
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

		MethodDefinition CreateEntireOutlinedMethod(MethodDefinition method) {
			var methodAttributes = (MethodAttributes)0;

			methodAttributes |= MethodAttributes.Public;

			if (method.IsStatic)
				methodAttributes |= MethodAttributes.Static;

			if (method.IsVirtual)
				methodAttributes |= MethodAttributes.Virtual;

			var targetMethod = new MethodDefinition(
					intrinsicId.ToString (),
					methodAttributes,
					method.ReturnType
			);
			intrinsicId++;

			// signature should match
			foreach (var param in method.Parameters) {
				targetMethod.Parameters.Add (param);
			}

			// should have same locals
			foreach (var local in method.Body.Variables) {
				targetMethod.Body.Variables.Add (local);
			}

			targetType.Methods.Add (targetMethod);
			var instructions = targetMethod.Body.Instructions;

			var targetModule = targetMethod.Module;
			foreach (var inst in method.Body.Instructions) {
				// instructions that reference other members need to be able to access them
				// from the target module
				switch (inst.Operand) {
				case MethodReference methodRef:
					inst.Operand = targetModule.ImportReference (methodRef);
					break;
				case FieldReference fieldRef:
					inst.Operand = targetModule.ImportReference (fieldRef);
					break;
				}

				instructions.Add (inst);
			}

			return targetMethod;
		}

		// DANGER: this assumes that the instruction sequence has the following stack effect:
		//   pops nargs integers
		//   leaves 1 integer on the stack.
		// The produced method will set up the stack with nargs integers (from method arguments),
		// and will return the integer left on the stack.
		MethodDefinition CreateOutlinedMethod((MethodDefinition method, int start, int end, int nargs) sequence) {
			// create a method to contain the outlined instructions
			var targetMethod = new MethodDefinition(
				intrinsicId.ToString (),
				MethodAttributes.Public | MethodAttributes.Static, // TODO: pick propert MethodAttributes?
				targetType.Module.TypeSystem.String // TODO: pick proper return type?
			);
			intrinsicId++;

			targetType.Methods.Add (targetMethod);
			var instructions = targetMethod.Body.Instructions;

			// push arguments onto the stack
			// TODO: is the order correct? I think the arguments are in order on the stack
			// before the call, so they need to be pushed in order.
			for (int i = 0; i < sequence.nargs; i++) {
				var parameter = new ParameterDefinition (targetType.Module.TypeSystem.Int32);
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
