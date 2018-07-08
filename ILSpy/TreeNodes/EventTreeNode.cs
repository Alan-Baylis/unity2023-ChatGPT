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
using System.Linq;
using System.Reflection;
using SRM = System.Reflection.Metadata;
using System.Windows.Media;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata;

namespace ICSharpCode.ILSpy.TreeNodes
{
	/// <summary>
	/// Represents an event in the TreeView.
	/// </summary>
	public sealed class EventTreeNode : ILSpyTreeNode, IMemberTreeNode
	{
		public EventTreeNode(IEvent @event)
		{
			this.EventDefinition = @event ?? throw new ArgumentNullException(nameof(@event));
			if (@event.CanAdd)
				this.Children.Add(new MethodTreeNode(@event.AddAccessor));
			if (@event.CanRemove)
				this.Children.Add(new MethodTreeNode(@event.RemoveAccessor));
			if (@event.CanInvoke)
				this.Children.Add(new MethodTreeNode(@event.InvokeAccessor));
			//foreach (var m in ev.OtherMethods)
			//	this.Children.Add(new MethodTreeNode(m));
		}

		public IEvent EventDefinition { get; }

		public override object Text => GetText(EventDefinition, this.Language) + EventDefinition.MetadataToken.ToSuffixString();

		public static object GetText(IEvent ev, Language language)
		{
			return language.EventToString(ev, false, false);
		}

		public override object Icon => GetIcon(EventDefinition);

		public static ImageSource GetIcon(IEvent @event)
		{
			var metadata = ((MetadataAssembly)@event.ParentAssembly).PEFile.Metadata;
			var accessor = metadata.GetEventDefinition((EventDefinitionHandle)@event.MetadataToken).GetAccessors().GetAny();
			if (!accessor.IsNil) {
				var accessorMethod = metadata.GetMethodDefinition(accessor);
				return Images.GetIcon(MemberIcon.Event, GetOverlayIcon(accessorMethod.Attributes), accessorMethod.HasFlag(MethodAttributes.Static));
			}
			return Images.GetIcon(MemberIcon.Event, AccessOverlayIcon.Public, false);
		}

		private static AccessOverlayIcon GetOverlayIcon(MethodAttributes methodAttributes)
		{
			switch (methodAttributes & MethodAttributes.MemberAccessMask) {
				case MethodAttributes.Public:
					return AccessOverlayIcon.Public;
				case MethodAttributes.Assembly:
					return AccessOverlayIcon.Internal;
				case MethodAttributes.FamANDAssem:
					return AccessOverlayIcon.PrivateProtected;
				case MethodAttributes.Family:
					return AccessOverlayIcon.Protected;
				case MethodAttributes.FamORAssem:
					return AccessOverlayIcon.ProtectedInternal;
				case MethodAttributes.Private:
					return AccessOverlayIcon.Private;
				case 0:
					return AccessOverlayIcon.CompilerControlled;
				default:
					throw new NotSupportedException();
			}
		}

		public override FilterResult Filter(FilterSettings settings)
		{
			if (!settings.ShowInternalApi && !IsPublicAPI)
				return FilterResult.Hidden;
			if (settings.SearchTermMatches(EventDefinition.Name) && settings.Language.ShowMember(EventDefinition))
				return FilterResult.Match;
			else
				return FilterResult.Hidden;
		}
		
		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			language.DecompileEvent(EventDefinition, output, options);
		}
		
		public override bool IsPublicAPI {
			get {
				switch (EventDefinition.Accessibility) {
					case Accessibility.Public:
					case Accessibility.ProtectedOrInternal:
					case Accessibility.Protected:
						return true;
					default:
						return false;
				}
			}
		}

		IEntity IMemberTreeNode.Member => EventDefinition;
	}
}
