// https://github.com/lpereira/ms-hackathon-2020/blob/master/Program.cs

using System;
using System.Collections.Generic;
using System.Text;
//using Gma.DataStructures.StringSearch;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mono.Linker {
	class InstrString {
		public Instruction instr;
		public string str;

		public InstrString (Instruction i) {
			str = InstrToString (i);
			instr = i;
		}

		public override int GetHashCode () {
			return HashCode.Combine (instr.OpCode, instr.Operand);
		}

		public override bool Equals (Object other) {
			if (this == other)
				return true;
			if (!(other is InstrString instrStr))
				return false;

			if (instr.OpCode != instrStr.instr.OpCode)
				return false;

			// TODO: this uses reference equality for the operand.
			if (instr.Operand != instr.Operand)
				return false;

			return true;
		}

		private static string InstrToString (Instruction i) {
			// Copy-paste from Cecil
			var instruction = new StringBuilder ();

			instruction.Append (i.OpCode.Name);

			if (i.Operand == null)
				return instruction.ToString ();

			instruction.Append (' ');

			switch (i.OpCode.OperandType) {
				case OperandType.ShortInlineBrTarget:
				case OperandType.InlineBrTarget:
					Instruction op = (Instruction) i.Operand;
					instruction.Append (InstrToString (op));
					break;
				case OperandType.InlineSwitch:
					var labels = (Instruction[]) i.Operand;
					for (int l = 0; l < labels.Length; l++) {
						if (l > 0)
							instruction.Append (',');

						instruction.Append (InstrToString (labels[l]));
					}
					break;
				case OperandType.InlineString:
					instruction.Append ('\"');
					instruction.Append (i.Operand);
					instruction.Append ('\"');
					break;
				default:
					instruction.Append (i.Operand);
					break;
			}

			return instruction.ToString ();
		}
	}

	class InstructionAsInt {
		private Dictionary<InstrString, int> instrToChar = new Dictionary<InstrString, int> ();
		private int lastChar = 0;

		public int Get (Instruction i) {
			var instrString = new InstrString (i);

			if (!instrToChar.ContainsKey (instrString)) {
				int oldChar = lastChar;
				instrToChar.Add (instrString, oldChar);
				lastChar++;

//				Console.WriteLine ("      Unique " + (int) oldChar + " -> " + instrString.str);

				return oldChar;
			}

			return instrToChar[instrString];
		}

		public int GetUniqueInstructions () {
			return lastChar;
		}
	}

	// class Program {
	// 	static void Main (string[] args) {
	// 		var fileToOpen = args.Length == 1 ? args[0] : "/usr/local/share/dotnet/shared/Microsoft.NETCore.App/3.1.6/System.Private.CoreLib.dll";
// 
	// 		var suffixTree = new UkkonenTrie<MethodDefinition> ();
	// 		ModuleDefinition module = ModuleDefinition.ReadModule (fileToOpen);
	// 		InstructionAsChar instrAsChar = new InstructionAsChar ();
	// 		int allInstructions = 0;
// 
	// 		Console.WriteLine ("Building suffix tree");
	// 		foreach (TypeDefinition type in module.Types) {
	// 			Console.WriteLine ("  Current type: " + type.Name);
	// 			foreach (MethodDefinition meth in type.Methods) {
	// 				if (!meth.IsIL || meth.IsNative || !meth.IsManaged)
	// 					continue;
	// 				if (!meth.HasBody)
	// 					continue;
// 
	// 				Console.WriteLine ("    Method: " + meth.Name);
// 
	// 				var builder = new StringBuilder ();
	// 				foreach (Instruction instr in meth.Body.Instructions) {
	// 					builder.Append (instrAsChar.Get (instr));
	// 				}
	// 				allInstructions += meth.Body.Instructions.Count;
	// 				suffixTree.Add (builder.ToString (), meth);
	// 			}
	// 		}
// 
	// 		Console.WriteLine ("Unique instructions: " + instrAsChar.GetUniqueInstructions ());
	// 		Console.WriteLine ("Total instructions: " + allInstructions);
	// 	}
	// }
}