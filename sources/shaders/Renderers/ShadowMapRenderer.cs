// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Collections.Generic;

using SiliconStudio.Core;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Paradox.DataModel;
using SiliconStudio.Paradox.Effects.Modules.Processors;
using SiliconStudio.Paradox.Effects.Modules.Shadowmap;
using SiliconStudio.Paradox.Engine;
using SiliconStudio.Paradox.EntityModel;
using SiliconStudio.Paradox.Graphics;

namespace SiliconStudio.Paradox.Effects.Modules.Renderers
{
    /// <summary>
    /// Handles shadow mapping.
    /// </summary>
    public class ShadowMapRenderer : RecursiveRenderer
    {
        #region Constants

        //TODO: dependant of the PostEffectVsmBlur shader. We should have a way to set everything at the same time
        private const float VsmBlurSize = 4.0f;

        #endregion

        #region Private static members

        /// <summary>
        /// Base points for frustrum corners.
        /// </summary>
        private static readonly Vector3[] FrustrumBasePoints =
            {
                new Vector3(-1.0f,-1.0f,-1.0f), new Vector3(1.0f,-1.0f,-1.0f), new Vector3(-1.0f,1.0f,-1.0f), new Vector3(1.0f,1.0f,-1.0f),
                new Vector3(-1.0f,-1.0f, 1.0f), new Vector3(1.0f,-1.0f, 1.0f), new Vector3(-1.0f,1.0f, 1.0f), new Vector3(1.0f,1.0f, 1.0f),
            };

        /// <summary>
        /// The various UP vectors to try.
        /// </summary>
        private static readonly Vector3[] VectorUps = new[] { Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX };

        internal static readonly ParameterKey<ShadowMapReceiverInfo[]> Receivers = ParameterKeys.New(new ShadowMapReceiverInfo[1]);
        internal static readonly ParameterKey<ShadowMapReceiverVsmInfo[]> ReceiversVsm = ParameterKeys.New(new ShadowMapReceiverVsmInfo[1]);
        internal static readonly ParameterKey<ShadowMapCascadeReceiverInfo[]> LevelReceivers = ParameterKeys.New(new ShadowMapCascadeReceiverInfo[1]);
        internal static readonly ParameterKey<int> ShadowMapLightCount = ParameterKeys.New(0);
        
        #endregion

        #region Private members

        // Storage for temporary variables
        private Vector3[] points = new Vector3[8];
        private Vector3[] directions = new Vector3[4];

        // VSM Blur quads
        private PostEffectQuad vsmHorizontalBlurQuad, vsmVerticalBlurQuad;

        // rectangles to blur for each shadow map
        private HashSet<ShadowMapTexture> shadowMapTexturesToBlur = new HashSet<ShadowMapTexture>();

        #endregion

        #region Constructor

        public ShadowMapRenderer(IServiceRegistry services, RenderPipeline recursivePipeline) : base(services, recursivePipeline)
        {
            // Build blur effects for VSM
            var vsmHorizontalBlur = EffectSystem.LoadEffect("HorizontalVsmBlur");
            var vsmVerticalBlur = EffectSystem.LoadEffect("VerticalVsmBlur");

            vsmHorizontalBlurQuad = new PostEffectQuad(GraphicsDevice, vsmHorizontalBlur);
            vsmVerticalBlurQuad = new PostEffectQuad(GraphicsDevice, vsmVerticalBlur);
        }

        #endregion

        #region Methods

