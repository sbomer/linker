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
			Context.IdenticalMethods = new Dictionary<int, List<MethodDefinition>> ();
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

		bool IsInstructionEligibleForOutlining (Instruction instr)
		{
			switch (instr.OpCode.Code) {
				case Code.Add:
				case Code.Add_Ovf:
				case Code.Add_Ovf_Un:
				case Code.And:
				case Code.Div:
				case Code.Div_Un:
				case Code.Mul:
				case Code.Mul_Ovf:
				case Code.Mul_Ovf_Un:
				case Code.Or:
				case Code.Rem:
				case Code.Rem_Un:
				case Code.Sub:
				case Code.Sub_Ovf:
				case Code.Sub_Ovf_Un:
				case Code.Xor:
				case Code.Cgt:
				case Code.Cgt_Un:
				case Code.Clt:
				case Code.Clt_Un:
				case Code.Ldelem_I:
				case Code.Ldelem_I1:
				case Code.Ldelem_I2:
				case Code.Ldelem_I4:
				case Code.Ldelem_I8:
				case Code.Ldelem_R4:
				case Code.Ldelem_R8:
				case Code.Ldelem_U1:
				case Code.Ldelem_U2:
				case Code.Ldelem_U4:
				case Code.Shl:
				case Code.Shr:
				case Code.Shr_Un:
				case Code.Ldelem_Any:
				case Code.Ldelem_Ref:
				case Code.Ldelema:
				case Code.Ceq:
					// pop 2, push 1
					return true;

				case Code.Dup:
					// push 1
					return true;

				case Code.Ldnull:
					// push 1
					return true;

				case Code.Ldc_I4_0:
				case Code.Ldc_I4_1:
				case Code.Ldc_I4_2:
				case Code.Ldc_I4_3:
				case Code.Ldc_I4_4:
				case Code.Ldc_I4_5:
				case Code.Ldc_I4_6:
				case Code.Ldc_I4_7:
				case Code.Ldc_I4_8:
					// push 1
					return true;

				case Code.Ldc_I4_M1:
					// push 1
					return true;

				case Code.Ldc_I4:
					// push 1
					return true;

				case Code.Ldc_I4_S:
					// push 1
					return true;

				case Code.Arglist:
				case Code.Ldftn:
				case Code.Sizeof:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
					// push 1
					return true;

				case Code.Ldarg:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
				case Code.Ldarg_S:
				case Code.Ldarga:
				case Code.Ldarga_S:
					// push 1
					// don't support ldarg
					return false;

				case Code.Ldloc:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
				case Code.Ldloc_S:
				case Code.Ldloca:
				case Code.Ldloca_S:
					// push 1
					// don't support ldloc
					return false;

				case Code.Ldstr:
					return true;

				case Code.Ldtoken:
					return true;

				case Code.Ldind_I:
				case Code.Ldind_I1:
				case Code.Ldind_I2:
				case Code.Ldind_I4:
				case Code.Ldind_I8:
				case Code.Ldind_R4:
				case Code.Ldind_R8:
				case Code.Ldind_U1:
				case Code.Ldind_U2:
				case Code.Ldind_U4:
				case Code.Ldlen:
				case Code.Ldvirtftn:
				case Code.Localloc:
				case Code.Refanytype:
				case Code.Refanyval:
				case Code.Conv_I1:
				case Code.Conv_I2:
				case Code.Conv_I4:
				case Code.Conv_Ovf_I1:
				case Code.Conv_Ovf_I1_Un:
				case Code.Conv_Ovf_I2:
				case Code.Conv_Ovf_I2_Un:
				case Code.Conv_Ovf_I4:
				case Code.Conv_Ovf_I4_Un:
				case Code.Conv_Ovf_U:
				case Code.Conv_Ovf_U_Un:
				case Code.Conv_Ovf_U1:
				case Code.Conv_Ovf_U1_Un:
				case Code.Conv_Ovf_U2:
				case Code.Conv_Ovf_U2_Un:
				case Code.Conv_Ovf_U4:
				case Code.Conv_Ovf_U4_Un:
				case Code.Conv_U1:
				case Code.Conv_U2:
				case Code.Conv_U4:
				case Code.Conv_I8:
				case Code.Conv_Ovf_I8:
				case Code.Conv_Ovf_I8_Un:
				case Code.Conv_Ovf_U8:
				case Code.Conv_Ovf_U8_Un:
				case Code.Conv_U8:
				case Code.Conv_I:
				case Code.Conv_Ovf_I:
				case Code.Conv_Ovf_I_Un:
				case Code.Conv_U:
				case Code.Conv_R_Un:
				case Code.Conv_R4:
				case Code.Conv_R8:
				case Code.Ldind_Ref:
				case Code.Ldobj:
				case Code.Mkrefany:
				case Code.Unbox:
				case Code.Unbox_Any:
				case Code.Box:
				case Code.Neg:
				case Code.Not:
					// pop 1, push 1
					return true;

				case Code.Isinst:
				case Code.Castclass:
					return true;

				case Code.Ldfld:
				case Code.Ldsfld:
				case Code.Ldflda:
				case Code.Ldsflda:
					// push 1
					return true;

				case Code.Newarr:
					// pop 1, puhs 1
					return true;

				case Code.Cpblk:
				case Code.Initblk:
				case Code.Stelem_I:
				case Code.Stelem_I1:
				case Code.Stelem_I2:
				case Code.Stelem_I4:
				case Code.Stelem_I8:
				case Code.Stelem_R4:
				case Code.Stelem_R8:
				case Code.Stelem_Any:
				case Code.Stelem_Ref:
					// pop 3
					return true;

				case Code.Stfld:
				case Code.Stsfld:
					// pop 1
					return true;

				case Code.Cpobj:
					// pop 2
					return true;

				case Code.Stind_I:
				case Code.Stind_I1:
				case Code.Stind_I2:
				case Code.Stind_I4:
				case Code.Stind_I8:
				case Code.Stind_R4:
				case Code.Stind_R8:
				case Code.Stind_Ref:
				case Code.Stobj:
					// pop 2
					return true;

				case Code.Initobj:
				case Code.Pop:
					// pop 1
					return true;

				case Code.Starg:
				case Code.Starg_S:
					// pop 1
					// don't handle args
					return false;

				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
					// pop 1
					// don't handle locals
					return false;

				case Code.Constrained:
				case Code.No:
				case Code.Readonly:
				case Code.Tail:
				case Code.Unaligned:
				case Code.Volatile:
					// prefix for next instruction
					return true;

				case Code.Brfalse:
				case Code.Brfalse_S:
				case Code.Brtrue:
				case Code.Brtrue_S:
					// pop 1, jmp
					// don't handle jumps
					return false;

				case Code.Calli:
					// stack effects depend on operand signature
					return true;

				case Code.Call:
				case Code.Callvirt:
				case Code.Newobj:
					// stack effects depend on operand signature
					return true;

				case Code.Jmp:
					// Not generated by mainstream compilers
					// don't handle jumps
					return false;

				case Code.Br:
				case Code.Br_S:
					// jmp
					// don't handle jumps
					return false;

				case Code.Leave:
				case Code.Leave_S:
					// don't handle exception-handling logic
					return false;

				case Code.Endfilter:
				case Code.Endfinally:
				case Code.Rethrow:
				case Code.Throw:
					// don't handle exception-handling logic
					return false;

				case Code.Ret:
					// can't outline ret in the caller
					return false;

				case Code.Switch:
					// pop 1, jmp to index of table
					// don't handle jumps
					return false;

				case Code.Beq:
				case Code.Beq_S:
				case Code.Bne_Un:
				case Code.Bne_Un_S:
				case Code.Bge:
				case Code.Bge_S:
				case Code.Bge_Un:
				case Code.Bge_Un_S:
				case Code.Bgt:
				case Code.Bgt_S:
				case Code.Bgt_Un:
				case Code.Bgt_Un_S:
				case Code.Ble:
				case Code.Ble_S:
				case Code.Ble_Un:
				case Code.Ble_Un_S:
				case Code.Blt:
				case Code.Blt_S:
				case Code.Blt_Un:
				case Code.Blt_Un_S:
					// pop 2, jmp
					// don't handle jumps
					return false;


				case Code.Nop:
					return true;

				default:
					throw new NotImplementedException (instr.ToString ());
			}
		}


		bool IsMethodEligibleForOutlining (MethodDefinition method)
		{
			if (!method.IsIL || method.IsNative || !method.IsManaged)
				return false;

			if (!method.HasBody)
				return false;

			if (method.HasGenericParameters)
				return false;

			// TODO: some might be eligible, if they don't use generic parameter types
			if (method.DeclaringType.HasGenericParameters)
				return false;

			if (method.IsVirtual)
				return false;

			// for now, only look for test methods we define...
			if (!(method.ToString ().Contains ("Mono.Linker.Tests.Cases.Outlining") ||
				method.ToString ().Contains(" console")
			))
				return false;

			return true;
		}
		void EncodeMethodForSuffixTree (MethodDefinition method)
		{
			Console.WriteLine("encoding method " + method.ToString());

			foreach (var instr in method.Body.Instructions) {
				int c;
				if (!IsInstructionEligibleForOutlining (instr)) {
					// append a unique separator to prevent this from showing up in a common subsequence
					c = instr.GetHashCode ();
				} else {
					// encode instruction as an int
					c = instructionAsInt.Get (instr);

					// track mapping from character -> instruction
					if (!Context.InstructionMap.TryAdd (c, instr)) {
					}
				}

				// append encoded instruction
				Context.InstructionSequence.Add (c);

				Console.Write(c.ToString("X8"));
				Console.WriteLine(": " + instr.ToString ());
			}

			// append a unique terminator for each method body
			var terminator = method.GetHashCode ();
			Context.InstructionSequence.Add (terminator);

			Console.WriteLine(terminator.ToString ("X8") + " (terminator)");
			Console.WriteLine();
		}

		void ProcessMethod (MethodDefinition method)
		{
			if (!IsMethodEligibleForOutlining (method))
				return;

			// EncodeMethodForSuffixTree (method);

			TrackMethodHash (method);
		}

		int HashMethodBody (MethodDefinition method)
		{
			var hash = HashCode.Combine (method.Body.HasVariables, method.Body.Variables.Count);
			hash = HashCode.Combine (hash, method.HasParameters);
			if (method.HasParameters) {
				foreach (var p in method.Parameters) {
					// TODO: doesn't account for modreq, etc...
					hash = HashCode.Combine (hash, p.ParameterType);
				}
			}
			foreach (var instr in method.Body.Instructions) {
				// TODO: fix GetHashCode in instructionAsInt.
				hash = HashCode.Combine (hash, instructionAsInt.Get (instr).GetHashCode ());
			}

			hash = HashCode.Combine (hash, method.IsStatic);

			return hash;
		}

		void TrackMethodHash (MethodDefinition method)
		{
			var hash = HashMethodBody (method);
			if (!Context.IdenticalMethods.TryGetValue (hash, out List<MethodDefinition> methods)) {
				methods = new List<MethodDefinition> ();
				Context.IdenticalMethods [hash] = methods;
			}
			methods.Add (method);
		}

		protected override void EndProcess() {
			// build the suffix array
//			Context.SuffixArray = new SuffixArray.SuffixArray<int>(Context.InstructionSequence);
		}
	}
}
