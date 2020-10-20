// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

.assembly extern mscorlib { }

.assembly 'library' { }

.class interface public auto ansi abstract IFooDefault
{
	.method public hidebysig newslot virtual
		instance void Default () cil managed
	{
		ldstr "IFooDefault.Default"
		call void [System.Console]System.Console::WriteLine(string)
		ret
	}
}

.class interface public auto ansi abstract IFoo
	implements IFooDefault
{
	.method public hidebysig newslot abstract virtual 
		instance void Method () cil managed 
	{
	}
}

.class public auto ansi Foo
	extends [mscorlib]System.Object
	implements IFoo
{
	.method public hidebysig specialname rtspecialname
	instance void .ctor () cil managed
	{
		ret
	}

	.method public hidebysig newslot virtual
		instance void Method () cil managed
	{
		ret
	}

	.method public hidebysig newslot virtual
		instance void Default () cil managed
	{
		ldstr "Foo.Default"
		call void [System.Console]System.Console::WriteLine(string)
		ret
	}
}