        protected override void Render(RenderContext context)
        {
            // get the lightprocessor
            var entitySystem = Services.GetServiceAs<EntitySystem>();
            var lightProcessor = entitySystem.GetProcessor<LightShadowProcessor>();
            if (lightProcessor == null)
                return;

            var graphicsDevice = context.GraphicsDevice;

            // Get View and Projection matrices
            Matrix view, projection;
            Pass.Parameters.Get(TransformationKeys.View, out view);
            Pass.Parameters.Get(TransformationKeys.Projection, out projection);

            // Clear shadow map textures
            foreach (var shadowMapTexture in lightProcessor.ActiveShadowMapTextures)
            {
                // Clear and set render target
                graphicsDevice.Clear(shadowMapTexture.ShadowMapDepthBuffer, DepthStencilClearOptions.DepthBuffer);

                if (shadowMapTexture.IsVarianceShadowMap)
                    graphicsDevice.Clear(shadowMapTexture.ShadowMapRenderTarget, Color4.White);

                // Reset shadow map allocator
                shadowMapTexture.GuillotinePacker.Clear(shadowMapTexture.ShadowMapDepthTexture.Width, shadowMapTexture.ShadowMapDepthTexture.Height);
            }

            ShadowMapFilterType filterBackup;
            var hasFilter = graphicsDevice.Parameters.ContainsKey(ShadowMapParameters.FilterType);
            filterBackup = graphicsDevice.Parameters.Get(ShadowMapParameters.FilterType);

            if (lightProcessor.ActiveShadowMaps.Count > 0)
            {
                // Compute frustum-dependent variables (common for all shadow maps)
                Matrix inverseView, inverseProjection;
                Matrix.Invert(ref projection, out inverseProjection);
                Matrix.Invert(ref view, out inverseView);

                // Transform Frustum corners in View Space (8 points) - algorithm is valid only if the view matrix does not do any kind of scale/shear transformation
                for (int i = 0; i < 8; ++i)
                    Vector3.TransformCoordinate(ref FrustrumBasePoints[i], ref inverseProjection, out points[i]);

                // Compute frustum edge directions
                for (int i = 0; i < 4; i++)
                    directions[i] = Vector3.Normalize(points[i + 4] - points[i]);

                // Prepare and render shadow maps
                foreach (var shadowMap in lightProcessor.ActiveShadowMaps)
                {
                    Vector3.Normalize(ref shadowMap.LightDirection, out shadowMap.LightDirectionNormalized);

                    // Compute shadow map infos
                    ComputeShadowMap(shadowMap, ref inverseView);

                    if (shadowMap.Filter == ShadowMapFilterType.Variance)
                        graphicsDevice.SetRenderTarget(shadowMap.Texture.ShadowMapDepthBuffer, shadowMap.Texture.ShadowMapRenderTarget);
                    else
                        graphicsDevice.SetRenderTarget(shadowMap.Texture.ShadowMapDepthBuffer);

                    // set layers
                    ActiveLayersBackup = context.ActiveLayers;
                    context.ActiveLayers = shadowMap.Layers;

                    // Render each cascade
                    for (int i = 0; i < shadowMap.CascadeCount; ++i)
                    {
                        var cascade = shadowMap.Cascades[i];

                        // Override with current shadow map parameters
                        graphicsDevice.Parameters.Set(ShadowMapKeys.DistanceMax, shadowMap.ShadowFarDistance);
                        graphicsDevice.Parameters.Set(LightKeys.LightDirection, shadowMap.LightDirectionNormalized);
                        graphicsDevice.Parameters.Set(ShadowMapCasterBaseKeys.shadowLightOffset, cascade.ReceiverInfo.Offset);

                        // We computed ViewProjection, so let's use View = Identity & Projection = ViewProjection
                        // (ideally we should override ViewProjection dynamic)
                        graphicsDevice.Parameters.Set(TransformationKeys.View, Matrix.Identity);
                        graphicsDevice.Parameters.Set(TransformationKeys.Projection, cascade.ViewProjCaster);

                        // Prepare viewport
                        var cascadeTextureCoord = cascade.CascadeTextureCoords;
                        var viewPortCoord = new Vector4(
                            cascadeTextureCoord.X * shadowMap.Texture.ShadowMapDepthTexture.Width,
                            cascadeTextureCoord.Y * shadowMap.Texture.ShadowMapDepthTexture.Height,
                            cascadeTextureCoord.Z * shadowMap.Texture.ShadowMapDepthTexture.Width,
                            cascadeTextureCoord.W * shadowMap.Texture.ShadowMapDepthTexture.Height);

                        // Set viewport
                        graphicsDevice.SetViewport(new Viewport((int)viewPortCoord.X, (int)viewPortCoord.Y, (int)(viewPortCoord.Z - viewPortCoord.X), (int)(viewPortCoord.W - viewPortCoord.Y)));

                        if (shadowMap.Filter == ShadowMapFilterType.Variance)
                            shadowMapTexturesToBlur.Add(shadowMap.Texture);
                        
                        graphicsDevice.Parameters.Set(ShadowMapParameters.FilterType, shadowMap.Filter);
                        base.Render(context);
                    }

                    // reset layers
                    context.ActiveLayers = ActiveLayersBackup;
                }

                // Reset parameters
                graphicsDevice.Parameters.Reset(ShadowMapKeys.DistanceMax);
                graphicsDevice.Parameters.Reset(LightKeys.LightDirection);
                graphicsDevice.Parameters.Reset(ShadowMapCasterBaseKeys.shadowLightOffset);
                graphicsDevice.Parameters.Reset(TransformationKeys.View);
                graphicsDevice.Parameters.Reset(TransformationKeys.Projection);
                if (hasFilter)
                    graphicsDevice.Parameters.Set(ShadowMapParameters.FilterType, filterBackup);
                else
                    graphicsDevice.Parameters.Reset(ShadowMapParameters.FilterType);
            }

            foreach (var shadowMap in shadowMapTexturesToBlur)
            {
                graphicsDevice.SetDepthStencilState(graphicsDevice.DepthStencilStates.None);
                graphicsDevice.SetRasterizerState(graphicsDevice.RasterizerStates.CullNone);
                graphicsDevice.SetRenderTarget(shadowMap.ShadowMapDepthBuffer, shadowMap.IntermediateBlurRenderTarget);
                vsmHorizontalBlurQuad.Draw(shadowMap.ShadowMapTargetTexture);
                graphicsDevice.SetRenderTarget(shadowMap.ShadowMapDepthBuffer, shadowMap.ShadowMapRenderTarget);
                vsmVerticalBlurQuad.Draw(shadowMap.IntermediateBlurTexture);
            }

            shadowMapTexturesToBlur.Clear();
        }

