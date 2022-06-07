// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Reflection;

namespace System
{
	public struct TestStructTypeIReflect : IReflect
	{
        public Type UnderlyingSystemType => throw new NotImplementedException ();

        public FieldInfo GetField(string name, BindingFlags bindingFlags) => throw new NotImplementedException ();

        public FieldInfo[] GetFields(BindingFlags bindingFlags) => throw new NotImplementedException ();

        public MemberInfo[] GetMember(string name, BindingFlags bindingFlags) => throw new NotImplementedException ();

        public MemberInfo[] GetMembers(BindingFlags bindingFlags) => throw new NotImplementedException ();

        public MethodInfo GetMethod(string name, BindingFlags bindingFlags) => throw new NotImplementedException ();

        public MethodInfo GetMethod(string name, BindingFlags bindingFlags, Binder binder, Type[] types, ParameterModifier[] modifiers) => throw new NotImplementedException ();

        public MethodInfo[] GetMethods(BindingFlags bindingFlags) => throw new NotImplementedException ();

        public PropertyInfo[] GetProperties(string name, BindingFlags bindingFlags) => throw new NotImplementedException ();

        public PropertyInfo[] GetProperties(BindingFlags bindingFlags) => throw new NotImplementedException ();

        public PropertyInfo GetProperty(string name, BindingFlags bindingFlags) => throw new NotImplementedException ();

        public PropertyInfo GetProperty(string name, BindingFlags bindingFlags, Binder binder, Type type, Type[] types, ParameterModifier[] modifiers) => throw new NotImplementedException ();

        public object? InvokeMember(string name, BindingFlags bindingFlags, Binder binder, Object instance, Object[] arguments, ParameterModifier[] modifiers, CultureInfo culture, string[] namedParameters) => throw new NotImplementedException ();
    }
}
