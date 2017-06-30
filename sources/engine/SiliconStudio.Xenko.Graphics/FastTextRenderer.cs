﻿// Copyright (c) 2017 Silicon Studio Corp. All rights reserved. (https://www.siliconstudio.co.jp)
// See LICENSE.md for full license information.

using System;
using SiliconStudio.Core;
using SiliconStudio.Core.Annotations;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Xenko.Rendering;

namespace SiliconStudio.Xenko.Graphics
{
        /// <summary>
        /// Renders text with a fixed size font.
        /// </summary>
        public class FastTextRenderer : ComponentBase
        {
            private const int VertexBufferCount = 3;

            private const int IndexStride = sizeof(int);

            private Buffer[] vertexBuffers;
            private int activeVertexBufferIndex;
            private VertexBufferBinding[] vertexBuffersBinding;
            private MappedResource mappedVertexBuffer;
            private IntPtr mappedVertexBufferPointer;

            private Buffer indexBuffer;
            private IndexBufferBinding indexBufferBinding;

            private MutablePipelineState pipelineState;
            private EffectInstance simpleEffect;

            private InputElementDescription[][] inputElementDescriptions;

            private int charsToRenderCount;

            public static FastTextRenderer New([NotNull] GraphicsContext graphicsContext)
            {
                return new FastTextRenderer().Initialize(graphicsContext);
            }

            public FastTextRenderer()
            {
                
            }

            public FastTextRenderer([NotNull] GraphicsContext graphicsContext)
            {
                Initialize(graphicsContext);
            }

            protected override void Destroy()
            {
                for (int i = 0; i < VertexBufferCount; i++)
                    vertexBuffers[i].Dispose();

                activeVertexBufferIndex = -1;

                mappedVertexBufferPointer = IntPtr.Zero;

                if (indexBuffer != null)
                {
                    indexBuffer.Dispose();
                    indexBuffer = null;
                }

                indexBufferBinding = null;
                pipelineState = null;

                if (simpleEffect != null)
                {
                    simpleEffect.Dispose();
                    simpleEffect = null;
                }

                for (int i = 0; i < VertexBufferCount; i++)
                    inputElementDescriptions[i] = null;

                charsToRenderCount = -1;

                base.Destroy();
            }

            /// <summary>
            /// Initializes a FastTextRendering instance (create and build required ressources, ...).
            /// </summary>
            /// <param name="graphicsContext">The current GraphicsContext.</param>
            public unsafe FastTextRenderer Initialize([NotNull] GraphicsContext graphicsContext)
            {
                var indexBufferSize = MaxCharactersPerLine * MaxCharactersLines * 6 * sizeof(int);
                var indexBufferLength = indexBufferSize / IndexStride;

                // Map and build the indice buffer
                indexBuffer = graphicsContext.Allocator.GetTemporaryBuffer(new BufferDescription(indexBufferSize, BufferFlags.IndexBuffer, GraphicsResourceUsage.Dynamic));

                var mappedIndices = graphicsContext.CommandList.MapSubresource(indexBuffer, 0, MapMode.WriteNoOverwrite, false, 0, indexBufferSize);
                var indexPointer = mappedIndices.DataBox.DataPointer;

                var i = 0;
                for (var c = 0; c < MaxCharactersPerLine * MaxCharactersLines; c++)
                {
                    *(int*)(indexPointer + IndexStride * i++) = c * 4 + 0;
                    *(int*)(indexPointer + IndexStride * i++) = c * 4 + 1;
                    *(int*)(indexPointer + IndexStride * i++) = c * 4 + 2;

                    *(int*)(indexPointer + IndexStride * i++) = c * 4 + 1;
                    *(int*)(indexPointer + IndexStride * i++) = c * 4 + 3;
                    *(int*)(indexPointer + IndexStride * i++) = c * 4 + 2;
                }

                graphicsContext.CommandList.UnmapSubresource(mappedIndices);

                indexBufferBinding = new IndexBufferBinding(Buffer.Index.New(graphicsContext.CommandList.GraphicsDevice, new DataPointer(indexPointer, indexBufferSize)), true, indexBufferLength);

                // Create vertex buffers
                var vertexBufferLength = MaxCharactersPerLine * MaxCharactersLines * 4;

                vertexBuffers = new Buffer[VertexBufferCount];
                for (int j = 0; j < VertexBufferCount; j++)
                    vertexBuffers[j] = Buffer.Vertex.New(graphicsContext.CommandList.GraphicsDevice, new VertexPositionNormalTexture[vertexBufferLength], GraphicsResourceUsage.Dynamic);

                vertexBuffersBinding = new VertexBufferBinding[VertexBufferCount];
                for (int j = 0; j < VertexBufferCount; j++)
                    vertexBuffersBinding[j] = new VertexBufferBinding(vertexBuffers[j], VertexPositionNormalTexture.Layout, 0);


                inputElementDescriptions = new InputElementDescription[VertexBufferCount][];
                for (int j = 0; j < VertexBufferCount; j++)
                    inputElementDescriptions[j] = vertexBuffersBinding[j].Declaration.CreateInputElements();

                // Create the pipeline state object
                pipelineState = new MutablePipelineState(graphicsContext.CommandList.GraphicsDevice);
                pipelineState.State.SetDefaults();
                pipelineState.State.InputElements = inputElementDescriptions[0];
                pipelineState.State.PrimitiveType = PrimitiveType.TriangleList;

                // Create the effect
                simpleEffect = new EffectInstance(new Effect(graphicsContext.CommandList.GraphicsDevice, SpriteEffect.Bytecode));
                simpleEffect.Parameters.Set(SpriteBaseKeys.MatrixTransform, Matrix.Identity);
                simpleEffect.Parameters.Set(TexturingKeys.Texture0, DebugSpriteFont);
                simpleEffect.Parameters.Set(TexturingKeys.Sampler, graphicsContext.CommandList.GraphicsDevice.SamplerStates.LinearClamp);
                simpleEffect.Parameters.Set(SpriteEffectKeys.Color, TextColor);

                simpleEffect.UpdateEffect(graphicsContext.CommandList.GraphicsDevice);

                return this;
            }

