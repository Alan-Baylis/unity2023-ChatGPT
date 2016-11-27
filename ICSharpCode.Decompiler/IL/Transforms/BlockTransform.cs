﻿using System.Collections.Generic;
using System.Linq;
using ICSharpCode.Decompiler.FlowAnalysis;
using ICSharpCode.Decompiler.IL.ControlFlow;

namespace ICSharpCode.Decompiler.IL.Transforms
{
	/// <summary>
	/// Per-block IL transform.
	/// </summary>
	public interface IBlockTransform
	{
		void Run(Block block, BlockTransformContext context);
	}

	/// <summary>
	/// Parameter class holding various arguments for <see cref="IBlockTransform.Run"/>.
	/// </summary>
	public class BlockTransformContext : ILTransformContext
	{
		/// <summary>
		/// The function containing the block currently being processed.
		/// </summary>
		public ILFunction Function { get; set; }

		/// <summary>
		/// The block to process.
		/// </summary>
		/// <remarks>
		/// Should be identical to the <c>block</c> parameter to <c>IBlockTransform.Run</c>.
		/// </remarks>
		public Block Block { get; set; }

		/// <summary>
		/// The control flow node corresponding to the block being processed.
		/// </summary>
		/// <remarks>
		/// Identical to <c>ControlFlowGraph.GetNode(Block)</c>.
		/// Note: the control flow graph is not up-to-date, but was created at the start of the
		/// block transforms (before loop detection).
		/// </remarks>
		public ControlFlowNode ControlFlowNode { get; set; }

		/// <summary>
		/// Gets the control flow graph.
		/// 
		/// Note: the control flow graph is not up-to-date, but was created at the start of the
		/// block transforms (before loop detection).
		/// </summary>
		public ControlFlowGraph ControlFlowGraph { get; set; }

		public BlockTransformContext(ILTransformContext context) : base(context)
		{
		}
	}


	/// <summary>
	/// IL transform that runs a list of per-block transforms.
	/// </summary>
	public class BlockILTransform : IILTransform
	{
		public IList<IBlockTransform> PreOrderTransforms { get; } = new List<IBlockTransform>();
		public IList<IBlockTransform> PostOrderTransforms { get; } = new List<IBlockTransform>();

		public void Run(ILFunction function, ILTransformContext context)
		{
			var blockContext = new BlockTransformContext(context);
			blockContext.Function = function;
			foreach (var container in function.Descendants.OfType<BlockContainer>().ToList()) {
				context.CancellationToken.ThrowIfCancellationRequested();
				blockContext.ControlFlowGraph = new ControlFlowGraph(container, context.CancellationToken);
				VisitBlock(blockContext.ControlFlowGraph.GetNode(container.EntryPoint), blockContext);
				// TODO: handle unreachable code?
			}
		}

		void VisitBlock(ControlFlowNode cfgNode, BlockTransformContext context)
		{
			Block block = (Block)cfgNode.UserData;
			context.Stepper.StartGroup(block.Label, block);

			context.ControlFlowNode = cfgNode;
			context.Block = block;
			block.RunTransforms(PreOrderTransforms, context);

			// First, process the children in the dominator tree.
			// The ConditionDetection transform requires dominated blocks to
			// be already processed.
			foreach (var child in cfgNode.DominatorTreeChildren) {
				VisitBlock(child, context);
			}

			context.ControlFlowNode = cfgNode;
			context.Block = block;
			block.RunTransforms(PostOrderTransforms, context);
			context.Stepper.EndGroup();
		}
	}
}
