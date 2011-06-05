﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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
using System.Collections.Generic;
using System.Linq;

using Mono.Cecil;

namespace ICSharpCode.Decompiler.ILAst
{
	partial class ILAstOptimizer
	{
		bool SimplifyNullableOperators(List<ILNode> body, ILExpression expr, int pos)
		{
			if (!SimplifyNullableOperators(expr)) return false;

			var inlining = new ILInlining(method);
			while (--pos >= 0 && inlining.InlineIfPossible(body, ref pos)) ;

			return true;
		}

		static bool SimplifyNullableOperators(ILExpression expr)
		{
			if (PatternMatcher.Simplify(expr)) return true;

			bool modified = false;
			foreach (var a in expr.Arguments)
				modified |= SimplifyNullableOperators(a);
			return modified;
		}

		struct PatternMatcher
		{
			abstract class Pattern
			{
				public readonly Pattern[] Arguments;

				protected Pattern(Pattern[] arguments)
				{
					this.Arguments = arguments;
				}

				public virtual bool Match(ref PatternMatcher pm, ILExpression e)
				{
					if (e.Arguments.Count != this.Arguments.Length || e.Prefixes != null) return false;
					for (int i = 0; i < this.Arguments.Length; i++)
						if (!this.Arguments[i].Match(ref pm, e.Arguments[i])) return false;
					return true;
				}

				public virtual ILExpression BuildNew(ref PatternMatcher pm)
				{
					throw new NotSupportedException();
				}

				public static Pattern operator &(Pattern a, Pattern b)
				{
					return new ILPattern(ILCode.LogicAnd, a, b);
				}

				public static Pattern operator |(Pattern a, Pattern b)
				{
					return new ILPattern(ILCode.LogicOr, a, b);
				}

				public static Pattern operator !(Pattern a)
				{
					return new ILPattern(ILCode.LogicNot, a);
				}
			}

			sealed class ILPattern : Pattern
			{
				readonly ILCode code;

				public ILPattern(ILCode code, params Pattern[] arguments)
					: base(arguments)
				{
					this.code = code;
				}

				public override bool Match(ref PatternMatcher pm, ILExpression e)
				{
					return e.Code == this.code && base.Match(ref pm, e);
				}

				public override ILExpression BuildNew(ref PatternMatcher pm)
				{
					var args = new ILExpression[this.Arguments.Length];
					for (int i = 0; i < args.Length; i++) args[i] = this.Arguments[i].BuildNew(ref pm);
					return new ILExpression(code, null, args);
				}
			}

			sealed class MethodPattern : Pattern
			{
				readonly ILCode code;
				readonly string method;

				public MethodPattern(ILCode code, string method, params Pattern[] arguments)
					: base(arguments)
				{
					this.code = code;
					this.method = method;
				}

				public override bool Match(ref PatternMatcher pm, ILExpression e)
				{
					if (e.Code != this.code) return false;
					var m = e.Operand as MethodReference;
					if (m == null || m.Name != this.method) return false;
					var t = m.DeclaringType;
					return t.Name == "Nullable`1" && t.Namespace == "System" && base.Match(ref pm, e);
				}
			}

			enum OperatorType
			{
				Equality, InEquality, Comparison, Other
			}

			sealed class OperatorPattern : Pattern
			{
				OperatorType type;
				bool simple;

				public OperatorPattern() : base(null) { }

				public OperatorPattern(OperatorType type, bool simple)
					: this()
				{
					this.type = type;
					this.simple = simple;
				}

				public override bool Match(ref PatternMatcher pm, ILExpression e)
				{
					switch (e.Code) {
						case ILCode.Ceq:
							if (type != OperatorType.Equality) return false;
							break;
						case ILCode.Cne:
							if (type != OperatorType.InEquality) return false;
							break;
						case ILCode.Cgt:
						case ILCode.Cgt_Un:
						case ILCode.Cge:
						case ILCode.Cge_Un:
						case ILCode.Clt:
						case ILCode.Clt_Un:
						case ILCode.Cle:
						case ILCode.Cle_Un:
							if (type != OperatorType.Comparison) return false;
							break;
						case ILCode.Add:
						case ILCode.Add_Ovf:
						case ILCode.Add_Ovf_Un:
						case ILCode.Sub:
						case ILCode.Sub_Ovf:
						case ILCode.Sub_Ovf_Un:
						case ILCode.Mul:
						case ILCode.Mul_Ovf:
						case ILCode.Mul_Ovf_Un:
						case ILCode.Div:
						case ILCode.Div_Un:
						case ILCode.Rem:
						case ILCode.Rem_Un:
						case ILCode.And:
						case ILCode.Or:
						case ILCode.Xor:
						case ILCode.Shl:
						case ILCode.Shr:
						case ILCode.Shr_Un:
						case ILCode.Not:
						case ILCode.Neg:
						case ILCode.LogicNot:
							if (type != OperatorType.Other) return false;
							break;
						case ILCode.Call:
							var m = e.Operand as MethodReference;
							if (m == null || m.HasThis || !m.HasParameters || e.Arguments.Count > 2 || !IsCustomOperator(m.Name)) return false;
							break;
						default: return false;
					}
					if (pm.Operator != null) throw new InvalidOperationException();
					pm.Operator = e;

					var a0 = e.Arguments[0];
					if (!simple) return VariableAGetValueOrDefault.Match(ref pm, a0) && VariableBGetValueOrDefault.Match(ref pm, e.Arguments[1]);
					if (e.Arguments.Count == 1) return VariableAGetValueOrDefault.Match(ref pm, a0);
					if (VariableAGetValueOrDefault.Match(ref pm, a0)) {
						pm.SimpleOperand = e.Arguments[1];
						pm.SimpleLeftOperand = false;
						return true;
					}
					if (VariableAGetValueOrDefault.Match(ref pm, e.Arguments[1])) {
						pm.SimpleOperand = a0;
						pm.SimpleLeftOperand = true;
						return true;
					}
					return false;
				}

