﻿// <auto-generated>
// Do not edit this file yourself!
//
// This code was generated by Xenko Shader Mixin Code Generator.
// To generate it yourself, please install SiliconStudio.Xenko.VisualStudio.Package .vsix
// and re-save the associated .xkfx.
// </auto-generated>

using System;
using SiliconStudio.Core;
using SiliconStudio.Xenko.Rendering;
using SiliconStudio.Xenko.Graphics;
using SiliconStudio.Xenko.Shaders;
using SiliconStudio.Core.Mathematics;
using Buffer = SiliconStudio.Xenko.Graphics.Buffer;

namespace SiliconStudio.Xenko.Rendering.Shadows
{
    internal static partial class ShadowMapReceiverPointCubeMapKeys
    {
        public static readonly ValueParameterKey<Vector4> LightPosition = ParameterKeys.NewValue<Vector4>();
        public static readonly ValueParameterKey<Vector2> LightFaceOffsets = ParameterKeys.NewValue<Vector2>();
        public static readonly ValueParameterKey<Vector2> LightFaceSize = ParameterKeys.NewValue<Vector2>();
        public static readonly ValueParameterKey<Vector2> LightDepthParameters = ParameterKeys.NewValue<Vector2>();
    }
}
