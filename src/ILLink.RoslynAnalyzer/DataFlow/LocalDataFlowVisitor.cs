// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using ILLink.Shared;
using ILLink.Shared.DataFlow;
using ILLink.Shared.TypeSystemProxy;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ILLink.RoslynAnalyzer.DataFlow
{
	// TODO: See if we can simplify DataFlowContext

	public readonly struct MethodKey : IEquatable<MethodKey>
	{
		public readonly IMethodSymbol? LocalFunction;
		public readonly IFlowAnonymousFunctionOperation? Lambda;

		public MethodKey (IMethodSymbol localFunction) => (LocalFunction, Lambda) = (localFunction, null);

		public MethodKey (IFlowAnonymousFunctionOperation lambda) => (LocalFunction, Lambda) = (null, lambda);

		public bool Equals (MethodKey other) => SymbolEqualityComparer.Default.Equals (LocalFunction, other.LocalFunction) &&
			(Lambda?.Equals (other.Lambda) ?? other.Lambda == null);
	}

	// Visitor which tracks the values of locals in a block. It provides extension points that get called
	// whenever a value that comes from a tracked local reference flows into one of the following:
	// - field
	// - parameter
	// - method return
	internal abstract class LocalDataFlowVisitor<TValue, TValueLattice> : OperationWalker<DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey>, TValue>,
		ITransfer<BlockProxy, LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey>
		// This struct constraint prevents warnings due to possible null returns from the visitor methods.
		// Note that this assumes that default(TValue) is equal to the TopValue.
		where TValue : struct, IEquatable<TValue>
		where TValueLattice : ILattice<TValue>
	{
		protected readonly LocalStateLattice<TValue, TValueLattice> LocalStateLattice;

		protected readonly OperationBlockAnalysisContext Context;

		protected TValue TopValue => LocalStateLattice.Lattice.ValueLattice.Top;

		public LocalDataFlowVisitor (LocalStateLattice<TValue, TValueLattice> lattice, OperationBlockAnalysisContext context) =>
			(LocalStateLattice, Context) = (lattice, context);

		public void Transfer (BlockProxy block, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			foreach (IOperation operation in block.Block.Operations)
				Visit (operation, context);

			// Blocks may end with a BranchValue computation. Visit the BranchValue operation after all others.
			IOperation? branchValueOperation = block.Block.BranchValue;
			if (branchValueOperation == null)
				return;

			var branchValue = Visit (branchValueOperation, context);

			// BranchValue may represent a value used in a conditional branch to the ConditionalSuccessor - if so, we are done.
			if (block.Block.ConditionKind != ControlFlowConditionKind.None)
				return;

			// If not, the BranchValue represents a return or throw value associated with the FallThroughSuccessor of this block.
			// (ConditionalSuccessor == null iff ConditionKind == None).

			// The BranchValue for a thrown value is not involved in dataflow tracking.
			if (block.Block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw)
				return;

			// Return statements with return values are represented in the control flow graph as
			// a branch value operation that computes the return value.

			// Use the branch value operation as the key for the warning store and the location of the warning.
			// We don't want the return operation because this might have multiple possible return values in general.
			HandleReturnValue (branchValue, branchValueOperation);
		}

		public abstract void HandleAssignment (TValue source, TValue target, IOperation operation);

		public abstract TValue HandleArrayElementRead (TValue arrayValue, TValue indexValue, IOperation operation);

		public abstract void HandleArrayElementWrite (TValue arrayValue, TValue indexValue, TValue valueToWrite, IOperation operation);

		// This takes an IOperation rather than an IReturnOperation because the return value
		// may (must?) come from BranchValue of an operation whose FallThroughSuccessor is the exit block.
		public abstract void HandleReturnValue (TValue returnValue, IOperation operation);

		// This is called for any method call, which includes:
		// - Normal invocation operation
		// - Accessing property value - which is treated as a call to the getter
		// - Setting a property value - which is treated as a call to the setter
		// All inputs are already visited and turned into values.
		// The return value should be a value representing the return value from the called method.
		public abstract TValue HandleMethodCall (IMethodSymbol calledMethod, TValue instance, ImmutableArray<TValue> arguments, IOperation operation);

		public override TValue VisitLocalReference (ILocalReferenceOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			return context.State.Get (new LocalKey (operation.Local));
		}

		public override TValue VisitSimpleAssignment (ISimpleAssignmentOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			var targetValue = Visit (operation.Target, context);
			var value = Visit (operation.Value, context);
			switch (operation.Target) {
			case ILocalReferenceOperation localRef:
				context.State.Set (new LocalKey (localRef.Local), value);
				break;
			case IFieldReferenceOperation:
			case IParameterReferenceOperation:
				// Extension point for assignments to "interesting" targets.
				// Doesn't get called for assignments to locals, which are handled above.
				HandleAssignment (value, targetValue, operation);
				break;
			case IPropertyReferenceOperation propertyRef:
				// A property assignment is really a call to the property setter.
				var setMethod = propertyRef.Property.SetMethod!;
				TValue instanceValue = Visit (propertyRef.Instance, context);
				// The return value of a property set expression is the value,
				// even though a property setter has no return value.
				HandleMethodCall (
					setMethod,
					instanceValue,
					ImmutableArray.Create (value),
					operation);
				break;
			// TODO: when setting a property in an attribute, target is an IPropertyReference.
			case IArrayElementReferenceOperation arrayElementRef:
				if (arrayElementRef.Indices.Length != 1)
					break;

				HandleArrayElementWrite (Visit (arrayElementRef.ArrayReference, state), Visit (arrayElementRef.Indices[0], state), value, operation);
				break;
			case IDiscardOperation:
				// Assignments like "_ = SomeMethod();" don't need dataflow tracking.
				break;
			case IInvalidOperation:
				// This can happen for a field assignment in an attribute instance.
				// TODO: validate against the field attributes.
				break;
			default:
				throw new NotImplementedException (operation.Target.GetType ().ToString ());
			}
			return value;
		}

		// Similar to VisitLocalReference
		public override TValue VisitFlowCaptureReference (IFlowCaptureReferenceOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			return context.State.Get (new LocalKey (operation.Id));
		}

		// Similar to VisitSimpleAssignment when assigning to a local, but for values which are captured without a
		// corresponding local variable. The "flow capture" is like a local assignment, and the "flow capture reference"
		// is like a local reference.
		public override TValue VisitFlowCapture (IFlowCaptureOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			TValue value = Visit (operation.Value, context);
			context.State.Set (new LocalKey (operation.Id), value);
			return value;
		}

		public override TValue VisitDelegateCreation (IDelegateCreationOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			if (operation.Target is not IFlowAnonymousFunctionOperation lambdaOperation)
				throw new NotImplementedException ("Delegate creation from anything but anonymous function is not supported.");

			var state = context.State.Current;
			context.InterproceduralState.ProcessCall (new (lambdaOperation), ref state);
			context.State.Current = LocalStateLattice.Top;

			// Return value? Top because we don't track the lambda.
			return TopValue;
		}

// 		public override TValue VisitExpressionStatement (IExpressionStatementOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
// 		{
// 			Visit (operation.Operation, context);
// 			return TopValue;
// 		}

		public override TValue VisitInvocation (IInvocationOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
			=> ProcessMethodCall (operation, operation.TargetMethod, operation.Instance, operation.Arguments, context);

		public static IMethodSymbol GetPropertyMethod (IPropertyReferenceOperation operation)
		{
			// The IPropertyReferenceOperation doesn't tell us whether this reference is to the getter or setter.
			// For this we need to look at the containing operation.
			var parent = operation.Parent;
			if (parent?.Kind == OperationKind.SimpleAssignment) {
				var assignment = (ISimpleAssignmentOperation) parent;
				if (assignment.Target == operation) {
					var setMethod = operation.Property.SetMethod;
					Debug.Assert (setMethod != null);
					return setMethod!;
				}
				Debug.Assert (assignment.Value == operation);
			}

			var getMethod = operation.Property.GetMethod;
			Debug.Assert (getMethod != null);
			return getMethod!;
		}

		public override TValue VisitPropertyReference (IPropertyReferenceOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			if (operation.GetValueUsageInfo (Context.OwningSymbol).HasFlag (ValueUsageInfo.Read)) {
				// Accessing property for reading is really a call to the getter
				// The setter case is handled in assignment operation since here we don't have access to the value to pass to the setter
				TValue instanceValue = Visit (operation.Instance, context);
				return HandleMethodCall (
					operation.Property.GetMethod!,
					instanceValue,
					ImmutableArray<TValue>.Empty,
					operation);
			}

			return TopValue;
		}

		public override TValue VisitArrayElementReference (IArrayElementReferenceOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			if (operation.GetValueUsageInfo (Context.OwningSymbol).HasFlag (ValueUsageInfo.Read)) {
				// Accessing an array element for reading is a call to the indexer
				// or a plain array access. Just handle plain array access for now.

				// Only handle simple index access
				if (operation.Indices.Length != 1)
					return TopValue;

				return HandleArrayElementRead (Visit (operation.ArrayReference, state), Visit (operation.Indices[0], state), operation);
			}

			return TopValue;
		}

		public override TValue VisitArgument (IArgumentOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			return Visit (operation.Value, context);
		}

		public override TValue VisitReturn (IReturnOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			if (operation.ReturnedValue != null) {
				var value = Visit (operation.ReturnedValue, context);
				HandleReturnValue (value, operation);
				return value;
			}

			return TopValue;
		}

		public override TValue VisitConversion (IConversionOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			var operandValue = Visit (operation.Operand, context);
			return operation.OperatorMethod == null ? operandValue : TopValue;
		}

		public override TValue VisitObjectCreation (IObjectCreationOperation operation, DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			if (operation.Constructor == null)
				return TopValue;

			return ProcessMethodCall (operation, operation.Constructor, null, operation.Arguments, context);
		}

		TValue ProcessMethodCall (
			IOperation operation,
			IMethodSymbol method,
			IOperation? instance,
			ImmutableArray<IArgumentOperation> arguments,
			DataFlowContext<LocalState<TValue>, LocalDataFlowState<TValue, TValueLattice>, LocalStateLattice<TValue, TValueLattice>, MethodKey> context)
		{
			TValue instanceValue = Visit (instance, context);

			var argumentsBuilder = ImmutableArray.CreateBuilder<TValue> ();
			foreach (var argument in arguments) {
				// For __arglist argument there might not be any parameter
				// __arglist is only legal as the last argument to a method and there's also no supported
				// way for it to carry annotations or participate in data flow in any way, so it's OK to ignore it.
				// Since it's always last, it's also OK to simply pass a shorter arguments array to the call.
				if (argument?.Parameter == null)
					break;

				argumentsBuilder.Add (VisitArgument (argument, context));
			}

			if (CompilerGeneratedCode.IsLocalFunction (new (method))) {
				// Track flow into the local function from captured variables.
				var state = context.State.Current;
				context.InterproceduralState.ProcessCall (new (method), ref state);
				// Ignore flow out for now.
				// TODO: this zeros out all tracked locals. Only zero out the relevant ones
				// (ones captured by local functions, or better, by this particular local function).
				// context.State.Current = new LocalState<TValue> (new DefaultValueDictionary<LocalKey, TValue>)
				context.State.Current = LocalStateLattice.Top;
				// LocalStateLattice.Top;

				// Let the below handle the method return value.
			}

			return HandleMethodCall (
				method,
				instanceValue,
				argumentsBuilder.ToImmutableArray (),
				operation);
		}
	}
}