        private void ComputeShadowMap(ShadowMap shadowMap, ref Matrix inverseView)
        {
            float shadowDistribute = 1.0f / shadowMap.CascadeCount;
            float znear = shadowMap.ShadowNearDistance;
            float zfar = shadowMap.ShadowFarDistance;

            var boudingBoxVectors = new Vector3[8];
            var direction = Vector3.Normalize(shadowMap.LightDirectionNormalized);

            // Fake value
            // It will be setup by next loop
            Vector3 side = Vector3.UnitX;
            Vector3 up = Vector3.UnitX;

            // Select best Up vector
            // TODO: User preference?
            foreach (var vectorUp in VectorUps)
            {
                if (Vector3.Dot(direction, vectorUp) < (1.0 - 0.0001))
                {
                    side = Vector3.Normalize(Vector3.Cross(vectorUp, direction));
                    up = Vector3.Normalize(Vector3.Cross(direction, side));
                    break;
                }
            }

            // Prepare cascade list (allocate it if not done yet)
            var cascades = shadowMap.Cascades;
            if (cascades == null)
                cascades = shadowMap.Cascades = new ShadowMapCascadeInfo[shadowMap.CascadeCount];

            for (int cascadeLevel = 0; cascadeLevel < shadowMap.CascadeCount; ++cascadeLevel)
            {
                // Compute caster view and projection matrices
                var shadowMapView = Matrix.Zero;
                var shadowMapProjection = Matrix.Zero;
                if (shadowMap.LightType == LightType.Directional)
                {
                    // Compute cascade split (between znear and zfar)
                    float k0 = (float)(cascadeLevel + 0) / shadowMap.CascadeCount;
                    float k1 = (float)(cascadeLevel + 1) / shadowMap.CascadeCount;
                    float min = (float)(znear * Math.Pow(zfar / znear, k0)) * (1.0f - shadowDistribute) + (znear + (zfar - znear) * k0) * shadowDistribute;
                    float max = (float)(znear * Math.Pow(zfar / znear, k1)) * (1.0f - shadowDistribute) + (znear + (zfar - znear) * k1) * shadowDistribute;

                    // Compute frustum corners
                    for (int j = 0; j < 4; j++)
                    {
                        boudingBoxVectors[j * 2 + 0] = points[j] + directions[j] * min;
                        boudingBoxVectors[j * 2 + 1] = points[j] + directions[j] * max;
                    }
                    var boundingBox = BoundingBox.FromPoints(boudingBoxVectors);

                    // Compute bounding box center & radius
                    // Note: boundingBox is computed in view space so the computation of the radius is only correct when the view matrix does not do any kind of scale/shear transformation
                    var radius = (boundingBox.Maximum - boundingBox.Minimum).Length() * 0.5f;
                    var target = Vector3.TransformCoordinate(boundingBox.Center, inverseView);

                    // Snap camera to texel units (so that shadow doesn't jitter when light doesn't change direction but camera is moving)
                    var shadowMapHalfSize = shadowMap.ShadowMapSize * 0.5f;
                    float x = (float)Math.Ceiling(Vector3.Dot(target, up) * shadowMapHalfSize / radius) * radius / shadowMapHalfSize;
                    float y = (float)Math.Ceiling(Vector3.Dot(target, side) * shadowMapHalfSize / radius) * radius / shadowMapHalfSize;
                    float z = Vector3.Dot(target, direction);
                    //target = up * x + side * y + direction * R32G32B32_Float.Dot(target, direction);
                    target = up * x + side * y + direction * z;

                    // Compute caster view and projection matrices
                    shadowMapView = Matrix.LookAtRH(target - direction * zfar * 0.5f, target + direction * zfar * 0.5f, up); // View;
                    shadowMapProjection = Matrix.OrthoOffCenterRH(-radius, radius, -radius, radius, znear / zfar, zfar); // Projection
                }
                else if (shadowMap.LightType == LightType.Spot)
                {
                    shadowMapView = Matrix.LookAtRH(shadowMap.LightPosition, shadowMap.LightPosition + shadowMap.LightDirection, Vector3.UnitY);
                    shadowMapProjection = Matrix.PerspectiveFovRH(shadowMap.Fov, 1, shadowMap.ShadowNearDistance, shadowMap.ShadowFarDistance);
                }

                // Allocate shadow map area
                var shadowMapRectangle = new Rectangle();
                if (!shadowMap.Texture.GuillotinePacker.Insert(shadowMap.ShadowMapSize, shadowMap.ShadowMapSize, ref shadowMapRectangle))
                    throw new InvalidOperationException("Not enough space to allocate all shadow maps.");

                var cascadeTextureCoords = new Vector4(
                    (float)shadowMapRectangle.Left / (float)shadowMap.Texture.ShadowMapDepthTexture.Width,
                    (float)shadowMapRectangle.Top / (float)shadowMap.Texture.ShadowMapDepthTexture.Height,
                    (float)shadowMapRectangle.Right / (float)shadowMap.Texture.ShadowMapDepthTexture.Width,
                    (float)shadowMapRectangle.Bottom / (float)shadowMap.Texture.ShadowMapDepthTexture.Height);

                // Copy texture coords without border
                cascades[cascadeLevel].CascadeTextureCoords = cascadeTextureCoords;

                // Add border (avoid using edges due to bilinear filtering and blur)
                var boderSizeU = VsmBlurSize / shadowMap.Texture.ShadowMapDepthTexture.Width;
                var boderSizeV = VsmBlurSize / shadowMap.Texture.ShadowMapDepthTexture.Height;
                cascadeTextureCoords.X += boderSizeU;
                cascadeTextureCoords.Y += boderSizeV;
                cascadeTextureCoords.Z -= boderSizeU;
                cascadeTextureCoords.W -= boderSizeV;

                float leftX = (float)shadowMap.ShadowMapSize / (float)shadowMap.Texture.ShadowMapDepthTexture.Width * 0.5f;
                float leftY = (float)shadowMap.ShadowMapSize / (float)shadowMap.Texture.ShadowMapDepthTexture.Height * 0.5f;
                float centerX = 0.5f * (cascadeTextureCoords.X + cascadeTextureCoords.Z);
                float centerY = 0.5f * (cascadeTextureCoords.Y + cascadeTextureCoords.W);

                // Compute caster view proj matrix
                Matrix.Multiply(ref shadowMapView, ref shadowMapProjection, out cascades[cascadeLevel].ViewProjCaster);

                // Compute receiver view proj matrix
                // TODO: Optimize adjustment matrix computation
                Matrix adjustmentMatrix = Matrix.Scaling(leftX, -leftY, 0.5f) * Matrix.Translation(centerX, centerY, 0.5f);
                Matrix.Multiply(ref cascades[cascadeLevel].ViewProjCaster, ref adjustmentMatrix, out cascades[cascadeLevel].ReceiverInfo.ViewProjReceiver);

                // Copy texture coords with border
                cascades[cascadeLevel].ReceiverInfo.CascadeTextureCoordsBorder = cascadeTextureCoords;

                // Compute offset
                Matrix shadowVInverse;
                Matrix.Invert(ref shadowMapView, out shadowVInverse);
                cascades[cascadeLevel].ReceiverInfo.Offset = new Vector3(shadowVInverse.M41, shadowVInverse.M42, shadowVInverse.M43);
            }
        }

        #endregion
    }
}