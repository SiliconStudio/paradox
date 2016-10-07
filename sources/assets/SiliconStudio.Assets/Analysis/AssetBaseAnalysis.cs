﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System;
using System.Collections.Generic;
using System.Linq;

using SiliconStudio.Assets.Diagnostics;
using SiliconStudio.Core.Diagnostics;
using SiliconStudio.Core.Reflection;

namespace SiliconStudio.Assets.Analysis
{
    /// <summary>
    /// An analysis to validate that all assets in a package have a valid <see cref="Asset.Base"/>.
    ///  In order to be valid, this analysis must be run after a <see cref="PackageAnalysis"/>
    /// </summary>
    public sealed class AssetBaseAnalysis : PackageSessionAnalysisBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AssetBaseAnalysis"/> class.
        /// </summary>
        /// <param name="packageSession">The package session.</param>
        public AssetBaseAnalysis(PackageSession packageSession)
            : base(packageSession)
        {
        }

        /// <summary>
        /// Performs a wide package validation analysis.
        /// </summary>
        /// <param name="log">The log to output the result of the validation.</param>
        public override void Run(ILogger log)
        {
            if (log == null) throw new ArgumentNullException("log");

            ValidateAssetBase(log);
        }

        /// <summary>
        /// Validates the inheritance of all assets in the package.
        /// </summary>
        /// <param name="log">The log to output the result of the analysis.</param>
        /// <returns>A collection that contains all valid assets.</returns>
        public HashSet<Asset> ValidateAssetBase(ILogger log)
        {
            var invalidAssets = new HashSet<Guid>();
            var validAssets = new HashSet<Asset>();

            foreach (var package in Session.Packages)
            {
                foreach (var assetItem in package.Assets)
                {
                    // This asset has been already flagged as invalid
                    if (invalidAssets.Contains(assetItem.Id))
                    {
                        continue;
                    }

                    var result = ValidateAssetBase(assetItem);

                    if (result.HasErrors)
                    {
                        // Copy errors to output log
                        result.CopyTo(log);

                        invalidAssets.Add(assetItem.Id);

                        // Value contains valid base asset, but they are invalid
                        // if any of the parent base are not valid
                        foreach (var baseItem in result.Value)
                        {
                            invalidAssets.Add(baseItem.Id);
                        }

                        foreach (var logMessage in result.Messages.OfType<AssetLogMessage>())
                        {
                            invalidAssets.Add(logMessage.AssetReference.Id);
                        }
                    }
                    else
                    {
                        validAssets.Add(assetItem.Asset);
                    }
                }
            }

            return validAssets;
        }

        /// <summary>
        /// Validates the inheritance of an asset by checking base accessibility up to the root base.
        /// </summary>
        /// <param name="assetItem">The asset item.</param>
        /// <returns>A logger result with a list of all the base in bottom-up orde.</returns>
        public LoggerValueResult<List<Asset>> ValidateAssetBase(AssetItem assetItem)
        {
            var results = new LoggerValueResult<List<Asset>>();
            results.Value.AddRange(ValidateAssetBase(assetItem, results));
            return results;
        }

        /// <summary>
        /// Validates the inheritance of an asset by checking base accessibility up to the root base.
        /// </summary>
        /// <param name="assetItem">The asset item.</param>
        /// <param name="log">The log to output the result of the analysis.</param>
        /// <returns>A list of all the base in bottom-up order.</returns>
        /// <exception cref="System.ArgumentNullException">asset
        /// or
        /// log</exception>
        public List<Asset> ValidateAssetBase(AssetItem assetItem, ILogger log)
        {
            if (assetItem == null) throw new ArgumentNullException("asset");
            if (log == null) throw new ArgumentNullException("log");

            var baseItems = new List<Asset>();

            // 1) Check that item is actually in the package and is the same instance
            var assetItemFound = Session.FindAsset(assetItem.Id);
            if (!ReferenceEquals(assetItem.Asset, assetItemFound.Asset))
            {
                var assetReference = assetItem.ToReference();
                log.Error(assetItem.Package, assetReference, AssetMessageCode.AssetForPackageNotFound, assetReference, assetItem.Package.FullPath.GetFileName());
                return baseItems;
            }

            // 2) Iterate on each base and perform validation
            var currentAsset = assetItem;
            while (currentAsset.Asset.Base != null)
            {
                // 2.1) Check that asset has not been already processed
                if (baseItems.Contains(currentAsset.Asset))
                {
                    // Else this is a circular reference
                    log.Error(assetItem.Package, currentAsset.ToReference(), AssetMessageCode.InvalidCircularReferences, baseItems.Select(item => item.Id));
                    break;
                }

                // TODO: here we need to add a deep-scan of each base (including the root) for any embedded assets that are 
                // 

                // 2.2) Check that base asset is existing
                var baseAssetItem = Session.FindAsset(currentAsset.Asset.Base.Id);
                if (baseAssetItem == null)
                {
                    AssetLogMessage error;

                    // If an asset with the same location is registered
                    // Add this asset as a reference in the error message
                    var newBaseAsset = Session.FindAsset(currentAsset.Asset.Base.Location);
                    if (newBaseAsset != null)
                    {
                        // If asset location exist, log a message with the new location, but don't perform any automatic fix
                        error = new AssetLogMessage(currentAsset.Package, currentAsset.ToReference(), LogMessageType.Error, AssetMessageCode.BaseChanged, currentAsset.Asset.Base.Location);
                        error.Related.Add(newBaseAsset.ToReference());
                    }
                    else
                    {
                        // Base was not found. The base asset has been removed.
                        error = new AssetLogMessage(currentAsset.Package, currentAsset.ToReference(), LogMessageType.Error, AssetMessageCode.BaseNotFound);
                    }

                    // Set the member to Base.
                    error.Member = TypeDescriptorFactory.Default.Find(typeof(Asset))["Base"];

                    // Log the error
                    log.Log(error);
                    break;
                }
                else
                {
                    if (baseAssetItem.GetType() != assetItem.Asset.GetType())
                    {
                        log.Error(currentAsset.Package, currentAsset.ToReference(), AssetMessageCode.BaseInvalidType, baseAssetItem.GetType(), assetItem.Asset.GetType());
                    }
                }

                currentAsset = baseAssetItem;
                baseItems.Add(currentAsset.Asset);
            }
            return baseItems;
        }

        private class AssetVisitor : DataVisitorBase
        {
            private readonly Asset rootAsset;
            private readonly List<Asset> assets = new List<Asset>();

            public AssetVisitor(ITypeDescriptorFactory typeDescriptorFactory, Asset rootAsset)
                : base(typeDescriptorFactory)
            {
                this.rootAsset = rootAsset;
            }

            public List<Asset> Collect()
            {
                Visit(rootAsset);
                return assets;
            }

            public override void VisitObject(object obj, ObjectDescriptor descriptor, bool visitMembers)
            {
                if (obj is Asset && !ReferenceEquals(obj, rootAsset))
                {
                    assets.Add((Asset)obj);
                }
                base.VisitObject(obj, descriptor, visitMembers);
            }
        }
    }
}