				bool IsCustomOperator(string s)
				{
					if (s.Length < 11 || !s.StartsWith("op_", StringComparison.Ordinal)) return false;
					switch (s) {
						case "op_Equality":
							return type == OperatorType.Equality;
						case "op_Inequality":
							return type == OperatorType.InEquality;
						case "op_GreaterThan":
						case "op_GreaterThanOrEqual":
						case "op_LessThan":
						case "op_LessThanOrEqual":
							return type == OperatorType.Comparison;
						case "op_Addition":
						case "op_Subtraction":
						case "op_Multiply":
						case "op_Division":
						case "op_Modulus":
						case "op_BitwiseAnd":
						case "op_BitwiseOr":
						case "op_ExclusiveOr":
						case "op_LeftShift":
						case "op_RightShift":
						case "op_Negation":
						case "op_UnaryNegation":
						case "op_UnaryPlus":
							return type == OperatorType.Other;
						default: return false;
					}
				}

				public override ILExpression BuildNew(ref PatternMatcher pm)
				{
					var res = pm.Operator;
					res.Arguments.Clear();
					if (pm.SimpleLeftOperand) res.Arguments.Add(pm.SimpleOperand);
					res.Arguments.Add(VariableA.BuildNew(ref pm));
					if (pm.B != null) res.Arguments.Add(VariableB.BuildNew(ref pm));
					else if (pm.SimpleOperand != null && !pm.SimpleLeftOperand) res.Arguments.Add(pm.SimpleOperand);
					return res;
				}
			}

			sealed class VariablePattern : Pattern
			{
				readonly ILCode code;
				readonly bool b;

				public VariablePattern(ILCode code, bool b)
					: base(null)
				{
					this.code = code;
					this.b = b;
				}

				public override bool Match(ref PatternMatcher pm, ILExpression e)
				{
					if (e.Code != this.code) return false;
					var v = e.Operand as ILVariable;
					return v != null && (this.b ? Capture(ref pm.B, v) : Capture(ref pm.A, v));
				}

				static bool Capture(ref ILVariable pmvar, ILVariable v)
				{
					if (pmvar != null) return pmvar == v;
					pmvar = v;
					return true;
				}

				static readonly ILExpression[] EmptyArguments = new ILExpression[0];
				public override ILExpression BuildNew(ref PatternMatcher pm)
				{
					var v = this.b ? pm.B : pm.A;
					var e = new ILExpression(ILCode.Ldloc, v, EmptyArguments);
					if (v.Type.Name == "Nullable`1" && v.Type.Namespace == "System") e = new ILExpression(ILCode.ValueOf, null, e);
					return e;
				}
			}

			static readonly Pattern VariableRefA = new VariablePattern(ILCode.Ldloca, false), VariableRefB = new VariablePattern(ILCode.Ldloca, true);
			static readonly Pattern VariableA = new VariablePattern(ILCode.Ldloc, false), VariableB = new VariablePattern(ILCode.Ldloc, true);
			static readonly Pattern VariableAHasValue = new MethodPattern(ILCode.CallGetter, "get_HasValue", VariableRefA);
			static readonly Pattern VariableAGetValueOrDefault = new MethodPattern(ILCode.Call, "GetValueOrDefault", VariableRefA);
			static readonly Pattern VariableBHasValue = new MethodPattern(ILCode.CallGetter, "get_HasValue", VariableRefB);
			static readonly Pattern VariableBGetValueOrDefault = new MethodPattern(ILCode.Call, "GetValueOrDefault", VariableRefB);
			static readonly Pattern CeqHasValue = new ILPattern(ILCode.Ceq, VariableAHasValue, VariableBHasValue);
			static readonly Pattern CneHasValue = new ILPattern(ILCode.Cne, VariableAHasValue, VariableBHasValue);
			static readonly Pattern AndHasValue = new ILPattern(ILCode.And, VariableAHasValue, VariableBHasValue);
			static readonly Pattern OperatorVariableAB = new OperatorPattern();

