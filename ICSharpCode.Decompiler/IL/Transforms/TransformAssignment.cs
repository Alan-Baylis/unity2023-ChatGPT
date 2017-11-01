﻿// Copyright (c) 2015 Siegfried Pammer
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Diagnostics;
using System.Linq;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.IL.Transforms
{
	/// <summary>
	/// Constructs compound assignments and inline assignments.
	/// </summary>
	public class TransformAssignment : IStatementTransform
	{
		StatementTransformContext context;
		
		void IStatementTransform.Run(Block block, int pos, StatementTransformContext context)
		{
			this.context = context;
			/*if (TransformPostIncDecOperatorOnAddress(block, i) || TransformPostIncDecOnStaticField(block, i) || TransformCSharp4PostIncDecOperatorOnAddress(block, i)) {
				block.Instructions.RemoveAt(i);
				continue;
			}
			if (TransformPostIncDecOperator(block, i)) {
				block.Instructions.RemoveAt(i);
				continue;
			}*/
			if (TransformInlineAssignmentStObjOrCall(block, pos) || TransformInlineAssignmentLocal(block, pos)) {
				// both inline assignments create a top-level stloc which might affect inlining
				context.RequestRerun();
				return;
			}
			/*
			TransformInlineCompoundAssignmentCall(block, pos);
			TransformRoslynCompoundAssignmentCall(block, pos);
			// TODO: post-increment on local
			// post-increment on address (e.g. field or array element)
			TransformPostIncDecOperatorOnAddress(block, pos);
			TransformRoslynPostIncDecOperatorOnAddress(block, pos);
			// TODO: post-increment on call
			*/
		}

		/// <code>
		/// stloc s(value)
		/// stloc l(ldloc s)
		/// stobj(..., ldloc s)
		///   where ... is pure and does not use s or l
		/// -->
		/// stloc l(stobj (..., value))
		/// </code>
		/// e.g. used for inline assignment to instance field
		/// 
		/// -or-
		/// 
		/// <code>
		/// stloc s(value)
		/// stobj (..., ldloc s)
		/// -->
		/// stloc s(stobj (..., value))
		/// </code>
		/// e.g. used for inline assignment to static field
		/// 
		/// -or-
		/// 
		/// <code>
		/// stloc s(value)
		/// call set_Property(..., ldloc s)
		///   where the '...' arguments are free of side-effects and not using 's'
		/// -->
		/// stloc s(Block InlineAssign { call set_Property(..., stloc i(value)); final: ldloc i })
		/// </code>
		bool TransformInlineAssignmentStObjOrCall(Block block, int pos)
		{
			var inst = block.Instructions[pos] as StLoc;
			// in some cases it can be a compiler-generated local
			if (inst == null || (inst.Variable.Kind != VariableKind.StackSlot && inst.Variable.Kind != VariableKind.Local))
				return false;
			if (IsImplicitTruncation(inst.Value, inst.Variable.Type)) {
				// 'stloc s' is implicitly truncating the value
				return false;
			}
			ILVariable local;
			int nextPos;
			if (block.Instructions[pos + 1] is StLoc localStore) { // with extra local
				if (localStore.Variable.Kind != VariableKind.Local || !localStore.Value.MatchLdLoc(inst.Variable))
					return false;
				if (!(inst.Variable.IsSingleDefinition && inst.Variable.LoadCount == 2))
					return false;
				local = localStore.Variable;
				nextPos = pos + 2;
			} else {
				local = inst.Variable;
				localStore = null;
				nextPos = pos + 1;
			}
			if (block.Instructions[nextPos] is StObj stobj) {
				if (!stobj.Value.MatchLdLoc(inst.Variable))
					return false;
				if (!SemanticHelper.IsPure(stobj.Target.Flags) || inst.Variable.IsUsedWithin(stobj.Target))
					return false;
				if (IsImplicitTruncation(inst.Value, stobj.Type)) {
					// 'stobj' is implicitly truncating the value
					return false;
				}
				context.Step("Inline assignment stobj", stobj);
				block.Instructions.Remove(localStore);
				block.Instructions.Remove(stobj);
				stobj.Value = inst.Value;
				inst.ReplaceWith(new StLoc(local, stobj));
				return true;
			} else if (block.Instructions[nextPos] is CallInstruction call) {
				// call must be a setter call:
				if (!(call.OpCode == OpCode.Call || call.OpCode == OpCode.CallVirt))
					return false;
				if (call.ResultType != StackType.Void || call.Arguments.Count == 0)
					return false;
				if (!call.Method.Equals((call.Method.AccessorOwner as IProperty)?.Setter))
					return false;
				if (!call.Arguments.Last().MatchLdLoc(inst.Variable))
					return false;
				foreach (var arg in call.Arguments.SkipLast(1)) {
					if (!SemanticHelper.IsPure(arg.Flags) || inst.Variable.IsUsedWithin(arg))
						return false;
				}
				if (IsImplicitTruncation(inst.Value, call.Method.Parameters.Last().Type)) {
					// setter call is implicitly truncating the value
					return false;
				}
				// stloc s(Block InlineAssign { call set_Property(..., stloc i(value)); final: ldloc i })
				context.Step("Inline assignment call", call);
				block.Instructions.Remove(localStore);
				block.Instructions.Remove(call);
				var newVar = context.Function.RegisterVariable(VariableKind.StackSlot, call.Method.Parameters.Last().Type);
				call.Arguments[call.Arguments.Count - 1] = new StLoc(newVar, inst.Value);
				inst.ReplaceWith(new StLoc(local, new Block(BlockType.CallInlineAssign) {
					Instructions = { call },
					FinalInstruction = new LdLoc(newVar)
				}));
				return true;
			} else {
				return false;
			}
		}
		
		static ILInstruction UnwrapSmallIntegerConv(ILInstruction inst, out Conv conv)
		{
			conv = inst as Conv;
			if (conv != null && conv.Kind == ConversionKind.Truncate && conv.TargetType.IsSmallIntegerType()) {
				// for compound assignments to small integers, the compiler emits a "conv" instruction
				return conv.Argument;
			} else {
				return inst;
			}
		}

		static bool ValidateCompoundAssign(BinaryNumericInstruction binary, Conv conv, IType targetType)
		{
			if (!CompoundAssignmentInstruction.IsBinaryCompatibleWithType(binary, targetType))
				return false;
			if (conv != null && !(conv.TargetType == targetType.ToPrimitiveType() && conv.CheckForOverflow == binary.CheckForOverflow))
				return false; // conv does not match binary operation
			return true;
		}

		/// <code>
		/// stloc s(binary(callvirt(getter), value))
		/// callvirt (setter, ldloc s)
		/// (followed by single usage of s in next instruction)
		/// -->
		/// stloc s(compound.op.new(callvirt(getter), value))
		/// </code>
		bool TransformInlineCompoundAssignmentCall(Block block, int i)
		{
			var mainStLoc = block.Instructions[i] as StLoc;
			// in some cases it can be a compiler-generated local
			if (mainStLoc == null || (mainStLoc.Variable.Kind != VariableKind.StackSlot && mainStLoc.Variable.Kind != VariableKind.Local))
				return false;
			BinaryNumericInstruction binary = UnwrapSmallIntegerConv(mainStLoc.Value, out var conv) as BinaryNumericInstruction;
			ILVariable localVariable = mainStLoc.Variable;
			if (!localVariable.IsSingleDefinition)
				return false;
			if (localVariable.LoadCount != 2)
				return false;
			var getterCall = binary?.Left as CallInstruction;
			var setterCall = block.Instructions.ElementAtOrDefault(i + 1) as CallInstruction;
			if (!MatchingGetterAndSetterCalls(getterCall, setterCall))
				return false;
			if (!setterCall.Arguments.Last().MatchLdLoc(localVariable))
				return false;
			
			var next = block.Instructions.ElementAtOrDefault(i + 2);
			if (next == null)
				return false;
			if (next.Descendants.Where(d => d.MatchLdLoc(localVariable)).Count() != 1)
				return false;
			IType targetType = getterCall.Method.ReturnType;
			if (!ValidateCompoundAssign(binary, conv, targetType))
				return false;
			context.Step($"Inline compound assignment to '{getterCall.Method.AccessorOwner.Name}'", setterCall);
			block.Instructions.RemoveAt(i + 1); // remove setter call
			mainStLoc.Value = new CompoundAssignmentInstruction(
				binary, getterCall, binary.Right,
				targetType, CompoundAssignmentType.EvaluatesToNewValue);
			return true;
		}

		/// <summary>
		/// Roslyn compound assignment that's not inline within another instruction.
		/// </summary>
		bool TransformRoslynCompoundAssignmentCall(Block block, int i)
		{
			// stloc variable(callvirt get_Property(ldloc obj))
			// callvirt set_Property(ldloc obj, binary.op(ldloc variable, ldc.i4 1))
			// => compound.op.new(callvirt get_Property(ldloc obj), ldc.i4 1)
			if (!(block.Instructions[i] is StLoc stloc))
				return false;
			if (!(stloc.Variable.IsSingleDefinition && stloc.Variable.LoadCount == 1))
				return false;
			var getterCall = stloc.Value as CallInstruction;
			var setterCall = block.Instructions[i + 1] as CallInstruction;
			if (!(MatchingGetterAndSetterCalls(getterCall, setterCall)))
				return false;
			var binary = setterCall.Arguments.Last() as BinaryNumericInstruction;
			if (binary == null || !binary.Left.MatchLdLoc(stloc.Variable))
				return false;
			if (!CompoundAssignmentInstruction.IsBinaryCompatibleWithType(binary, getterCall.Method.ReturnType))
				return false;
			context.Step($"Compound assignment to '{getterCall.Method.AccessorOwner.Name}'", setterCall);
			block.Instructions.RemoveAt(i + 1); // remove setter call
			stloc.ReplaceWith(new CompoundAssignmentInstruction(
				binary, getterCall, binary.Right,
				getterCall.Method.ReturnType, CompoundAssignmentType.EvaluatesToNewValue));
			return true;
		}

		static bool MatchingGetterAndSetterCalls(CallInstruction getterCall, CallInstruction setterCall)
		{
			if (getterCall == null || setterCall == null || !IsSameMember(getterCall.Method.AccessorOwner, setterCall.Method.AccessorOwner))
				return false;
			var owner = getterCall.Method.AccessorOwner as IProperty;
			if (owner == null || !IsSameMember(getterCall.Method, owner.Getter) || !IsSameMember(setterCall.Method, owner.Setter))
				return false;
			if (setterCall.Arguments.Count != getterCall.Arguments.Count + 1)
				return false;
			// Ensure that same arguments are passed to getterCall and setterCall:
			for (int j = 0; j < getterCall.Arguments.Count; j++) {
				if (!SemanticHelper.IsPure(getterCall.Arguments[j].Flags))
					return false;
				if (!getterCall.Arguments[j].Match(setterCall.Arguments[j]).Success)
					return false;
			}
			return true;
		}

		/// <summary>
		/// Transform compound assignments where the return value is not being used,
		/// or where there's an inlined assignment within the setter call.
		/// </summary>
		/// <remarks>
		/// Called by ExpressionTransforms.
		/// </remarks>
		internal static bool HandleCallCompoundAssign(CallInstruction setterCall, StatementTransformContext context)
		{
			// callvirt set_Property(ldloc S_1, binary.op(callvirt get_Property(ldloc S_1), value))
			// ==> compound.op.new(callvirt(callvirt get_Property(ldloc S_1)), value)
			var setterValue = setterCall.Arguments.LastOrDefault();
			var storeInSetter = setterValue as StLoc;
			if (storeInSetter != null) {
				// callvirt set_Property(ldloc S_1, stloc v(binary.op(callvirt get_Property(ldloc S_1), value)))
				// ==> stloc v(compound.op.new(callvirt(callvirt get_Property(ldloc S_1)), value))
				setterValue = storeInSetter.Value;
			}
			if (setterValue is Conv conv && conv.Kind == ConversionKind.Truncate && conv.TargetType.IsSmallIntegerType()) {
				// for compound assignments to small integers, the compiler emits a "conv" instruction
				setterValue = conv.Argument;
			} else {
				conv = null;
			}
			if (!(setterValue is BinaryNumericInstruction binary))
				return false;
			var getterCall = binary.Left as CallInstruction;
			if (!MatchingGetterAndSetterCalls(getterCall, setterCall))
				return false;
			IType targetType = getterCall.Method.ReturnType;
			if (!CompoundAssignmentInstruction.IsBinaryCompatibleWithType(binary, targetType))
				return false;
			if (conv != null && !(conv.TargetType == targetType.ToPrimitiveType() && conv.CheckForOverflow == binary.CheckForOverflow))
				return false; // conv does not match binary operation
			context.Step($"Compound assignment to '{getterCall.Method.AccessorOwner.Name}'", setterCall);
			ILInstruction newInst = new CompoundAssignmentInstruction(
				binary, getterCall, binary.Right,
				getterCall.Method.ReturnType, CompoundAssignmentType.EvaluatesToNewValue);
			if (storeInSetter != null) {
				storeInSetter.Value = newInst;
				newInst = storeInSetter;
				context.RequestRerun(); // moving stloc to top-level might trigger inlining
			}
			setterCall.ReplaceWith(newInst);
			return true;
		}

		/// <summary>
		/// stobj(target, binary.op(ldobj(target), ...))
		/// => compound.op(target, ...)
		/// </summary>
		/// <remarks>
		/// Called by ExpressionTransforms.
		/// </remarks>
		internal static bool HandleStObjCompoundAssign(StObj inst, ILTransformContext context)
		{
			if (!(UnwrapSmallIntegerConv(inst.Value, out var conv) is BinaryNumericInstruction binary))
				return false;
			if (!(binary.Left is LdObj ldobj))
				return false;
			if (!inst.Target.Match(ldobj.Target).Success)
				return false;
			if (!SemanticHelper.IsPure(ldobj.Target.Flags))
				return false;
			// ldobj.Type may just be 'int' (due to ldind.i4) when we're actually operating on a 'ref MyEnum'.
			// Try to determine the real type of the object we're modifying:
			IType targetType = ldobj.Target.InferType();
			if (targetType.Kind == TypeKind.Pointer || targetType.Kind == TypeKind.ByReference) {
				targetType = ((TypeWithElementType)targetType).ElementType;
				if (targetType.Kind == TypeKind.Unknown || targetType.GetSize() != ldobj.Type.GetSize()) {
					targetType = ldobj.Type;
				}
			} else {
				targetType = ldobj.Type;
			}
			if (!ValidateCompoundAssign(binary, conv, targetType))
				return false;
			context.Step("compound assignment", inst);
			inst.ReplaceWith(new CompoundAssignmentInstruction(
				binary, binary.Left, binary.Right,
				targetType, CompoundAssignmentType.EvaluatesToNewValue));
			return true;
		}

		/// <code>
		/// stloc s(value)
		/// stloc l(ldloc s)
		/// -->
		/// stloc s(stloc l(value))
		/// </code>
		bool TransformInlineAssignmentLocal(Block block, int pos)
		{
			var inst = block.Instructions[pos] as StLoc;
			var nextInst = block.Instructions.ElementAtOrDefault(pos + 1) as StLoc;
			if (inst == null || nextInst == null)
				return false;
			if (inst.Variable.Kind != VariableKind.StackSlot)
				return false;
			Debug.Assert(!inst.Variable.Type.IsSmallIntegerType());
			if (!(nextInst.Variable.Kind == VariableKind.Local || nextInst.Variable.Kind == VariableKind.Parameter))
				return false;
			if (!nextInst.Value.MatchLdLoc(inst.Variable))
				return false;
			if (IsImplicitTruncation(inst.Value, nextInst.Variable.Type)) {
				// 'stloc l' is implicitly truncating the stack value
				return false;
			}
			context.Step("Inline assignment to local variable", inst);
			var value = inst.Value;
			var var = nextInst.Variable;
			var stackVar = inst.Variable;
			block.Instructions.RemoveAt(pos);
			nextInst.ReplaceWith(new StLoc(stackVar, new StLoc(var, value)));
			return true;
		}

		/// <summary>
		/// Gets whether 'stobj type(..., value)' would evaluate to a different value than 'value'
		/// due to implicit truncation.
		/// </summary>
		bool IsImplicitTruncation(ILInstruction value, IType type)
		{
			if (!type.IsSmallIntegerType()) {
				// Implicit truncation in ILAst only happens for small integer types;
				// other types of implicit truncation in IL cause the ILReader to insert
				// conv instructions.
				return false;
			}
			// With small integer types, test whether the value might be changed by
			// truncation (based on type.GetSize()) followed by sign/zero extension (based on type.GetSign()).
			// (it's OK to have false-positives here if we're unsure)
			if (value.MatchLdcI4(out int val)) {
				switch (type.GetEnumUnderlyingType().GetDefinition()?.KnownTypeCode) {
					case KnownTypeCode.Boolean:
						return !(val == 0 || val == 1);
					case KnownTypeCode.Byte:
						return !(val >= byte.MinValue && val <= byte.MaxValue);
					case KnownTypeCode.SByte:
						return !(val >= sbyte.MinValue && val <= sbyte.MaxValue);
					case KnownTypeCode.Int16:
						return !(val >= short.MinValue && val <= short.MaxValue);
					case KnownTypeCode.UInt16:
					case KnownTypeCode.Char:
						return !(val >= ushort.MinValue && val <= ushort.MaxValue);
				}
			} else if (value is Conv conv) {
				return conv.TargetType != type.ToPrimitiveType();
			} else if (value is Comp) {
				return false; // comp returns 0 or 1, which always fits
			} else {
				IType inferredType = value.InferType();
				if (inferredType.Kind != TypeKind.Unknown) {
					return !(inferredType.GetSize() <= type.GetSize() && inferredType.GetSign() == type.GetSign());
				}
			}
			return true;
		}
		
		/// <code>
		/// stloc s(ldloc l)
		/// stloc l(binary.op(ldloc s, ldc.i4 1))
		/// -->
		/// stloc s(block {
		/// 	stloc s2(ldloc l)
		/// 	stloc l(binary.op(ldloc s2, ldc.i4 1))
		/// 	final: ldloc s2
		/// })
		/// </code>
		bool TransformPostIncDecOperator(Block block, int i)
		{
			var inst = block.Instructions[i] as StLoc;
			var nextInst = block.Instructions.ElementAtOrDefault(i + 1) as StLoc;
			if (inst == null || nextInst == null || !inst.Value.MatchLdLoc(out var l) || !ILVariableEqualityComparer.Instance.Equals(l, nextInst.Variable))
				return false;
			var binary = nextInst.Value as BinaryNumericInstruction;
			if (inst.Variable.Kind != VariableKind.StackSlot || nextInst.Variable.Kind == VariableKind.StackSlot || binary == null)
				return false;
			if (binary.IsLifted)
				return false;
			if ((binary.Operator != BinaryNumericOperator.Add && binary.Operator != BinaryNumericOperator.Sub) || !binary.Left.MatchLdLoc(inst.Variable) || !binary.Right.MatchLdcI4(1))
				return false;
			context.Step($"TransformPostIncDecOperator", inst);
			var tempStore = context.Function.RegisterVariable(VariableKind.StackSlot, inst.Variable.Type);
			var assignment = new Block(BlockType.PostfixOperator);
			assignment.Instructions.Add(new StLoc(tempStore, new LdLoc(nextInst.Variable)));
			assignment.Instructions.Add(new StLoc(nextInst.Variable, new BinaryNumericInstruction(binary.Operator, new LdLoc(tempStore), new LdcI4(1), binary.CheckForOverflow, binary.Sign)));
			assignment.FinalInstruction = new LdLoc(tempStore);
			nextInst.ReplaceWith(new StLoc(inst.Variable, assignment));
			return true;
		}

		/// <code>
		/// stobj(target, binary.add(stloc l(ldobj(target)), ldc.i4 1))
		///   where target is pure and does not use 'l'
		/// -->
		/// stloc l(compound.op.old(ldobj(target), ldc.i4 1))
		/// </code>
		bool TransformPostIncDecOperatorOnAddress(Block block, int i)
		{
			if (!(block.Instructions[i] is StObj stobj))
				return false;
			var binary = UnwrapSmallIntegerConv(stobj.Value, out var conv) as BinaryNumericInstruction;
			if (binary == null || !binary.Right.MatchLdcI4(1))
				return false;
			if (!(binary.Operator == BinaryNumericOperator.Add || binary.Operator == BinaryNumericOperator.Sub))
				return false;
			if (!(binary.Left is StLoc stloc))
				return false;
			if (!(stloc.Variable.Kind == VariableKind.Local || stloc.Variable.Kind == VariableKind.StackSlot))
				return false;
			if (!(stloc.Value is LdObj ldobj))
				return false;
			if (!SemanticHelper.IsPure(ldobj.Target.Flags))
				return false;
			if (!ldobj.Target.Match(stobj.Target).Success)
				return false;
			if (stloc.Variable.IsUsedWithin(ldobj.Target))
				return false;
			IType targetType = ldobj.Type;
			if (!ValidateCompoundAssign(binary, conv, targetType))
				return false;
			context.Step("TransformPostIncDecOperatorOnAddress", stobj);
			block.Instructions[i] = new StLoc(stloc.Variable, new CompoundAssignmentInstruction(
				binary, ldobj, binary.Right, targetType, CompoundAssignmentType.EvaluatesToOldValue));
			return true;
		}

		/// <code>
		/// stloc l(ldobj(target))
		/// stobj(target, binary.op(ldloc l, ldc.i4 1))
		///   target is pure and does not use 'l'
		/// -->
		/// stloc l(compound.op.old(ldobj(target), ldc.i4 1))
		/// </code>
		bool TransformRoslynPostIncDecOperatorOnAddress(Block block, int i)
		{
			var inst = block.Instructions[i] as StLoc;
			var stobj = block.Instructions.ElementAtOrDefault(i + 1) as StObj;
			if (inst == null || stobj == null)
				return false;
			if (!(inst.Value is LdObj ldobj))
				return false;
			if (!SemanticHelper.IsPure(ldobj.Target.Flags))
				return false;
			if (!ldobj.Target.Match(stobj.Target).Success)
				return false;
			if (inst.Variable.IsUsedWithin(ldobj.Target))
				return false;
			var binary = UnwrapSmallIntegerConv(stobj.Value, out var conv) as BinaryNumericInstruction;
			if (binary == null || !binary.Left.MatchLdLoc(inst.Variable) || !binary.Right.MatchLdcI4(1))
				return false;
			if (!(binary.Operator == BinaryNumericOperator.Add || binary.Operator == BinaryNumericOperator.Sub))
				return false;
			var targetType = ldobj.Type;
			if (!ValidateCompoundAssign(binary, conv, targetType))
				return false;
			context.Step("TransformRoslynPostIncDecOperatorOnAddress", inst);
			inst.Value = new CompoundAssignmentInstruction(binary, inst.Value, binary.Right, targetType, CompoundAssignmentType.EvaluatesToOldValue);
			block.Instructions.RemoveAt(i + 1);
			return true;
		}

		/// <code>
		/// stloc s(ldflda)
		/// stloc s2(ldobj(ldflda(ldloc s)))
		/// stloc l(ldloc s2)
		/// stobj (ldflda(ldloc s), binary.add(ldloc s2, ldc.i4 1))
		/// -->
		/// stloc l(compound.op.old(ldobj(ldflda(ldflda)), ldc.i4 1))
		/// </code>
		bool TransformCSharp4PostIncDecOperatorOnAddress(Block block, int i)
		{
			var baseFieldAddress = block.Instructions[i] as StLoc;
			var fieldValue = block.Instructions.ElementAtOrDefault(i + 1) as StLoc;
			var fieldValueCopyToLocal = block.Instructions.ElementAtOrDefault(i + 2) as StLoc;
			var stobj = block.Instructions.ElementAtOrDefault(i + 3) as StObj;
			if (baseFieldAddress == null || fieldValue == null || fieldValueCopyToLocal == null || stobj == null)
				return false;
			if (baseFieldAddress.Variable.Kind != VariableKind.StackSlot || fieldValue.Variable.Kind != VariableKind.StackSlot || fieldValueCopyToLocal.Variable.Kind != VariableKind.Local)
				return false;
			IType t;
			IField targetField;
			ILInstruction targetFieldLoad, baseFieldAddressLoad2;
			if (!fieldValue.Value.MatchLdObj(out targetFieldLoad, out t))
				return false;
			ILInstruction baseAddress;
			if (baseFieldAddress.Value is LdFlda) {
				IField targetField2;
				ILInstruction baseFieldAddressLoad3;
				if (!targetFieldLoad.MatchLdFlda(out baseFieldAddressLoad2, out targetField) || !baseFieldAddressLoad2.MatchLdLoc(baseFieldAddress.Variable))
					return false;
				if (!stobj.Target.MatchLdFlda(out baseFieldAddressLoad3, out targetField2) || !baseFieldAddressLoad3.MatchLdLoc(baseFieldAddress.Variable) || !IsSameMember(targetField, targetField2))
					return false;
				baseAddress = new LdFlda(baseFieldAddress.Value, targetField);
			} else if (baseFieldAddress.Value is LdElema) {
				if (!targetFieldLoad.MatchLdLoc(baseFieldAddress.Variable) || !stobj.Target.MatchLdLoc(baseFieldAddress.Variable))
					return false;
				baseAddress = baseFieldAddress.Value;
			} else {
				return false;
			}
			BinaryNumericInstruction binary = stobj.Value as BinaryNumericInstruction;
			if (binary == null || !binary.Left.MatchLdLoc(fieldValue.Variable) || !binary.Right.MatchLdcI4(1)
			    || (binary.Operator != BinaryNumericOperator.Add && binary.Operator != BinaryNumericOperator.Sub))
				return false;
			context.Step($"TransformCSharp4PostIncDecOperatorOnAddress", baseFieldAddress);
			var assignment = new CompoundAssignmentInstruction(binary, new LdObj(baseAddress, t), binary.Right, t, CompoundAssignmentType.EvaluatesToOldValue);
			stobj.ReplaceWith(new StLoc(fieldValueCopyToLocal.Variable, assignment));
			block.Instructions.RemoveAt(i + 2);
			block.Instructions.RemoveAt(i + 1);
			return true;
		}
		
		/// <code>
		/// stloc s(ldobj(ldsflda))
		/// stobj (ldsflda, binary.op(ldloc s, ldc.i4 1))
		/// -->
		/// stloc s(compound.op.old(ldobj(ldsflda), ldc.i4 1))
		/// </code>
		bool TransformPostIncDecOnStaticField(Block block, int i)
		{
			var inst = block.Instructions[i] as StLoc;
			var stobj = block.Instructions.ElementAtOrDefault(i + 1) as StObj;
			if (inst == null || stobj == null)
				return false;
			ILInstruction target;
			IType type;
			IField field, field2;
			if (inst.Variable.Kind != VariableKind.StackSlot || !inst.Value.MatchLdObj(out target, out type) || !target.MatchLdsFlda(out field))
				return false;
			if (!stobj.Target.MatchLdsFlda(out field2) || !IsSameMember(field, field2))
				return false;
			var binary = stobj.Value as BinaryNumericInstruction;
			if (binary == null || !binary.Left.MatchLdLoc(inst.Variable) || !binary.Right.MatchLdcI4(1))
				return false;
			context.Step($"TransformPostIncDecOnStaticField", inst);
			var assignment = new CompoundAssignmentInstruction(binary, inst.Value, binary.Right, type, CompoundAssignmentType.EvaluatesToOldValue);
			stobj.ReplaceWith(new StLoc(inst.Variable, assignment));
			return true;
		}
		
		static bool IsSameMember(IMember a, IMember b)
		{
			if (a == null || b == null)
				return false;
			a = a.MemberDefinition;
			b = b.MemberDefinition;
			return a.Equals(b);
		}
	}
}
