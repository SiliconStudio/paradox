// Copyright (c) 2014-2016 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;

using SiliconStudio.Core;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Xenko.Engine;
using SiliconStudio.Xenko.Rendering.Lights;

namespace SiliconStudio.Xenko.Rendering.Shadows
{
    /// <summary>
    /// An allocated shadow map texture associated to a light.
    /// </summary>
    public class LightShadowMapTexture
    {
        public RenderView RenderView { get; private set; }

        public LightComponent LightComponent { get; private set; }

        public IDirectLight Light { get; private set; }

        public LightShadowMap Shadow { get; private set; }

        public Type FilterType { get; private set; }

        public byte TextureId { get; internal set; }

        public LightShadowType ShadowType { get; internal set; }

        public int Size { get; private set; }

        public int CascadeCount { get; set; }

        public float CurrentMinDistance { get; set; }

        public float CurrentMaxDistance { get; set; }

        public ShadowMapAtlasTexture Atlas { get; internal set; }

        public ILightShadowMapRenderer Renderer;

        public ILightShadowMapShaderData ShaderData;

        public void Initialize(RenderView renderView, LightComponent lightComponent, IDirectLight light, LightShadowMap shadowMap, int size, ILightShadowMapRenderer renderer)
        {
            if (renderView == null) throw new ArgumentNullException(nameof(renderView));
            if (lightComponent == null) throw new ArgumentNullException(nameof(lightComponent));
            if (light == null) throw new ArgumentNullException(nameof(light));
            if (shadowMap == null) throw new ArgumentNullException(nameof(shadowMap));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));

            RenderView = renderView;
            LightComponent = lightComponent;
            Light = light;
            Shadow = shadowMap;
            Size = size;
            FilterType = Shadow.Filter == null || !Shadow.Filter.RequiresCustomBuffer() ? null : Shadow.Filter.GetType();
            Renderer = renderer;
            Atlas = null; // Reset the atlas, It will be setup after
            CascadeCount = 1;

            ShadowType = renderer.GetShadowType(Shadow);
        }

        public Rectangle GetRectangle(int i)
        {
            if (i < 0 || i > CascadeCount || i > MaxRectangles)
            {
                throw new ArgumentOutOfRangeException("i", "Must be in the range [0, CascadeCount]");
            }
            unsafe
            {
                fixed (void* ptr = &Rectangle0)
                {
                    return ((Rectangle*)ptr)[i];
                }
            }
        }

        public void SetRectangle(int i, Rectangle value)
        {
            if (i < 0 || i > CascadeCount || i > MaxRectangles)
            {
                throw new ArgumentOutOfRangeException("i", "Must be in the range [0, CascadeCount]");
            }
            unsafe
            {
                fixed (void* ptr = &Rectangle0)
                {
                    ((Rectangle*)ptr)[i] = value;
                }
            }
        }

        // Even if C# things Rectangle1, Rectangle2 and Rectangle3 are not used,
        // they are indirectly in `GetRectangle' and `SetRectangle' through pointer
        // arithmetics.
        // MaxRectangles should be updated to match the actual number of rectangles to detected out of range errors
        public const int MaxRectangles = 6;
        private Rectangle Rectangle0;
#pragma warning disable 169
        private Rectangle Rectangle1;
        private Rectangle Rectangle2;
        private Rectangle Rectangle3;
        private Rectangle Rectangle4;
        private Rectangle Rectangle5;
#pragma warning restore 169

    }
}