			static OperatorPattern OperatorNN(OperatorType type)
			{
				return new OperatorPattern(type, false);
			}

			static OperatorPattern OperatorNV(OperatorType type)
			{
				return new OperatorPattern(type, true);
			}

			static readonly Pattern[] Comparisons = new Pattern[] {
				/* both operands nullable */
				// == (primitive, decimal)
				OperatorNN(OperatorType.Equality) & CeqHasValue,
				// == (struct)
				CeqHasValue & (!VariableAHasValue | OperatorNN(OperatorType.Equality)),
				// != (primitive, decimal)
				OperatorNN(OperatorType.InEquality) | CneHasValue,
				// != (struct)
				CneHasValue | (VariableAHasValue & OperatorNN(OperatorType.InEquality)),
				// > , < , >= , <= (primitive, decimal)
				OperatorNN(OperatorType.Comparison) & AndHasValue,
				// > , < , >= , <= (struct)
				AndHasValue & OperatorNN(OperatorType.Comparison),

				/* only one operand nullable */
				// == (primitive, decimal)
				OperatorNV(OperatorType.Equality) & VariableAHasValue,
				// == (struct)
				VariableAHasValue & OperatorNV(OperatorType.Equality),
				// != (primitive, decimal)
				OperatorNV(OperatorType.InEquality) | !VariableAHasValue,
				// != (struct)
				!VariableAHasValue | OperatorNV(OperatorType.InEquality),
				// > , <, >= , <= (primitive, decimal)
				OperatorNV(OperatorType.Comparison) & VariableAHasValue,
				// > , < , >= , <= (struct)
				VariableAHasValue & OperatorNV(OperatorType.Comparison),
			};

			static readonly Pattern[] Other = new Pattern[] {
				/* both operands nullable */
				// & (bool)
				new ILPattern(ILCode.TernaryOp, VariableAGetValueOrDefault | (!VariableBGetValueOrDefault & !VariableAHasValue), VariableB, VariableA),
				new ILPattern(ILCode.And, VariableA, VariableB),
				// | (bool)
				new ILPattern(ILCode.TernaryOp, VariableAGetValueOrDefault | (!VariableBGetValueOrDefault & !VariableAHasValue), VariableA, VariableB),
				new ILPattern(ILCode.Or, VariableA, VariableB),
				// all other
				new ILPattern(ILCode.TernaryOp, AndHasValue, new MethodPattern(ILCode.Newobj, ".ctor", OperatorNN(OperatorType.Other)), new ILPattern(ILCode.DefaultValue)),
				null,

				/* only one operand nullable */
				new ILPattern(ILCode.TernaryOp, VariableAHasValue, new MethodPattern(ILCode.Newobj, ".ctor", OperatorNV(OperatorType.Other)), new ILPattern(ILCode.DefaultValue)),
				null,
			};

			ILVariable A, B;
			ILExpression Operator, SimpleOperand;
			bool SimpleLeftOperand;

			public static bool Simplify(ILExpression expr)
			{
				if (expr.Code == ILCode.LogicAnd || expr.Code == ILCode.LogicOr) {
					var ps = Comparisons;
					for (int i = 0; i < ps.Length; i++) {
						var pm = new PatternMatcher();
						if (!ps[i].Match(ref pm, expr)) continue;
						var n = OperatorVariableAB.BuildNew(ref pm);
						n.ILRanges = ILRange.OrderAndJoint(expr.GetSelfAndChildrenRecursive<ILExpression>().SelectMany(el => el.ILRanges));
						// the new expression is wrapped in a container so that negations aren't pushed through the comparison operation
						expr.Code = ILCode.Wrap;
						expr.Operand = null;
						expr.Arguments.Clear();
						expr.Arguments.Add(n);
						expr.ILRanges.Clear();
						expr.InferredType = n.InferredType;
						return true;
					}
				} else if (expr.Code == ILCode.TernaryOp) {
					var ps = Other;
					for (int i = 0; i < ps.Length; i += 2) {
						var pm = new PatternMatcher();
						if (!ps[i].Match(ref pm, expr)) continue;
						var n = (ps[i + 1] ?? OperatorVariableAB).BuildNew(ref pm);
						n.ILRanges = ILRange.OrderAndJoint(expr.GetSelfAndChildrenRecursive<ILExpression>().SelectMany(el => el.ILRanges));
						// the new expression is wrapped in a container so that negations aren't pushed through the comparison operation
						expr.Code = ILCode.Wrap;
						expr.Operand = null;
						expr.Arguments.Clear();
						expr.Arguments.Add(n);
						expr.ILRanges.Clear();
						expr.InferredType = n.InferredType;
						return true;
					}
				}
				return false;
			}
		}
	}
}
