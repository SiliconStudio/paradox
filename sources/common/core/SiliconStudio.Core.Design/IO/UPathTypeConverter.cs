﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.ComponentModel;
using System.Globalization;

namespace SiliconStudio.Core.IO
{
    /// <summary>
    /// An abstract implementation of <see cref="TypeConverter"/> used for types derived from <see cref="UPath"/> in order to convert then from a string.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class UPathTypeConverter<T> : TypeConverter
    {
        /// <summary>
        /// Performs the actual string conversion.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        protected abstract T Convert(string value);

        /// <inheritdoc/>
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        /// <inheritdoc/>
        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var stringPath = value as string;
            return stringPath != null ? Convert(stringPath) : base.ConvertFrom(context, culture, value);
        }
    }

    /// <summary>
    /// The implementation of <see cref="TypeConverter"/> for <see cref="UFile"/> that implements conversion from strings.
    /// </summary>
    public sealed class UFileTypeConverter : UPathTypeConverter<UFile>
    {
        /// <inheritdoc/>
        protected override UFile Convert(string value)
        {
            return value;
        }
    }

    /// <summary>
    /// The implementation of <see cref="TypeConverter"/> for <see cref="UDirectory"/> that implements conversion from strings.
    /// </summary>
    public sealed class UDirectoryTypeConverter : UPathTypeConverter<UDirectory>
    {
        /// <inheritdoc/>
        protected override UDirectory Convert(string value)
        {
            return value;
        }
    }
}