using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces
{
	public class InterfaceImplementationCases
	{
		public static void Main ()
		{
			(new OnTypeExplicit () as I).M ();
			(new OnTypeExplicitWithVirtual () as I).M ();
			(new OnTypeNonVirtual () as I).M ();
			(new OnTypeVirtual () as I).M ();
			(new OnTypeFromBaseNonVirtual () as I).M();
			(new OnTypeFromBaseVirtual () as I).M();
			(new OnTypeOverride () as I).M();
			(new OnBaseExplict () as I).M();
			(new OnBaseNonVirtual () as I).M();
			(new OnBaseVirtual () as I).M();
			(new OnBaseVirtualOverride () as I).M();
			(new OnTypeDefault () as IDefault).M();
			(new OnTypeRequiresDefault () as IDefault).M();
			(new OnBaseDefault () as IDefault).M();
			(new OnBaseRequiresDefault () as IDefault).M();
			(new OnTypeProvidesDefault () as I).M();
			(new OnBaseProvidesDefault () as I).M();
		}

		[Kept]
		interface I
		{
			[Kept]
			void M ();
		}

		[Kept]
		[KeptMember (".ctor()")]
		class BaseForOnTypeExplicit
		{
			// SHOULD BE REMOVED
			[Kept]
			public virtual void M() { }
		}

		// removed
		interface IProvidesDefaultForOnTypeExplicit : I
		{
			void I.M() { }

			new void M();
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseForOnTypeExplicit))]
		class OnTypeExplicit : BaseForOnTypeExplicit, IProvidesDefaultForOnTypeExplicit, I
		{
			[Kept]
			void I.M () { }

			// SHOULD BE REMOVED
			// It is only kept because of the new void M();
			// on IProvidesDefaultForOnTypeExplicit.
			[Kept]
			public new void M() { }
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		class OnTypeExplicitWithVirtual : I
		{
			[Kept]
			void I.M () { }

			// SHOULD BE REMOVED
			[Kept]
			public virtual void M() { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		class BaseForOnTypeNonVirtual {
			public void M () { }
		}

		// removed
		interface IProvidesDefaultForOnTypeNonVirtual : I {
			void I.M() { }

			new void M();
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseForOnTypeNonVirtual))]
		class OnTypeNonVirtual : BaseForOnTypeNonVirtual, IProvidesDefaultForOnTypeNonVirtual, I
		{
			[Kept]
			public new void M () { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		class BaseForOnTypeVirtual {
			// SHOULD BE REMOVED
			[Kept]
			public virtual void M () { }
		}

		// removed
		interface IProvidesDefaultForOnTypeVirtual : I {
			void I.M() { }

			new void M();
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseForOnTypeVirtual))]
		class OnTypeVirtual : BaseForOnTypeVirtual, IProvidesDefaultForOnTypeVirtual, I
		{
			[Kept]
			public new virtual void M () { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		class BaseNonVirtual
		{
			[Kept]
			public void M () { } // virtual final
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseNonVirtual))]
		class OnTypeFromBaseNonVirtual : BaseNonVirtual, I { }

		[Kept]
		[KeptMember (".ctor()")]
		class BaseVirtual
		{
			[Kept]
			public virtual void M () { }
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseVirtual))]
		class OnTypeFromBaseVirtual : BaseVirtual, I { }

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseVirtual))]
		class OnTypeOverride : BaseVirtual, I
		{
			[Kept]
			public override void M () { }
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		class BaseExplicitImpl : I
		{
			[Kept]
			void I.M () { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseExplicitImpl))]
		class OnBaseExplict : BaseExplicitImpl {
			// removed
			public virtual void M () { }
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		class BaseNonVirtualImpl : I
		{
			[Kept]
			public void M () { } // virtual final
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseNonVirtualImpl))]
		class OnBaseNonVirtual : BaseNonVirtualImpl {
			// SHOULD BE REMOVED
			[Kept]
			public new virtual void M () { }
		}

		[Kept]
		[KeptInterface (typeof (I))]
		[KeptMember (".ctor()")]
		class BaseVirtualImpl : I
		{
			[Kept]
			public virtual void M () { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseVirtualImpl))]
		class OnBaseVirtual : BaseVirtualImpl { 
			// removed
			public new void M () { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseVirtualImpl))]
		class OnBaseVirtualOverride : BaseVirtualImpl
		{
			[Kept]
			public override void M () { }
		}

		[Kept]
		interface IDefault
		{
			[Kept]
			void M () { }
		}

		// removed
		interface IRequiresDefault : IDefault {
			new void M ();
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptInterface (typeof (IDefault))]
		class OnTypeDefault : IDefault {
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptInterface (typeof (IDefault))]
		class OnTypeRequiresDefault : IRequiresDefault {
			// removed
			void IRequiresDefault.M() { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptInterface (typeof (IDefault))]
		class BaseDefault : IDefault { }

		[Kept]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseDefault))]
		class OnBaseDefault : BaseDefault {
			// removed. shouldn't this be kept!?
			// no it shouldn't - because the interface method call will resolve to the default impl.
			// this method is not part of an interface implementation.
			// but a similar scenario where a type implements an interface which brings in a default
			// method, but the type doesn't implement the interface, the runtime will resolve
			// the interface method to this type. can I write a test for that case?
			// I want to make sure the type doesn't itself implement the interface.
			public virtual void M() { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptInterface (typeof (IDefault))]
		class BaseRequiresDefault : IRequiresDefault {
			void IRequiresDefault.M() { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseRequiresDefault))]
		class OnBaseRequiresDefault : BaseRequiresDefault {
			// removed
			public virtual void M() { }
		}

		[Kept]
		[KeptInterface (typeof (I))]
		interface IProvidesDefaultOnType : I
		{
			[Kept]
			void I.M () { }

			// SHOULD BE REMOVED
			[Kept]
			new void M () { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptInterface (typeof (IProvidesDefaultOnType))]
		[KeptInterface (typeof (I))]
		class OnTypeProvidesDefault : IProvidesDefaultOnType {
		}

		[Kept]
		[KeptInterface (typeof (I))]
		interface IProvidesDefaultOnBase : I
		{
			[Kept]
			void I.M () { }

			// SHOULD BE REMOVED
			[Kept]
			new void M () { }
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptInterface (typeof (IProvidesDefaultOnBase))]
		[KeptInterface (typeof (I))]
		class BaseProvidesDefault : IProvidesDefaultOnBase { }

		[Kept]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (BaseProvidesDefault))]
		class OnBaseProvidesDefault : BaseProvidesDefault { }
	}
}
