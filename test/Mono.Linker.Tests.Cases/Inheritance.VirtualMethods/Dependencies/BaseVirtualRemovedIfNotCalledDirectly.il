// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

.assembly extern mscorlib { }

.assembly 'library' { }

.class public auto ansi beforefieldinit Derived
	extends Base
{
	.method public hidebysig virtual 
		instance void M () cil managed 
	{
		ret
	}

	.method public hidebysig specialname rtspecialname 
		instance void .ctor () cil managed 
	{
		ldarg.0
		call instance void Base::.ctor()
		ret
	}
}

.class public auto ansi beforefieldinit Base
	extends [mscorlib]System.Object
{
	.method public hidebysig newslot virtual 
		instance void M () cil managed 
	{
		ret
	}

	.method public hidebysig specialname rtspecialname 
		instance void .ctor () cil managed 
	{
		ldarg.0
		call instance void [mscorlib]System.Object::.ctor()
		ret
	}
}

.class public auto ansi Helper
	extends [mscorlib]System.Object
{
	.method public hidebysig specialname rtspecialname
	instance void .ctor () cil managed
	{
		ret
	}

	.method public hidebysig static
		void CallDerivedMethodDirectly () cil managed
	{
		newobj instance void Derived::.ctor()
		callvirt instance void Derived::M()
		ret
	}
}
