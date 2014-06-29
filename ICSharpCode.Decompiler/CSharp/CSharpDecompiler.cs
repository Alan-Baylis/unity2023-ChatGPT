﻿using ICSharpCode.Decompiler.IL;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using ICSharpCode.NRefactory.Utils;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ICSharpCode.Decompiler.CSharp
{
	public class CSharpDecompiler
	{
		CecilLoader cecilLoader = new CecilLoader { IncludeInternalMembers = true, LazyLoad = true };
		Dictionary<IUnresolvedEntity, MemberReference> entityDict = new Dictionary<IUnresolvedEntity, MemberReference>();
		ICompilation compilation;
		ITypeResolveContext mainAssemblyTypeResolveContext;
		TypeSystemAstBuilder typeSystemAstBuilder;
		StatementBuilder statementBuilder;

		public CancellationToken CancellationToken { get; set; }

		public CSharpDecompiler(ModuleDefinition module)
		{
			cecilLoader.OnEntityLoaded = (entity, mr) => {
				// entityDict needs locking because the type system is multi-threaded and may be accessed externally
				lock (entityDict)
					entityDict[entity] = mr;
			};

			IUnresolvedAssembly mainAssembly = cecilLoader.LoadModule(module);
			var referencedAssemblies = new List<IUnresolvedAssembly>();
			foreach (var asmRef in module.AssemblyReferences) {
				var asm = module.AssemblyResolver.Resolve(asmRef);
				if (asm != null)
					referencedAssemblies.Add(cecilLoader.LoadAssembly(asm));
			}
			compilation = new SimpleCompilation(mainAssembly, referencedAssemblies);
			mainAssemblyTypeResolveContext = new SimpleTypeResolveContext(compilation.MainAssembly);

			typeSystemAstBuilder = new TypeSystemAstBuilder();
			typeSystemAstBuilder.AlwaysUseShortTypeNames = true;
			typeSystemAstBuilder.AddAnnotations = true;

			statementBuilder = new StatementBuilder();
		}

		MemberReference GetMemberReference(IMember member)
		{
			var unresolved = member.UnresolvedMember;
			lock (entityDict) {
				if (unresolved != null && entityDict.TryGetValue(unresolved, out var mr))
					return mr;
			}
			return null;
		}

		ITypeDefinition GetTypeDefinition(TypeDefinition typeDef)
		{
			return compilation.MainAssembly.GetTypeDefinition(typeDef.GetFullTypeName());
		}

		IMethod GetMethod(MethodDefinition methodDef)
		{
			ITypeDefinition typeDef = GetTypeDefinition(methodDef.DeclaringType);
			if (typeDef == null)
				return null;
			return typeDef.Methods.FirstOrDefault(m => GetMemberReference(m) == methodDef);
		}

		public EntityDeclaration Decompile(MethodDefinition methodDefinition)
		{
			if (methodDefinition == null)
				throw new ArgumentNullException("methodDefinition");
			var method = GetMethod(methodDefinition);
			if (method == null)
				throw new InvalidOperationException("Could not find method in NR type system");
			var entityDecl = typeSystemAstBuilder.ConvertEntity(method);
			if (methodDefinition.HasBody) {
				var ilReader = new ILReader(methodDefinition.Body, CancellationToken);
				var inst = ilReader.CreateBlocks(true);
				var body = statementBuilder.Convert(inst);
				var bodyBlock = body as BlockStatement ?? new BlockStatement { body };
				entityDecl.AddChild(bodyBlock, Roles.Body);
			}
			return entityDecl;
		}
	}
}