            /// <summary>
            /// Begins text rendering (swaps and maps the vertex buffer to write to).
            /// </summary>
            /// <param name="graphicsContext">The current GraphicsContext.</param>
            public void Begin([NotNull] GraphicsContext graphicsContext)
            {
                // Swap vertex buffer
                activeVertexBufferIndex = ++activeVertexBufferIndex >= VertexBufferCount ? 0 : activeVertexBufferIndex;

                charsToRenderCount = 0;

                // Map the vertex buffer to write to
                mappedVertexBuffer = graphicsContext.CommandList.MapSubresource(vertexBuffers[activeVertexBufferIndex], 0, MapMode.WriteDiscard);
                mappedVertexBufferPointer = mappedVertexBuffer.DataBox.DataPointer;
            }

            /// <summary>
            /// Begins text rendering (swaps and maps the vertex buffer to write to).
            /// </summary>
            /// <param name="graphicsContext">The current GraphicsContext.</param>
            public void End([NotNull] GraphicsContext graphicsContext)
            {
                if (graphicsContext == null) throw new ArgumentNullException(nameof(graphicsContext));

                // Unmap the vertex buffer
                graphicsContext.CommandList.UnmapSubresource(mappedVertexBuffer);
                mappedVertexBufferPointer = IntPtr.Zero;

                // Update pipeline state
                pipelineState.State.SetDefaults();
                pipelineState.State.RootSignature = simpleEffect.RootSignature;
                pipelineState.State.EffectBytecode = simpleEffect.Effect.Bytecode;
                pipelineState.State.DepthStencilState = DepthStencilStates.None;
                pipelineState.State.BlendState = BlendStates.AlphaBlend;
                pipelineState.State.Output.CaptureState(graphicsContext.CommandList);
                pipelineState.State.InputElements = inputElementDescriptions[activeVertexBufferIndex];
                pipelineState.Update();

                graphicsContext.CommandList.SetPipelineState(pipelineState.CurrentState);

                // Update effect
                simpleEffect.UpdateEffect(graphicsContext.CommandList.GraphicsDevice);
                simpleEffect.Apply(graphicsContext);

                // Bind and draw
                graphicsContext.CommandList.SetVertexBuffer(0, vertexBuffersBinding[activeVertexBufferIndex].Buffer, vertexBuffersBinding[activeVertexBufferIndex].Offset, vertexBuffersBinding[activeVertexBufferIndex].Stride);
                graphicsContext.CommandList.SetIndexBuffer(indexBufferBinding.Buffer, 0, indexBufferBinding.Is32Bit);

                graphicsContext.CommandList.DrawIndexed(charsToRenderCount * 6);
            }

            /// <summary>
            /// Adds a string to be drawn once End() is called.
            /// </summary>
            /// <param name="graphicsContext">The current GraphicsContext.</param>
            /// <param name="text">The text to draw.</param>
            /// <param name="x">Position of the text on the X axis (in viewport space).</param>
            /// <param name="y">Position of the text on the Y axis (in viewport space).</param>
            public unsafe void DrawString(GraphicsContext graphicsContext, string text, int x, int y)
            {
                var target = graphicsContext.CommandList.RenderTarget;

                var renderInfos = new RectangleF(x, y, target.ViewWidth, target.ViewHeight);
                var constantInfos = new RectangleF(GlyphWidth, GlyphHeight, DebugSpriteWidth, DebugSpriteHeight);

                var textLength = text.Length;
                var textLengthPointer = new IntPtr(&textLength);

                Native.NativeInvoke.xnGraphicsFastTextRendererGenerateVertices(constantInfos, renderInfos, text, out textLengthPointer, out mappedVertexBufferPointer);

                charsToRenderCount += *(int*)textLengthPointer.ToPointer();
            }

            /// <summary>
            /// Sets or gets the color to use when drawing the text.
            /// </summary>
            public Color4 TextColor { get; set; } = Color.LightGreen;

            /// <summary>
            /// Sets or gets the sprite font texture to use when drawing the text.
            /// The sprite font must have fixed size of <see cref="DebugSpriteWidth"/> and <see cref="DebugSpriteHeight"/>
            /// and each glyph should have uniform size of <see cref="GlyphWidth"/> and <see cref="GlyphHeight"/>
            /// </summary>
            public Texture DebugSpriteFont { get; set; }

            /// <summary>
            /// Width of a single glyph of font <see cref="DebugSpriteFont"/>.
            /// </summary>
            public int GlyphWidth { get; set; } = 8;

            /// <summary>
            /// Height of a single glyph of font <see cref="DebugSpriteFont"/>.
            /// </summary>
            public int GlyphHeight { get; set; } = 16;

            /// <summary>
            /// Width of font Texture <see cref="DebugSpriteFont"/>.
            /// </summary>
            public int DebugSpriteWidth { get; set; } = 256;

            /// <summary>
            /// Height of font Texture <see cref="DebugSpriteFont"/>.
            /// </summary>
            public int DebugSpriteHeight { get; set; } = 64;

            public int MaxCharactersPerLine { get; set; } = 240;

            public int MaxCharactersLines { get; set; } = 65;
        }
}
