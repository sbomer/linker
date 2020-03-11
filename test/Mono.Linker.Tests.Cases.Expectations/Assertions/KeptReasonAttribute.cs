using System;
using Mono.Linker;

namespace Mono.Linker.Tests.Cases.Expectations.Assertions
{

	// [AttributeUsage (AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	// public KeptReasonAttribute : BaseExpectedLinkedBehaviorAttribute {
	// 	// method -> method
	// 	public KeptReasonAttribute (MarkReasonKind kind, Type methodType, string methodName, 
	// }

	public abstract class KeptReasonAttribute : BaseExpectedLinkedBehaviorAttribute {
	}

	[AttributeUsage (AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public class KeptMethodReasonAttribute : KeptReasonAttribute {
		public KeptMethodReasonAttribute (DependencyKind kind, Type methodType, string methodName) {
			switch (kind) {
			case DependencyKind.DirectCall:
				// ok
				break;
			case DependencyKind.VirtualCall:
				// ok
				break;
			default:
				throw new ArgumentException ("invalid kept reason kind for method: " + nameof (kind));
			}

			if (methodType == null)
				throw new ArgumentNullException (nameof (methodType));
			if (string.IsNullOrEmpty (methodName))
				throw new ArgumentException ("Value cannot be null or empty.", nameof (methodName));
		}

		public KeptMethodReasonAttribute (DependencyKind kind) {
			switch (kind) {
			case DependencyKind.EntryMethod:
				// ok
				break;
			default:
				throw new ArgumentException ("invalid kept reason kind for method: " + nameof (kind));
			}
		}

		public KeptMethodReasonAttribute (DependencyKind kind, Type type) {
//			if (kind != MarkReasonKind.TypeCctor)
//				throw new ArgumentException ("this ctor only usable with typecctor dependency.");
			if (type == null)
				throw new ArgumentNullException (nameof (type));
		}
	}
}
