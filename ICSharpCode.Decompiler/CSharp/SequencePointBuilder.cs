﻿// Copyright (c) 2017 Daniel Grunwald
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
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.CSharp
{
	/// <summary>
	/// Given a SyntaxTree that was output from the decompiler, constructs the list of sequence points.
	/// </summary>
	class SequencePointBuilder : DepthFirstAstVisitor
	{
		struct StatePerSequencePoint
		{
			/// <summary>
			/// Main AST node associated with this sequence point.
			/// </summary>
			internal readonly AstNode PrimaryNode;

			/// <summary>
			/// List of IL intervals that are associated with this sequence point.
			/// </summary>
			internal readonly List<Interval> Intervals;

			/// <summary>
			/// The function containing this sequence point.
			/// </summary>
			internal ILFunction Function;

			public StatePerSequencePoint(AstNode primaryNode)
			{
				this.PrimaryNode = primaryNode;
				this.Intervals = new List<Interval>();
				this.Function = null;
			}
		}

		readonly List<(ILFunction, SequencePoint)> sequencePoints = new List<(ILFunction, SequencePoint)>();
		readonly HashSet<ILInstruction> mappedInstructions = new HashSet<ILInstruction>();
		
		// Stack holding information for outer statements.
		readonly Stack<StatePerSequencePoint> outerStates = new Stack<StatePerSequencePoint>();

		// Collects information for the current sequence point.
		StatePerSequencePoint current = new StatePerSequencePoint();

		void VisitAsSequencePoint(AstNode node)
		{
			StartSequencePoint(node);
			node.AcceptVisitor(this);
			EndSequencePoint(node.StartLocation, node.EndLocation);
		}

		protected override void VisitChildren(AstNode node)
		{
			base.VisitChildren(node);
			AddToSequencePoint(node);
		}

		public override void VisitBlockStatement(BlockStatement blockStatement)
		{
			foreach (var stmt in blockStatement.Statements) {
				VisitAsSequencePoint(stmt);
			}
		}

		public override void VisitForStatement(ForStatement forStatement)
		{
			// Every element of a for-statement is it's own sequence point.
			foreach (var init in forStatement.Initializers) {
				VisitAsSequencePoint(init);
			}
			VisitAsSequencePoint(forStatement.Condition);
			foreach (var inc in forStatement.Iterators) {
				VisitAsSequencePoint(inc);
			}
			VisitAsSequencePoint(forStatement.EmbeddedStatement);
		}
		
		public override void VisitSwitchStatement(SwitchStatement switchStatement)
		{
			StartSequencePoint(switchStatement);
			switchStatement.Expression.AcceptVisitor(this);
			foreach (var section in switchStatement.SwitchSections) {
				// note: sections will not contribute to the current sequence point
				section.AcceptVisitor(this);
			}
			// add switch statement itself to sequence point
			// (call only after the sections are visited)
			AddToSequencePoint(switchStatement);
			EndSequencePoint(switchStatement.StartLocation, switchStatement.RParToken.EndLocation);
		}

		public override void VisitSwitchSection(Syntax.SwitchSection switchSection)
		{
			// every statement in the switch section is its own sequence point
			foreach (var stmt in switchSection.Statements) {
				VisitAsSequencePoint(stmt);
			}
		}

		/// <summary>
		/// Start a new C# statement = new sequence point.
		/// </summary>
		void StartSequencePoint(AstNode primaryNode)
		{
			outerStates.Push(current);
			current = new StatePerSequencePoint(primaryNode);
		}

		void EndSequencePoint(TextLocation startLocation, TextLocation endLocation)
		{
			if (current.Intervals.Count > 0 && current.Function != null) {
				sequencePoints.Add((current.Function, new SequencePoint {
					Offset = current.Intervals.Select(i => i.Start).Min(),
					StartLine = startLocation.Line,
					StartColumn = startLocation.Column,
					EndLine = endLocation.Line,
					EndColumn = endLocation.Column
				}));
			}
			current = outerStates.Pop();
		}

		/// <summary>
		/// Add the ILAst instruction associated with the AstNode to the sequence point.
		/// Also add all its ILAst sub-instructions (unless they were already added to another sequence point).
		/// </summary>
		void AddToSequencePoint(AstNode node)
		{
			foreach (var inst in node.Annotations.OfType<ILInstruction>()) {
				AddToSequencePoint(inst);
			}
		}

		void AddToSequencePoint(ILInstruction inst)
		{
			if (!mappedInstructions.Add(inst)) {
				// inst was already used by a nested sequence point within this sequence point
				return;
			}
			// Add the IL range associated with this instruction to the current sequence point.
			if (!inst.ILRange.IsEmpty) {
				current.Intervals.Add(inst.ILRange);
				current.Function = inst.Ancestors.OfType<ILFunction>().FirstOrDefault();
			}
			// Also add the child IL instructions, unless they were already processed by
			// another C# expression.
			foreach (var child in inst.Children) {
				AddToSequencePoint(child);
			}
		}

		/// <summary>
		/// Called after the visitor is done to return the results.
		/// </summary>
		internal Dictionary<ILFunction, List<SequencePoint>> GetSequencePoints()
		{
			var dict = new Dictionary<ILFunction, List<SequencePoint>>();
			foreach (var (function, sequencePoint) in this.sequencePoints) {
				if (!dict.TryGetValue(function, out var list)) {
					dict.Add(function, list = new List<SequencePoint>());
				}
				list.Add(sequencePoint);
			}
			foreach (var list in dict.Values) {
				list.Sort((a, b) => a.Offset.CompareTo(b.Offset));
			}
			return dict;
		}
	}
}
