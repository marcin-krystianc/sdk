// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using static Microsoft.NET.Sdk.WorkloadManifestReader.WorkloadResolver;
using Microsoft.DotNet.Workloads.Workload.Install.InstallRecord;
using Microsoft.Extensions.EnvironmentAbstractions;
using System.Collections.Generic;
using Microsoft.NET.Sdk.WorkloadManifestReader;
using Microsoft.DotNet.Cli;

namespace Microsoft.DotNet.Workloads.Workload.Install
{
    internal interface IWorkloadPackInstaller : IInstaller
    {
        void InstallWorkloadPacks(IEnumerable<PackInfo> packInfos, SdkFeatureBand sdkFeatureBand, ITransactionContext transactionContext, DirectoryPath? offlineCache = null);

        void RepairWorkloadPack(PackInfo packInfo, SdkFeatureBand sdkFeatureBand, ITransactionContext transactionContext, DirectoryPath? offlineCache = null);

        void DownloadToOfflineCache(PackInfo packInfo, DirectoryPath offlineCache, bool includePreviews);

        void GarbageCollectInstalledWorkloadPacks(DirectoryPath? offlineCache =  null);

        IEnumerable<(WorkloadPackId Id, string Version)> GetInstalledPacks(SdkFeatureBand sdkFeatureBand);
    }
}
