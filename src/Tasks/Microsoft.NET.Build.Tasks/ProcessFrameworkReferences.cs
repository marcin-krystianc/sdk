// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Configurer;
using Microsoft.DotNet.Workloads.Workload;
using Microsoft.NET.Sdk.WorkloadManifestReader;
using Newtonsoft.Json;
using NuGet.Frameworks;

namespace Microsoft.NET.Build.Tasks
{
    /// <summary>
    /// This class processes the FrameworkReference items.  It adds PackageReferences for the
    /// targeting packs which provide the reference assemblies, and creates RuntimeFramework
    /// items, which are written to the runtimeconfig file
    /// </summary>
    public class ProcessFrameworkReferences : TaskBase
    {
        public string TargetFrameworkIdentifier { get; set; }

        public string TargetFrameworkVersion { get; set; }

        public string TargetPlatformIdentifier { get; set; }

        public string TargetPlatformVersion { get; set; }

        public string TargetingPackRoot { get; set; }

        [Required]
        public string RuntimeGraphPath { get; set; }

        public bool SelfContained { get; set; }

        public bool ReadyToRunEnabled { get; set; }

        public bool ReadyToRunUseCrossgen2 { get; set; }

        public string RuntimeIdentifier { get; set; }

        public string[] RuntimeIdentifiers { get; set; }

        public string RuntimeFrameworkVersion { get; set; }

        public bool TargetLatestRuntimePatch { get; set; }

        public bool TargetLatestRuntimePatchIsDefault { get; set; }

        public bool EnableTargetingPackDownload { get; set; }

        public bool EnableRuntimePackDownload { get; set; }

        public bool EnableWindowsTargeting { get; set; }

        public bool DisableTransitiveFrameworkReferenceDownloads { get; set; }

        public ITaskItem[] FrameworkReferences { get; set; } = Array.Empty<ITaskItem>();

        public ITaskItem[] KnownFrameworkReferences { get; set; } = Array.Empty<ITaskItem>();

        public ITaskItem[] KnownRuntimePacks { get; set; } = Array.Empty<ITaskItem>();

        public ITaskItem[] KnownCrossgen2Packs { get; set; } = Array.Empty<ITaskItem>();

        [Required]
        public string NETCoreSdkRuntimeIdentifier { get; set; }

        [Required]
        public string NetCoreRoot { get; set; }

        [Required]
        public string NETCoreSdkVersion { get; set; }

        [Output]
        public ITaskItem[] PackagesToDownload { get; set; }

        [Output]
        public ITaskItem[] RuntimeFrameworks { get; set; }

        [Output]
        public ITaskItem[] TargetingPacks { get; set; }

        [Output]
        public ITaskItem[] RuntimePacks { get; set; }

        [Output]
        public ITaskItem[] Crossgen2Packs { get; set; }

        //  Runtime packs which aren't available for the specified RuntimeIdentifier
        [Output]
        public ITaskItem[] UnavailableRuntimePacks { get; set; }

        protected override void ExecuteCore()
        {
            
        }

