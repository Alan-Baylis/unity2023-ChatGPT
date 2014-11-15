﻿// Copyright (c) 2014 Daniel Grunwald
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

using System.Diagnostics;
using ExpressionType = System.Linq.Expressions.ExpressionType;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.TypeSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICSharpCode.Decompiler.CSharp
{
	/// <summary>
	/// Translates from ILAst to C# expressions.
	/// </summary>
	class ExpressionBuilder : ILVisitor<ConvertedExpression>
	{
		internal readonly ICompilation compilation;
		internal readonly NRefactoryCecilMapper cecilMapper;
		internal readonly CSharpResolver resolver;
		internal readonly TypeSystemAstBuilder astBuilder;
		
		public ExpressionBuilder(ICompilation compilation, NRefactoryCecilMapper cecilMapper)
		{
			Debug.Assert(compilation != null);
			Debug.Assert(cecilMapper != null);
			this.compilation = compilation;
			this.cecilMapper = cecilMapper;
			this.resolver = new CSharpResolver(compilation);
			this.astBuilder = new TypeSystemAstBuilder(resolver);
		}

		public AstType ConvertType(Mono.Cecil.TypeReference typeReference)
		{
			if (typeReference == null)
				return AstType.Null;
			var type = cecilMapper.GetType(typeReference);
			return ConvertType(type);
		}

		public AstType ConvertType(IType type)
		{
			var astType = astBuilder.ConvertType(type);
			astType.AddAnnotation(new TypeResolveResult(type));
			return astType;
		}
		
		public ConvertedExpression Convert(ILInstruction inst)
		{
			Debug.Assert(inst != null);
			var cexpr = inst.AcceptVisitor(this);
			Debug.Assert(cexpr.Type.GetStackType() == inst.ResultType || cexpr.Type.Kind == TypeKind.Unknown || inst.ResultType == StackType.Void);
			return cexpr;
		}
		
		public ConvertedExpression ConvertCondition(ILInstruction condition)
		{
			var expr = Convert(condition);
			return expr.ConvertToBoolean(this);
		}
		
		ExpressionWithResolveResult ConvertVariable(ILVariable variable)
		{
			Expression expr;
			if (variable.Kind == VariableKind.This)
				expr = new ThisReferenceExpression();
			else
				expr = new IdentifierExpression(variable.Name);
			// TODO: use LocalResolveResult instead
			if (variable.Type.SkipModifiers().MetadataType == Mono.Cecil.MetadataType.ByReference) {
				// When loading a by-ref parameter, use 'ref paramName'.
				// We'll strip away the 'ref' when dereferencing.
				
				// Ensure that the IdentifierExpression itself also gets a resolve result, as that might
				// get used after the 'ref' is stripped away:
				var elementType = variable.Type.SkipModifiers().GetElementType();
				expr.WithRR(new ResolveResult(cecilMapper.GetType(elementType)));
				
				expr = new DirectionExpression(FieldDirection.Ref, expr);
			}
			return expr.WithRR(new ResolveResult(cecilMapper.GetType(variable.Type)));
		}
		
		ConvertedExpression IsType(IsInst inst)
		{
			var arg = Convert(inst.Argument);
			var type = cecilMapper.GetType(inst.Type);
			return new IsExpression(arg.Expression, ConvertType(type))
				.WithILInstruction(inst)
				.WithRR(new TypeIsResolveResult(arg.ResolveResult, type, compilation.FindType(TypeCode.Boolean)));
		}
		
		protected internal override ConvertedExpression VisitIsInst(IsInst inst)
		{
			var arg = Convert(inst.Argument);
			var type = cecilMapper.GetType(inst.Type);
			return new AsExpression(arg.Expression, ConvertType(type))
				.WithILInstruction(inst)
				.WithRR(new ConversionResolveResult(type, arg.ResolveResult, Conversion.TryCast));
		}
		
		protected internal override ConvertedExpression VisitNewObj(NewObj inst)
		{
			return HandleCallInstruction(inst);
		}

		protected internal override ConvertedExpression VisitLdcI4(LdcI4 inst)
		{
			return new PrimitiveExpression(inst.Value)
				.WithILInstruction(inst)
				.WithRR(new ConstantResolveResult(compilation.FindType(KnownTypeCode.Int32), inst.Value));
		}
		
		protected internal override ConvertedExpression VisitLdcI8(LdcI8 inst)
		{
			return new PrimitiveExpression(inst.Value)
				.WithILInstruction(inst)
				.WithRR(new ConstantResolveResult(compilation.FindType(KnownTypeCode.Int64), inst.Value));
		}
		
		protected internal override ConvertedExpression VisitLdcF(LdcF inst)
		{
			return new PrimitiveExpression(inst.Value)
				.WithILInstruction(inst)
				.WithRR(new ConstantResolveResult(compilation.FindType(KnownTypeCode.Double), inst.Value));
		}
		
		protected internal override ConvertedExpression VisitLdStr(LdStr inst)
		{
			return new PrimitiveExpression(inst.Value)
				.WithILInstruction(inst)
				.WithRR(new ConstantResolveResult(compilation.FindType(KnownTypeCode.String), inst.Value));
		}
		
		protected internal override ConvertedExpression VisitLdNull(LdNull inst)
		{
			return new NullReferenceExpression()
				.WithILInstruction(inst)
				.WithRR(new ConstantResolveResult(SpecialType.UnknownType, null));
		}
		
		protected internal override ConvertedExpression VisitLogicNot(LogicNot inst)
		{
			return LogicNot(ConvertCondition(inst.Argument)).WithILInstruction(inst);
		}
		
		ExpressionWithResolveResult LogicNot(ConvertedExpression expr)
		{
			return new UnaryOperatorExpression(UnaryOperatorType.Not, expr.Expression)
				.WithRR(new OperatorResolveResult(compilation.FindType(KnownTypeCode.Boolean), ExpressionType.Not));
		}
		
		protected internal override ConvertedExpression VisitLdLoc(LdLoc inst)
		{
			return ConvertVariable(inst.Variable).WithILInstruction(inst);
		}
		
		protected internal override ConvertedExpression VisitLdLoca(LdLoca inst)
		{
			var expr = ConvertVariable(inst.Variable).WithILInstruction(inst);
			// Note that we put the instruction on the IdentifierExpression instead of the DirectionExpression,
			// because the DirectionExpression might get removed by dereferencing instructions such as LdObj
			return new DirectionExpression(FieldDirection.Ref, expr.Expression)
				.WithoutILInstruction()
				.WithRR(new ByReferenceResolveResult(expr.ResolveResult, isOut: false));
		}
		
		protected internal override ConvertedExpression VisitStLoc(StLoc inst)
		{
			return Assignment(ConvertVariable(inst.Variable).WithoutILInstruction(), Convert(inst.Value)).WithILInstruction(inst);
		}
		
		protected internal override ConvertedExpression VisitCeq(Ceq inst)
		{
			// Translate '(e as T) == null' to '!(e is T)'.
			// This is necessary for correctness when T is a value type.
			if (inst.Left.OpCode == OpCode.IsInst && inst.Right.OpCode == OpCode.LdNull) {
				return LogicNot(IsType((IsInst)inst.Left)).WithILInstruction(inst);
			} else if (inst.Right.OpCode == OpCode.IsInst && inst.Left.OpCode == OpCode.LdNull) {
				return LogicNot(IsType((IsInst)inst.Right)).WithILInstruction(inst);
			}
			
			var left = Convert(inst.Left);
			var right = Convert(inst.Right);
			
			// Remove redundant bool comparisons
			if (left.Type.IsKnownType(KnownTypeCode.Boolean)) {
				if (inst.Right.MatchLdcI4(0))
					return LogicNot(left).WithILInstruction(inst); // 'b == 0' => '!b'
				if (inst.Right.MatchLdcI4(1))
					return left; // 'b == 1' => 'b'
			} else if (right.Type.IsKnownType(KnownTypeCode.Boolean)) {
				if (inst.Left.MatchLdcI4(0))
					return LogicNot(right).WithILInstruction(inst); // '0 == b' => '!b'
				if (inst.Left.MatchLdcI4(1))
					return right; // '1 == b' => 'b'
			}
			
			var rr = resolver.ResolveBinaryOperator(BinaryOperatorType.Equality, left.ResolveResult, right.ResolveResult);
			if (rr.IsError) {
				// TODO: insert casts to the wider type of the two input types
			}
			return new BinaryOperatorExpression(left.Expression, BinaryOperatorType.Equality, right.Expression)
				.WithILInstruction(inst)
				.WithRR(rr);
		}
		
		protected internal override ConvertedExpression VisitClt(Clt inst)
		{
			return Comparison(inst, BinaryOperatorType.LessThan);
		}
		
		protected internal override ConvertedExpression VisitCgt(Cgt inst)
		{
			return Comparison(inst, BinaryOperatorType.GreaterThan);
		}
		
		protected internal override ConvertedExpression VisitClt_Un(Clt_Un inst)
		{
			return Comparison(inst, BinaryOperatorType.LessThan, un: true);
		}

		protected internal override ConvertedExpression VisitCgt_Un(Cgt_Un inst)
		{
			return Comparison(inst, BinaryOperatorType.GreaterThan, un: true);
		}
		
		ConvertedExpression Comparison(BinaryComparisonInstruction inst, BinaryOperatorType op, bool un = false)
		{
			var left = Convert(inst.Left);
			var right = Convert(inst.Right);
			// TODO: ensure the arguments are signed
			// or with _Un: ensure the arguments are unsigned; and that float comparisons are performed unordered
			return new BinaryOperatorExpression(left.Expression, op, right.Expression)
				.WithILInstruction(inst)
				.WithRR(new OperatorResolveResult(compilation.FindType(TypeCode.Boolean),
				                                  BinaryOperatorExpression.GetLinqNodeType(op, false),
				                                  left.ResolveResult, right.ResolveResult));
		}
		
		ExpressionWithResolveResult Assignment(ConvertedExpression left, ConvertedExpression right)
		{
			right = right.ConvertTo(left.Type, this);
			return new AssignmentExpression(left.Expression, right.Expression)
				.WithRR(new OperatorResolveResult(left.Type, ExpressionType.Assign, left.ResolveResult, right.ResolveResult));
		}
		
		protected internal override ConvertedExpression VisitAdd(Add inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.Add);
		}
		
		protected internal override ConvertedExpression VisitSub(Sub inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.Subtract);
		}
		
		protected internal override ConvertedExpression VisitMul(Mul inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.Multiply);
		}
		
		protected internal override ConvertedExpression VisitDiv(Div inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.Divide);
		}
		
		protected internal override ConvertedExpression VisitRem(Rem inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.Modulus);
		}
		
		protected internal override ConvertedExpression VisitBitXor(BitXor inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.ExclusiveOr);
		}
		
		protected internal override ConvertedExpression VisitBitAnd(BitAnd inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.BitwiseAnd);
		}
		
		protected internal override ConvertedExpression VisitBitOr(BitOr inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.BitwiseOr);
		}
		
		protected internal override ConvertedExpression VisitShl(Shl inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.ShiftLeft);
		}
		
		protected internal override ConvertedExpression VisitShr(Shr inst)
		{
			return HandleBinaryNumeric(inst, BinaryOperatorType.ShiftRight);
		}
		
		ConvertedExpression HandleBinaryNumeric(BinaryNumericInstruction inst, BinaryOperatorType op)
		{
			var resolverWithOverflowCheck = resolver.WithCheckForOverflow(inst.CheckForOverflow);
			var left = Convert(inst.Left);
			var right = Convert(inst.Right);
			var rr = resolverWithOverflowCheck.ResolveBinaryOperator(op, left.ResolveResult, right.ResolveResult);
			if (rr.IsError || rr.Type.GetStackType() != inst.ResultType
			    || !IsCompatibleWithSign(left.Type, inst.Sign) || !IsCompatibleWithSign(right.Type, inst.Sign))
			{
				// Left and right operands are incompatible, so convert them to a common type
				IType targetType = compilation.FindType(inst.ResultType.ToKnownTypeCode(inst.Sign));
				left = left.ConvertTo(targetType, this);
				right = right.ConvertTo(targetType, this);
				rr = resolverWithOverflowCheck.ResolveBinaryOperator(op, left.ResolveResult, right.ResolveResult);
			}
			return new BinaryOperatorExpression(left.Expression, op, right.Expression)
				.WithILInstruction(inst)
				.WithRR(rr);
		}

		/// <summary>
		/// Gets whether <paramref name="type"/> has the specified <paramref name="sign"/>.
		/// If <paramref name="sign"/> is None, always returns true.
		/// </summary>
		bool IsCompatibleWithSign(IType type, Sign sign)
		{
			return sign == Sign.None || type.GetSign() == sign;
		}
		
		protected internal override ConvertedExpression VisitConv(Conv inst)
		{
			var arg = Convert(inst.Argument);
			if (arg.Type.GetSign() != inst.Sign) {
				// we need to cast the input to a type of appropriate sign
				var inputType = inst.Argument.ResultType.ToKnownTypeCode(inst.Sign);
				arg = arg.ConvertTo(compilation.FindType(inputType), this);
			}
			var targetType = compilation.FindType(inst.TargetType.ToKnownTypeCode());
			var rr = resolver.WithCheckForOverflow(inst.CheckForOverflow).ResolveCast(targetType, arg.ResolveResult);
			return new CastExpression(ConvertType(targetType), arg.Expression)
				.WithILInstruction(inst)
				.WithRR(rr);
		}
		
		protected internal override ConvertedExpression VisitCall(Call inst)
		{
			return HandleCallInstruction(inst);
		}
		
		protected internal override ConvertedExpression VisitCallVirt(CallVirt inst)
		{
			return HandleCallInstruction(inst);
		}
		
		ConvertedExpression HandleCallInstruction(CallInstruction inst)
		{
			// Used for Call, CallVirt and NewObj
			var method = cecilMapper.GetMethod(inst.Method);
			ConvertedExpression target;
			if (inst.OpCode == OpCode.NewObj) {
				target = default(ConvertedExpression); // no target
			} else if (inst.Method.HasThis) {
				var argInstruction = inst.Arguments[0];
				if (inst.OpCode == OpCode.Call && argInstruction.MatchLdThis()) {
					target = new BaseReferenceExpression()
						.WithILInstruction(argInstruction)
						.WithRR(new ThisResolveResult(cecilMapper.GetType(inst.Method.DeclaringType), causesNonVirtualInvocation: true));
				} else {
					target = Convert(argInstruction);
				}
			} else {
				var declaringType = cecilMapper.GetType(inst.Method.DeclaringType);
				target = new TypeReferenceExpression(ConvertType(declaringType))
					.WithoutILInstruction()
					.WithRR(new TypeResolveResult(declaringType));
			}
			
			var arguments = inst.Arguments.SelectArray(Convert);
			int firstParamIndex = (inst.Method.HasThis && inst.OpCode != OpCode.NewObj) ? 1 : 0;
			Debug.Assert(arguments.Length == firstParamIndex + inst.Method.Parameters.Count);
			ResolveResult rr;
			if (method != null) {
				// Convert arguments to the expected parameter types
				Debug.Assert(arguments.Length == firstParamIndex + method.Parameters.Count);
				for (int i = firstParamIndex; i < arguments.Length; i++) {
					var parameter = method.Parameters[i - firstParamIndex];
					arguments[i] = arguments[i].ConvertTo(parameter.Type, this);
				}
				var argumentResolveResults = arguments.Skip(firstParamIndex).Select(arg => arg.ResolveResult).ToList();
				rr = new CSharpInvocationResolveResult(target.ResolveResult, method, argumentResolveResults);
			} else {
				// no IMethod found -- determine the target types from the cecil parameter collection instead
				for (int i = firstParamIndex; i < arguments.Length; i++) {
					var parameterDefinition = inst.Method.Parameters[i - firstParamIndex];
					var parameterType = cecilMapper.GetType(parameterDefinition.ParameterType);
					arguments[i] = arguments[i].ConvertTo(parameterType, this);
				}
				if (inst.OpCode == OpCode.NewObj) {
					rr = new ResolveResult(cecilMapper.GetType(inst.Method.DeclaringType));
				} else {
					rr = new ResolveResult(cecilMapper.GetType(inst.Method.ReturnType));
				}
			}
			
			var argumentExpressions = arguments.Skip(firstParamIndex).Select(arg => arg.Expression);
			if (inst.OpCode == OpCode.NewObj) {
				return new ObjectCreateExpression(ConvertType(inst.Method.DeclaringType), argumentExpressions)
					.WithILInstruction(inst).WithRR(rr);
			} else {
				var mre = new MemberReferenceExpression(target.Expression, inst.Method.Name);
				return new InvocationExpression(mre, argumentExpressions)
					.WithILInstruction(inst).WithRR(rr);
			}
		}
		
		protected internal override ConvertedExpression VisitLdObj(LdObj inst)
		{
			var target = Convert(inst.Target);
			var type = cecilMapper.GetType(inst.Type);
			if (target.Type.Equals(new ByReferenceType(type)) && target.Expression is DirectionExpression) {
				// we can deference the managed reference by stripping away the 'ref'
				var result = target.UnwrapChild(((DirectionExpression)target.Expression).Expression);
				result = result.ConvertTo(type, this);
				result.Expression.AddAnnotation(inst); // add LdObj in addition to the existing ILInstruction annotation
				return result;
			} else {
				// Cast pointer type if necessary:
				target = target.ConvertTo(new PointerType(type), this);
				return new UnaryOperatorExpression(UnaryOperatorType.Dereference, target.Expression)
					.WithILInstruction(inst)
					.WithRR(new ResolveResult(type));
			}
		}
		
		protected internal override ConvertedExpression VisitUnboxAny(UnboxAny inst)
		{
			var arg = Convert(inst.Argument);
			if (arg.Type.IsReferenceType != true) {
				// ensure we treat the input as a reference type
				arg = arg.ConvertTo(compilation.FindType(KnownTypeCode.Object), this);
			}
			return new CastExpression(ConvertType(inst.Type), arg.Expression)
				.WithILInstruction(inst)
				.WithRR(new ConversionResolveResult(cecilMapper.GetType(inst.Type), arg.ResolveResult, Conversion.UnboxingConversion));
		}

		protected override ConvertedExpression Default(ILInstruction inst)
		{
			return ErrorExpression("OpCode not supported: " + inst.OpCode);
		}

		static ConvertedExpression ErrorExpression(string message)
		{
			var e = new ErrorExpression();
			e.AddChild(new Comment(message, CommentType.MultiLine), Roles.Comment);
			return e.WithoutILInstruction().WithRR(ErrorResolveResult.UnknownError);
		}
	}
}
