// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using ILLink.Shared;
using ILLink.Shared.TrimAnalysis;
using Microsoft.CodeAnalysis;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
	public record SymbolValue : ValueWithDynamicallyAccessedMembers
	{
		public readonly ISymbol Source;
		public readonly bool IsMethodReturn;

		public readonly IPropertySymbol? Property;

		// Ideally this would just be the same as the method case, called for a property setter.
		// However, there doesn't appear to be a way to get the property symbol from the get method,
		// so we must save the property along with it.
		public SymbolValue (IMethodSymbol getMethod, IPropertySymbol property)
		{
			// Should only be created to represent property getters.
			// For setters, the return value doesn't matter.
			Debug.Assert (property.GetMethod == getMethod);
			Source = getMethod;
			Property = property;
			IsMethodReturn = true;

			// TODO: what about 'return' inside a getter body? Should this also
			// get its annotation from the get method or the property?
			// This ctor is currently only called for calls to the getter.

			// TODO: what about the receiver? Need to check the property... :/
		}

		// Similarly for property setters
		public SymbolValue (IParameterSymbol setterParameter, IPropertySymbol property)
		{
			Debug.Assert (setterParameter.ContainingSymbol is IMethodSymbol setMethod
				&& setMethod.MethodKind == MethodKind.PropertySet
				&& property.SetMethod == setMethod);
			Source = setterParameter;
			Property = property;

			// TODO: inside the property body, should the "value" get its annotation both from
			// the setter and the property?
			// This ctor is currently only called for calls to the setter.
		}

		public SymbolValue (IMethodSymbol method, bool isMethodReturn) => (Source, IsMethodReturn) = (method, isMethodReturn);

		public SymbolValue (IParameterSymbol parameter) => Source = parameter;

		public SymbolValue (IFieldSymbol field) => Source = field;

		public SymbolValue (INamedTypeSymbol type) => Source = type;

		// This ctor isn't used for dataflow - it's really just a wrapper
		// for annotations on type arguments/parameters which are type-checked
		// by the analyzer (outside of the dataflow analysis).
		public SymbolValue (ITypeSymbol typeArgument) => Source = typeArgument;

		public SymbolValue (ITypeParameterSymbol typeParameter) => Source = typeParameter;

		public override DynamicallyAccessedMemberTypes DynamicallyAccessedMemberTypes {
			get {
				if (!IsMethodReturn) {
					var damt = Source.GetDynamicallyAccessedMemberTypes ();
					// TODO: how to handle conflicts between the getter and the property annotation?
					if (Property != null && damt == DynamicallyAccessedMemberTypes.None)
						damt = Property.GetDynamicallyAccessedMemberTypes ();

					return damt;
				}

				var returnDamt = ((IMethodSymbol) Source).GetDynamicallyAccessedMemberTypesOnReturnType ();
				// TODO: how to handle conflicts between the getter and the property annotation?
				if (Property != null && returnDamt == DynamicallyAccessedMemberTypes.None)
					returnDamt = Property.GetDynamicallyAccessedMemberTypes ();

				return returnDamt;
			}
		}

		public override string ToString ()
		{
			StringBuilder sb = new ();
			switch (Source) {
			case IMethodSymbol method:
				if (IsMethodReturn)
					sb.Append (method.Name);
				else
					sb.Append ("'this'");
				break;
			case IParameterSymbol param:
				sb.Append (param.Name);
				break;
			case IFieldSymbol field:
				sb.Append (field.Name);
				break;
			case INamedTypeSymbol:
				sb.Append ("type 'this'");
				break;
			default:
				throw new NotImplementedException (Source.GetType ().ToString ());
			}
			var damtStr = Annotations.GetMemberTypesString (DynamicallyAccessedMemberTypes);
			var memberTypesStr = damtStr.Split ('.')[1].TrimEnd ('\'');
			sb.Append ("[").Append (memberTypesStr).Append ("]");
			return sb.ToString ();
		}
	}
}