        private KnownRuntimePack? SelectRuntimePack(ITaskItem frameworkReference, KnownFrameworkReference knownFrameworkReference, List<KnownRuntimePack> knownRuntimePacks)
        {
            var requiredLabelsMetadata = frameworkReference?.GetMetadata(MetadataKeys.RuntimePackLabels) ?? "";

            HashSet<string> requiredRuntimePackLabels = null;
            if (frameworkReference != null)
            {
                requiredRuntimePackLabels = new HashSet<string>(requiredLabelsMetadata.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            }

            //  The runtime pack name matches the RuntimeFrameworkName on the KnownFrameworkReference
            var matchingRuntimePacks = knownRuntimePacks.Where(krp => krp.Name.Equals(knownFrameworkReference.RuntimeFrameworkName, StringComparison.OrdinalIgnoreCase))
                .Where(krp =>
                {
                    if (requiredRuntimePackLabels == null)
                    {
                        return krp.RuntimePackLabels.Length == 0;
                    }
                    else
                    {
                        return requiredRuntimePackLabels.SetEquals(krp.RuntimePackLabels);
                    }
                })
                .ToList();

            if (matchingRuntimePacks.Count == 0)
            {
                return null;
            }
            else if (matchingRuntimePacks.Count == 1)
            {
                return matchingRuntimePacks[0];
            }
            else
            {
                string runtimePackDescriptionForErrorMessage = knownFrameworkReference.RuntimeFrameworkName +
                    (requiredLabelsMetadata == string.Empty ? string.Empty : ":" + requiredLabelsMetadata);

                Log.LogError(Strings.ConflictingRuntimePackInformation, runtimePackDescriptionForErrorMessage,
                    string.Join(Environment.NewLine, matchingRuntimePacks.Select(rp => rp.RuntimePackNamePatterns)));

                return knownFrameworkReference.ToKnownRuntimePack();
            }
        }

        private void ProcessRuntimeIdentifier(
            string runtimeIdentifier,
            KnownRuntimePack selectedRuntimePack,
            string runtimePackVersion,
            List<string> additionalFrameworkReferencesForRuntimePack,
            HashSet<string> unrecognizedRuntimeIdentifiers,
            List<ITaskItem> unavailableRuntimePacks,
            List<ITaskItem> runtimePacks,
            List<ITaskItem> packagesToDownload,
            string isTrimmable,
            bool addRuntimePackAndDownloadIfNecessary,
            bool wasReferencedDirectly)
        {
            var runtimeGraph = new RuntimeGraphCache(this).GetRuntimeGraph(RuntimeGraphPath);
            var knownFrameworkReferenceRuntimePackRuntimeIdentifiers = selectedRuntimePack.RuntimePackRuntimeIdentifiers.Split(';');
            var knownFrameworkReferenceRuntimePackExcludedRuntimeIdentifiers = selectedRuntimePack.RuntimePackExcludedRuntimeIdentifiers.Split(';');

            string runtimePackRuntimeIdentifier = NuGetUtils.GetBestMatchingRidWithExclusion(
                    runtimeGraph,
                    runtimeIdentifier,
                    knownFrameworkReferenceRuntimePackExcludedRuntimeIdentifiers,
                    knownFrameworkReferenceRuntimePackRuntimeIdentifiers,
                    out bool wasInGraph);

            if (runtimePackRuntimeIdentifier == null)
            {
                if (wasInGraph)
                {
                    //  Report this as an error later, if necessary.  This is because we try to download
                    //  all available runtime packs in case there is a transitive reference to a shared
                    //  framework we don't directly reference.  But we don't want to immediately error out
                    //  here if a runtime pack that we might not need to reference isn't available for the
                    //  targeted RID (e.g. Microsoft.WindowsDesktop.App for a linux RID).
                    var unavailableRuntimePack = new TaskItem(selectedRuntimePack.Name);
                    unavailableRuntimePack.SetMetadata(MetadataKeys.RuntimeIdentifier, runtimeIdentifier);
                    unavailableRuntimePacks.Add(unavailableRuntimePack);
                }
                else if (!unrecognizedRuntimeIdentifiers.Contains(runtimeIdentifier))
                {
                    //  NETSDK1083: The specified RuntimeIdentifier '{0}' is not recognized.
                    Log.LogError(Strings.RuntimeIdentifierNotRecognized, runtimeIdentifier);
                    unrecognizedRuntimeIdentifiers.Add(runtimeIdentifier);
                }
            }
            else if (addRuntimePackAndDownloadIfNecessary)
            {
                foreach (var runtimePackNamePattern in selectedRuntimePack.RuntimePackNamePatterns.Split(';'))
                {
                    string runtimePackName = runtimePackNamePattern.Replace("**RID**", runtimePackRuntimeIdentifier);

                    //  Look up runtimePackVersion from workload manifests if necessary
                    string resolvedRuntimePackVersion = GetResolvedPackVersion(runtimePackName, runtimePackVersion);

                    string runtimePackPath = GetPackPath(runtimePackName, resolvedRuntimePackVersion);

                    if (runtimePacks != null)
                    {
                        TaskItem runtimePackItem = new TaskItem(runtimePackName);
                        runtimePackItem.SetMetadata(MetadataKeys.NuGetPackageId, runtimePackName);
                        runtimePackItem.SetMetadata(MetadataKeys.NuGetPackageVersion, resolvedRuntimePackVersion);
                        runtimePackItem.SetMetadata(MetadataKeys.FrameworkName, selectedRuntimePack.Name);
                        runtimePackItem.SetMetadata(MetadataKeys.RuntimeIdentifier, runtimePackRuntimeIdentifier);
                        runtimePackItem.SetMetadata(MetadataKeys.IsTrimmable, isTrimmable);

                        if (selectedRuntimePack.RuntimePackAlwaysCopyLocal)
                        {
                            runtimePackItem.SetMetadata(MetadataKeys.RuntimePackAlwaysCopyLocal, "true");
                        }

                        if (additionalFrameworkReferencesForRuntimePack != null)
                        {
                            runtimePackItem.SetMetadata(MetadataKeys.AdditionalFrameworkReferences, string.Join(";", additionalFrameworkReferencesForRuntimePack));
                        }

                        if (runtimePackPath != null)
                        {
                            runtimePackItem.SetMetadata(MetadataKeys.PackageDirectory, runtimePackPath);
                        }

                        runtimePacks.Add(runtimePackItem);
                    }

                    if (runtimePackPath == null && (wasReferencedDirectly || !DisableTransitiveFrameworkReferenceDownloads))
                    {
                        TaskItem packageToDownload = new TaskItem(runtimePackName);
                        packageToDownload.SetMetadata(MetadataKeys.Version, resolvedRuntimePackVersion);

                        packagesToDownload.Add(packageToDownload);
                    }
                }
            }
        }

        private bool AddCrossgen2Package(Version normalizedTargetFrameworkVersion, List<ITaskItem> packagesToDownload)
        {
            var knownCrossgen2Pack = KnownCrossgen2Packs.Where(crossgen2Pack =>
            {
                var packTargetFramework = NuGetFramework.Parse(crossgen2Pack.GetMetadata("TargetFramework"));
                return packTargetFramework.Framework.Equals(TargetFrameworkIdentifier, StringComparison.OrdinalIgnoreCase) &&
                    NormalizeVersion(packTargetFramework.Version) == normalizedTargetFrameworkVersion;
            }).SingleOrDefault();

            if (knownCrossgen2Pack == null)
            {
                return false;
            }

            var crossgen2PackPattern = knownCrossgen2Pack.GetMetadata("Crossgen2PackNamePattern");
            var crossgen2PackVersion = knownCrossgen2Pack.GetMetadata("Crossgen2PackVersion");
            var crossgen2PackSupportedRuntimeIdentifiers = knownCrossgen2Pack.GetMetadata("Crossgen2RuntimeIdentifiers").Split(';');

            // Get the best RID for the host machine, which will be used to validate that we can run crossgen for the target platform and architecture
            var runtimeGraph = new RuntimeGraphCache(this).GetRuntimeGraph(RuntimeGraphPath);
            var hostRuntimeIdentifier = NuGetUtils.GetBestMatchingRid(runtimeGraph, NETCoreSdkRuntimeIdentifier, crossgen2PackSupportedRuntimeIdentifiers, out bool wasInGraph);
            if (hostRuntimeIdentifier == null)
            {
                return false;
            }

            var crossgen2PackName = crossgen2PackPattern.Replace("**RID**", hostRuntimeIdentifier);
            if (!string.IsNullOrEmpty(RuntimeFrameworkVersion))
            {
                crossgen2PackVersion = RuntimeFrameworkVersion;
            }

            TaskItem packageToDownload = new TaskItem(crossgen2PackName);
            packageToDownload.SetMetadata(MetadataKeys.Version, crossgen2PackVersion);
            packagesToDownload.Add(packageToDownload);

            Crossgen2Packs = new ITaskItem[1];
            Crossgen2Packs[0] = new TaskItem(crossgen2PackName);
            Crossgen2Packs[0].SetMetadata(MetadataKeys.NuGetPackageId, crossgen2PackName);
            Crossgen2Packs[0].SetMetadata(MetadataKeys.NuGetPackageVersion, crossgen2PackVersion);

            return true;
        }

        private string GetRuntimeFrameworkVersion(
            ITaskItem frameworkReference,
            KnownFrameworkReference knownFrameworkReference,
            KnownRuntimePack? knownRuntimePack,
            out string runtimePackVersion)
        {
            //  Precedence order for selecting runtime framework version
            //  - RuntimeFrameworkVersion metadata on FrameworkReference item
            //  - RuntimeFrameworkVersion MSBuild property
            //  - Then, use either the LatestRuntimeFrameworkVersion or the DefaultRuntimeFrameworkVersion of the KnownFrameworkReference, based on
            //      - The value (if set) of TargetLatestRuntimePatch metadata on the FrameworkReference
            //      - The TargetLatestRuntimePatch MSBuild property (which defaults to True if SelfContained is true, and False otherwise)
            //      - But, if TargetLatestRuntimePatch was defaulted and not overridden by user, then acquire latest runtime pack for future
            //        self-contained deployment (or for crossgen of framework-dependent deployment), while targeting the default version.

            string requestedVersion = GetRequestedRuntimeFrameworkVersion(frameworkReference);
            if (!string.IsNullOrEmpty(requestedVersion))
            {
                runtimePackVersion = requestedVersion;
                return requestedVersion;
            }

            switch (GetRuntimePatchRequest(frameworkReference))
            {
                case RuntimePatchRequest.UseDefaultVersion:
                    runtimePackVersion = knownFrameworkReference.DefaultRuntimeFrameworkVersion;
                    return knownFrameworkReference.DefaultRuntimeFrameworkVersion;

                case RuntimePatchRequest.UseLatestVersion:
                    if (knownRuntimePack != null)
                    {
                        runtimePackVersion = knownRuntimePack?.LatestRuntimeFrameworkVersion;
                        return knownRuntimePack?.LatestRuntimeFrameworkVersion;
                    }
                    else
                    {
                        runtimePackVersion = knownFrameworkReference.DefaultRuntimeFrameworkVersion;
                        return knownFrameworkReference.DefaultRuntimeFrameworkVersion;
                    }
                case RuntimePatchRequest.UseDefaultVersionWithLatestRuntimePack:
                    if (knownRuntimePack != null)
                    {
                        runtimePackVersion = knownRuntimePack?.LatestRuntimeFrameworkVersion;
                    }
                    else
                    {
                        runtimePackVersion = knownFrameworkReference.DefaultRuntimeFrameworkVersion;
                    }
                    return knownFrameworkReference.DefaultRuntimeFrameworkVersion;

                default:
                    // Unreachable
                    throw new InvalidOperationException();
            }
        }

        private string GetPackPath(string packName, string packVersion)
        {
            IEnumerable<string> GetPackFolders()
            {
                var packRootEnvironmentVariable = Environment.GetEnvironmentVariable(EnvironmentVariableNames.WORKLOAD_PACK_ROOTS);
                if (!string.IsNullOrEmpty(packRootEnvironmentVariable))
                {
                    foreach (var packRoot in packRootEnvironmentVariable.Split(Path.PathSeparator))
                    {
                        yield return Path.Combine(packRoot, "packs");
                    }
                }

                if (!string.IsNullOrEmpty(NetCoreRoot) && !string.IsNullOrEmpty(NETCoreSdkVersion))
                {
                    if (WorkloadFileBasedInstall.IsUserLocal(NetCoreRoot, NETCoreSdkVersion) &&
                        CliFolderPathCalculatorCore.GetDotnetUserProfileFolderPath() is { } userProfileDir)
                    {
                        yield return Path.Combine(userProfileDir, "packs");
                    }
                }

                if (!string.IsNullOrEmpty(TargetingPackRoot))
                {
                    yield return TargetingPackRoot;
                }
            }

            foreach (var packFolder in GetPackFolders())
            {
                string packPath = Path.Combine(packFolder, packName, packVersion);
                if (Directory.Exists(packPath))
                {
                    return packPath;
                }
            }

            return null;
        }

        SdkDirectoryWorkloadManifestProvider _workloadManifestProvider;
        WorkloadResolver _workloadResolver;

        private string GetResolvedPackVersion(string packID, string packVersion)
        {
            if (!packVersion.Equals("**FromWorkload**", StringComparison.OrdinalIgnoreCase))
            {
                return packVersion;
            }

            if (_workloadManifestProvider == null)
            {
                string userProfileDir = CliFolderPathCalculatorCore.GetDotnetUserProfileFolderPath();
                _workloadManifestProvider = new SdkDirectoryWorkloadManifestProvider(NetCoreRoot, NETCoreSdkVersion, userProfileDir);
                _workloadResolver = WorkloadResolver.Create(_workloadManifestProvider, NetCoreRoot, NETCoreSdkVersion, userProfileDir);
            }

            var packInfo = _workloadResolver.TryGetPackInfo(new WorkloadPackId(packID));
            if (packInfo == null)
            {
                Log.LogError("NETSDKZZZZ: Error getting pack version: Pack '{0}' was not present in workload manifests.", packID);
                return packVersion;
            }
            return packInfo.Version;
        }

        private enum RuntimePatchRequest
        {
            UseDefaultVersionWithLatestRuntimePack,
            UseDefaultVersion,
            UseLatestVersion,
        }

        /// <summary>
        /// Compare PackageToDownload by name and version.
        /// Used to deduplicate PackageToDownloads
        /// </summary>
        private class PackageToDownloadComparer<T> : IEqualityComparer<T> where T : ITaskItem
        {
            public bool Equals(T x, T y)
            {
                if (x is null || y is null)
                {
                    return false;
                }

                return x.ItemSpec.Equals(y.ItemSpec,
                           StringComparison.OrdinalIgnoreCase) &&
                       x.GetMetadata(MetadataKeys.Version).Equals(
                           y.GetMetadata(MetadataKeys.Version), StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(T obj)
            {
                var hashCode = -1923861349;
                hashCode = hashCode * -1521134295 + obj.ItemSpec.GetHashCode();
                hashCode = hashCode * -1521134295 + obj.GetMetadata(MetadataKeys.Version).GetHashCode();
                return hashCode;
            }
        }

        private RuntimePatchRequest GetRuntimePatchRequest(ITaskItem frameworkReference)
        {
            string value = frameworkReference?.GetMetadata("TargetLatestRuntimePatch");
            if (!string.IsNullOrEmpty(value))
            {
                return MSBuildUtilities.ConvertStringToBool(value, defaultValue: false)
                    ? RuntimePatchRequest.UseLatestVersion
                    : RuntimePatchRequest.UseDefaultVersion;
            }

            if (TargetLatestRuntimePatch)
            {
                return RuntimePatchRequest.UseLatestVersion;
            }

            return TargetLatestRuntimePatchIsDefault
                ? RuntimePatchRequest.UseDefaultVersionWithLatestRuntimePack
                : RuntimePatchRequest.UseDefaultVersion;
        }

        private string GetRequestedRuntimeFrameworkVersion(ITaskItem frameworkReference)
        {
            string requestedVersion = frameworkReference?.GetMetadata("RuntimeFrameworkVersion");

            if (string.IsNullOrEmpty(requestedVersion))
            {
                requestedVersion = RuntimeFrameworkVersion;
            }

            return requestedVersion;
        }

        internal static Version NormalizeVersion(Version version)
        {
            if (version.Revision == 0)
            {
                if (version.Build == 0)
                {
                    return new Version(version.Major, version.Minor);
                }
                else
                {
                    return new Version(version.Major, version.Minor, version.Build);
                }
            }

            return version;
        }

        private struct KnownFrameworkReference
        {
            ITaskItem _item;
            public KnownFrameworkReference(ITaskItem item)
            {
                _item = item;
                TargetFramework = NuGetFramework.Parse(item.GetMetadata("TargetFramework"));
            }

            //  The name / itemspec of the FrameworkReference used in the project
            public string Name => _item.ItemSpec;

            //  The framework name to write to the runtimeconfig file (and the name of the folder under dotnet/shared)
            public string RuntimeFrameworkName => _item.GetMetadata(MetadataKeys.RuntimeFrameworkName);
            public string DefaultRuntimeFrameworkVersion => _item.GetMetadata("DefaultRuntimeFrameworkVersion");

            //  The ID of the targeting pack NuGet package to reference
            public string TargetingPackName => _item.GetMetadata("TargetingPackName");
            public string TargetingPackVersion => _item.GetMetadata("TargetingPackVersion");
            public string TargetingPackFormat => _item.GetMetadata("TargetingPackFormat");

            public string RuntimePackRuntimeIdentifiers => _item.GetMetadata(MetadataKeys.RuntimePackRuntimeIdentifiers);

            public bool IsWindowsOnly => _item.HasMetadataValue("IsWindowsOnly", "true");
            
            public bool RuntimePackAlwaysCopyLocal =>
                _item.HasMetadataValue(MetadataKeys.RuntimePackAlwaysCopyLocal, "true");

            public string Profile => _item.GetMetadata("Profile");

            public NuGetFramework TargetFramework { get; }

            public KnownRuntimePack ToKnownRuntimePack()
            {
                return new KnownRuntimePack(_item);
            }
        }

        private struct KnownRuntimePack
        {
            ITaskItem _item;

            public KnownRuntimePack(ITaskItem item)
            {
                _item = item;
                TargetFramework = NuGetFramework.Parse(item.GetMetadata("TargetFramework"));
                string runtimePackLabels = item.GetMetadata(MetadataKeys.RuntimePackLabels);
                if (string.IsNullOrEmpty(runtimePackLabels))
                {
                    RuntimePackLabels = Array.Empty<string>();
                }
                else
                {
                    RuntimePackLabels = runtimePackLabels.Split(';');
                }
            }

            //  The name / itemspec of the FrameworkReference used in the project
            public string Name => _item.ItemSpec;

            ////  The framework name to write to the runtimeconfig file (and the name of the folder under dotnet/shared)
            public string LatestRuntimeFrameworkVersion => _item.GetMetadata("LatestRuntimeFrameworkVersion");

            public string RuntimePackNamePatterns => _item.GetMetadata("RuntimePackNamePatterns");

            public string RuntimePackRuntimeIdentifiers => _item.GetMetadata(MetadataKeys.RuntimePackRuntimeIdentifiers);

            public string RuntimePackExcludedRuntimeIdentifiers => _item.GetMetadata(MetadataKeys.RuntimePackExcludedRuntimeIdentifiers);

            public string IsTrimmable => _item.GetMetadata(MetadataKeys.IsTrimmable);

            public bool IsWindowsOnly => _item.HasMetadataValue("IsWindowsOnly", "true");

            public bool RuntimePackAlwaysCopyLocal =>
                _item.HasMetadataValue(MetadataKeys.RuntimePackAlwaysCopyLocal, "true");

            public string[] RuntimePackLabels { get; }

            public NuGetFramework TargetFramework { get; }
        }
    }
}
