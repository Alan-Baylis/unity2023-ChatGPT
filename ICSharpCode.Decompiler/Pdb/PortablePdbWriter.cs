﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp.TypeSystem;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.Pdb
{
	public class PortablePdbWriter
	{
		public static readonly Guid CSharpLanguageGuid = new Guid("3f5162f8-07c6-11d3-9053-00c04fa302a1");

		public static readonly Guid DebugInfoEmbeddedSource = new Guid("0e8a571b-6926-466e-b4ad-8ab04611f5fe");

		public static readonly Guid HashAlgorithmSHA1 = new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460");
		public static readonly Guid HashAlgorithmSHA256 = new Guid("8829d00f-11b8-4213-878b-770e8597ac16");
		static readonly FileVersionInfo decompilerVersion = FileVersionInfo.GetVersionInfo(typeof(CSharpDecompiler).Assembly.Location);


		public static void WritePdb(PEFile file, CSharpDecompiler decompiler, DecompilerSettings settings, Stream targetStream)
		{
			MetadataBuilder metadata = new MetadataBuilder();
			MetadataReader reader = file.Metadata;
			var entrypointHandle = MetadataTokens.MethodDefinitionHandle(file.Reader.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress);

			var hasher = SHA256.Create();
			var sequencePointBlobs = new Dictionary<MethodDefinitionHandle, (DocumentHandle Document, BlobHandle SequencePoints)>();
			var importScopeBlobs = new Dictionary<MethodDefinitionHandle, (DocumentHandle Document, BlobHandle ImportScope)>();
			var emptyList = new List<Metadata.SequencePoint>();

			foreach (var handle in reader.GetTopLevelTypeDefinitions()) {
				var type = reader.GetTypeDefinition(handle);
				var name = metadata.GetOrAddDocumentName("ILSpy_Generated_" + type.GetFullTypeName(reader) + "_" + Guid.NewGuid() + ".cs");
				var ast = decompiler.DecompileTypes(new[] { handle });
				ast.InsertChildAfter(null, new Comment(" PDB and source generated by ICSharpCode.Decompiler " + decompilerVersion.FileVersion), Roles.Comment);
				var sourceText = SyntaxTreeToString(ast, settings);
				var sequencePoints = decompiler.CreateSequencePoints(ast).ToDictionary(sp => (MethodDefinitionHandle)sp.Key.Method.MetadataToken, sp => sp.Value);
				var sourceCheckSum = hasher.ComputeHash(Encoding.UTF8.GetBytes(sourceText));
				var sourceBlob = WriteSourceToBlob(metadata, sourceText);

				var document = metadata.AddDocument(name,
					hashAlgorithm: metadata.GetOrAddGuid(HashAlgorithmSHA256),
					hash: metadata.GetOrAddBlob(sourceCheckSum),
					language: metadata.GetOrAddGuid(CSharpLanguageGuid));

				metadata.AddCustomDebugInformation(document, metadata.GetOrAddGuid(DebugInfoEmbeddedSource), sourceBlob);

				foreach (var method in type.GetMethods()) {
					var methodDef = reader.GetMethodDefinition(method);
					if (!sequencePoints.TryGetValue(method, out var points))
						points = emptyList;
					int localSignatureRowId;
					MethodBodyBlock methodBody;
					if (methodDef.RelativeVirtualAddress != 0) {
						methodBody = file.Reader.GetMethodBody(methodDef.RelativeVirtualAddress);
						localSignatureRowId = methodBody.LocalSignature.IsNil ? 0 : MetadataTokens.GetRowNumber(methodBody.LocalSignature);
					} else {
						methodBody = null;
						localSignatureRowId = 0;
					}
					if (points.Count == 0)
						sequencePointBlobs.Add(method, (default, default));
					else
						sequencePointBlobs.Add(method, (document, EncodeSequencePoints(metadata, localSignatureRowId, points)));
					importScopeBlobs.Add(method, (document, EncodeImportScope(metadata, reader, ast, decompiler.TypeSystem.Compilation)));
				}

				foreach (var nestedTypeHandle in type.GetNestedTypes()) {
					var nestedType = reader.GetTypeDefinition(nestedTypeHandle);

					foreach (var method in nestedType.GetMethods()) {

					}
				}
			}

			foreach (var method in reader.MethodDefinitions) {
				if (sequencePointBlobs.TryGetValue(method, out var info)) {
					metadata.AddMethodDebugInformation(info.Document, info.SequencePoints);
					//metadata.AddMethodDebugInformation(default, default);
				} else {
					metadata.AddMethodDebugInformation(default, default);
				}
				if (importScopeBlobs.TryGetValue(method, out var scopeInfo)) {
					//metadata.AddImportScope(default, scopeInfo.ImportScope);
					metadata.AddMethodDebugInformation(default, default);
				} else {
					metadata.AddImportScope(default, default);
				}
			}
			var debugDir = file.Reader.ReadDebugDirectory().FirstOrDefault(dir => dir.IsPortableCodeView);
			var portable = file.Reader.ReadCodeViewDebugDirectoryData(debugDir);
			var contentId = new BlobContentId(portable.Guid, debugDir.Stamp);
			PortablePdbBuilder serializer = new PortablePdbBuilder(metadata, GetRowCounts(reader), entrypointHandle, blobs => contentId);
			BlobBuilder blobBuilder = new BlobBuilder();
			serializer.Serialize(blobBuilder);
			blobBuilder.WriteContentTo(targetStream);
		}

		static BlobHandle WriteSourceToBlob(MetadataBuilder metadata, string sourceText)
		{
			var builder = new BlobBuilder();
			builder.WriteInt32(0); // uncompressed
			builder.WriteUTF8(sourceText);

			return metadata.GetOrAddBlob(builder);
		}

		static BlobHandle EncodeImportScope(MetadataBuilder metadata, MetadataReader reader, SyntaxTree ast, ICompilation compilation)
		{
			var scope = ast.Annotation<UsingScope>()?.Resolve(compilation);

			if (scope == null)
				return default;

			Dictionary<IAssembly, AssemblyReferenceHandle> assemblyReferences = new Dictionary<IAssembly, AssemblyReferenceHandle>();

			var writer = new BlobBuilder();

			foreach (var import in scope.Usings) {
				foreach (var asm in import.ContributingAssemblies) {
					if (asm == compilation.MainAssembly) {
						writer.WriteByte(1);
						writer.WriteCompressedInteger(MetadataTokens.GetHeapOffset(metadata.GetOrAddBlobUTF8(import.FullName)));
					} else {
						writer.WriteByte(2);
						if (!assemblyReferences.TryGetValue(asm, out var referenceHandle)) {
							foreach (var h in reader.AssemblyReferences) {
								var reference = reader.GetAssemblyReference(h);
								string asmName = reader.GetString(reference.Name);
								if (asmName == asm.AssemblyName) {
									assemblyReferences.Add(asm, referenceHandle = h);
								}
							}
						}
						Debug.Assert(!referenceHandle.IsNil);
						writer.WriteCompressedInteger(MetadataTokens.GetRowNumber(referenceHandle));
						writer.WriteCompressedInteger(MetadataTokens.GetHeapOffset(metadata.GetOrAddBlobUTF8(import.FullName)));
					}
				}
			}

			return metadata.GetOrAddBlob(writer);
		}

		static BlobHandle EncodeSequencePoints(MetadataBuilder metadata, int localSignatureRowId, List<Metadata.SequencePoint> sequencePoints)
		{
			if (sequencePoints.Count == 0)
				return default;
			var writer = new BlobBuilder();
			// header:
			writer.WriteCompressedInteger(localSignatureRowId);

			int previousOffset = -1;
			int previousStartLine = -1;
			int previousStartColumn = -1;

			for (int i = 0; i < sequencePoints.Count; i++) {
				var sequencePoint = sequencePoints[i];
				// delta IL offset:
				if (i > 0)
					writer.WriteCompressedInteger(sequencePoint.Offset - previousOffset);
				else
					writer.WriteCompressedInteger(sequencePoint.Offset);
				previousOffset = sequencePoint.Offset;

				if (sequencePoint.IsHidden) {
					writer.WriteInt16(0);
					continue;
				}

				int lineDelta = sequencePoint.EndLine - sequencePoint.StartLine;
				int columnDelta = sequencePoint.EndColumn - sequencePoint.StartColumn;

				writer.WriteCompressedInteger(lineDelta);

				if (lineDelta == 0) {
					writer.WriteCompressedInteger(columnDelta);
				} else {
					writer.WriteCompressedSignedInteger(columnDelta);
				}

				if (previousStartLine < 0) {
					writer.WriteCompressedInteger(sequencePoint.StartLine);
					writer.WriteCompressedInteger(sequencePoint.StartColumn);
				} else {
					writer.WriteCompressedSignedInteger(sequencePoint.StartLine - previousStartLine);
					writer.WriteCompressedSignedInteger(sequencePoint.StartColumn - previousStartColumn);
				}

				previousStartLine = sequencePoint.StartLine;
				previousStartColumn = sequencePoint.StartColumn;
			}

			return metadata.GetOrAddBlob(writer);
		}

		static ImmutableArray<int> GetRowCounts(MetadataReader reader)
		{
			var builder = ImmutableArray.CreateBuilder<int>(MetadataTokens.TableCount);
			for (int i = 0; i < MetadataTokens.TableCount; i++) {
				builder.Add(reader.GetTableRowCount((TableIndex)i));
			}

			return builder.MoveToImmutable();
		}

		static string SyntaxTreeToString(SyntaxTree syntaxTree, DecompilerSettings settings)
		{
			StringWriter w = new StringWriter();
			TokenWriter tokenWriter = new TextWriterTokenWriter(w);
			syntaxTree.AcceptVisitor(new InsertParenthesesVisitor { InsertParenthesesForReadability = true });
			tokenWriter = TokenWriter.WrapInWriterThatSetsLocationsInAST(tokenWriter);
			syntaxTree.AcceptVisitor(new CSharpOutputVisitor(tokenWriter, settings.CSharpFormattingOptions));
			return w.ToString();
		}
	}
}
