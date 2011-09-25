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
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.ILAst;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.CSharp;
using Mono.Cecil;

namespace ICSharpCode.Decompiler.Ast
{
	public class TextOutputFormatter : IOutputFormatter
	{
		readonly ITextOutput output;
		readonly Stack<AstNode> nodeStack = new Stack<AstNode>();
		int braceLevelWithinType = -1;
		bool inDocumentationComment = false;
		
		public TextOutputFormatter(ITextOutput output)
		{
			if (output == null)
				throw new ArgumentNullException("output");
			this.output = output;
		}
		
		public void WriteIdentifier(string identifier)
		{
			object memberRef = GetCurrentMemberReference();

			if (memberRef != null) {
				output.WriteReference(identifier, memberRef, isIconMapping: IsIconMapping());
				return;
			}

			var definition = GetCurrentLocalDefinition();
			if (definition != null) {
				output.WriteDefinition(identifier, definition);
				return;
			}

			memberRef = GetCurrentLocalReference();
			if (memberRef != null) {
				output.WriteReference(identifier, memberRef, true);
				return;
			}

			output.Write(identifier);
		}

		MemberReference GetCurrentMemberReference()
		{
			AstNode node = nodeStack.Peek();
			MemberReference memberRef = node.Annotation<MemberReference>();
			if (memberRef == null && node.Role == AstNode.Roles.TargetExpression && (node.Parent is InvocationExpression || node.Parent is ObjectCreateExpression)) {
				memberRef = node.Parent.Annotation<MemberReference>();
			}
			return memberRef;
		}

		object GetCurrentLocalReference()
		{
			AstNode node = nodeStack.Peek();
			ILVariable variable = node.Annotation<ILVariable>();
			if (variable != null) {
				if (variable.OriginalParameter != null)
					return variable.OriginalParameter;
				//if (variable.OriginalVariable != null)
				//    return variable.OriginalVariable;
				return variable;
			}
			return null;
		}

		object GetCurrentLocalDefinition()
		{
			AstNode node = nodeStack.Peek();
			var parameterDef = node.Annotation<ParameterDefinition>();
			if (parameterDef != null)
				return parameterDef;

			if (node is VariableInitializer || node is CatchClause || node is ForeachStatement) {
				var variable = node.Annotation<ILVariable>();
				if (variable != null) {
					if (variable.OriginalParameter != null)
						return variable.OriginalParameter;
					//if (variable.OriginalVariable != null)
					//    return variable.OriginalVariable;
					return variable;
				} else {

				}
			}

			return null;
		}
		
		public void WriteKeyword(string keyword)
		{
			output.Write(keyword);
		}
		
		public void WriteToken(string token)
		{
			// Attach member reference to token only if there's no identifier in the current node.
			MemberReference memberRef = GetCurrentMemberReference();
			var node = nodeStack.Peek();
			if (memberRef != null && node.GetChildByRole(AstNode.Roles.Identifier).IsNull)
				output.WriteReference(token, memberRef, isIconMapping: IsIconMapping());
			else
				output.Write(token);
		}
		
		public void Space()
		{
			output.Write(' ');
		}
		
		public void OpenBrace(BraceStyle style)
		{
			if (braceLevelWithinType >= 0 || nodeStack.Peek() is TypeDeclaration)
				braceLevelWithinType++;
			if (nodeStack.OfType<BlockStatement>().Count() <= 1) {
				output.MarkFoldStart(defaultCollapsed: braceLevelWithinType == 1);
			}
			output.WriteLine();
			output.WriteLine("{");
			output.Indent();
		}
		
		public void CloseBrace(BraceStyle style)
		{
			output.Unindent();
			output.Write('}');
			if (nodeStack.OfType<BlockStatement>().Count() <= 1)
				output.MarkFoldEnd();
			if (braceLevelWithinType >= 0)
				braceLevelWithinType--;
		}
		
		public void Indent()
		{
			output.Indent();
		}
		
		public void Unindent()
		{
			output.Unindent();
		}
		
		public void NewLine()
		{
			output.WriteLine();
		}
		
		public void WriteComment(CommentType commentType, string content)
		{
			switch (commentType) {
				case CommentType.SingleLine:
					output.Write("//");
					output.WriteLine(content);
					break;
				case CommentType.MultiLine:
					output.Write("/*");
					output.Write(content);
					output.Write("*/");
					break;
				case CommentType.Documentation:
					bool isLastLine = !(nodeStack.Peek().NextSibling is Comment);
					if (!inDocumentationComment && !isLastLine) {
						inDocumentationComment = true;
						output.MarkFoldStart("///" + content, true);
					}
					output.Write("///");
					output.Write(content);
					if (inDocumentationComment && isLastLine) {
						inDocumentationComment = false;
						output.MarkFoldEnd();
					}
					output.WriteLine();
					break;
			}
		}
		
		Stack<TextLocation> startLocations = new Stack<TextLocation>();
		MemberMapping currentMemberMapping;
		Stack<MemberMapping> parentMemberMappings = new Stack<MemberMapping>();
		
		public void StartNode(AstNode node)
		{
			nodeStack.Push(node);
			startLocations.Push(output.Location);
			
			MemberMapping mapping = node.Annotation<MemberMapping>();
			if (mapping != null) {
				parentMemberMappings.Push(currentMemberMapping);
				currentMemberMapping = mapping;
			}
		}
		
		public void EndNode(AstNode node)
		{
			if (nodeStack.Pop() != node)
				throw new InvalidOperationException();
			
			var startLocation = startLocations.Pop();
			
			// code mappings
			if (currentMemberMapping != null) {
				var ranges = node.Annotation<List<ILRange>>();
				if (ranges != null && ranges.Count > 0) {
					// add all ranges
					foreach (var range in ranges) {
						currentMemberMapping.MemberCodeMappings.Add(
							new SourceCodeMapping {
								ILInstructionOffset = range,
								StartLocation = startLocation,
								EndLocation = output.Location,
								MemberMapping = currentMemberMapping
							});
					}
				}
			}
			
			
			if (node.Annotation<MemberMapping>() != null) {
				output.AddDebuggerMemberMapping(currentMemberMapping);
				currentMemberMapping = parentMemberMappings.Pop();
			}
		}
		
		private bool IsIconMapping()
		{
			if (nodeStack == null || nodeStack.Count == 0)
				return false;
			
			var node = nodeStack.Peek();
			
			return 
				node is FieldDeclaration ||
				node is ConstructorDeclaration ||
				node is EventDeclaration ||
				node is DestructorDeclaration ||
				node is DelegateDeclaration ||
				node is OperatorDeclaration||
				node is MemberDeclaration ||
				node is TypeDeclaration;
		}
	}
}
