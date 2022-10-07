// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.NET.Build.Tasks.ConflictResolution
{
    public class ResolvePackageFileConflicts : TaskWithAssemblyResolveHooks
    {
        private HashSet<ITaskItem> referenceConflicts = new HashSet<ITaskItem>();
        private HashSet<ITaskItem> analyzerConflicts = new HashSet<ITaskItem>();
        private HashSet<ITaskItem> copyLocalConflicts = new HashSet<ITaskItem>();
        private HashSet<ConflictItem> compilePlatformWinners = new HashSet<ConflictItem>();
        private HashSet<ConflictItem> allConflicts = new HashSet<ConflictItem>();

        public ITaskItem[] References { get; set; }

        public ITaskItem[] Analyzers { get; set; }

        public ITaskItem[] ReferenceCopyLocalPaths { get; set; }

        public ITaskItem[] OtherRuntimeItems { get; set; }

        public ITaskItem[] PlatformManifests { get; set; }

        public ITaskItem[] TargetFrameworkDirectories { get; set; }

        /// <summary>
        /// NuGet3 and later only.  In the case of a conflict with identical file version information a file from the most preferred package will be chosen.
        /// </summary>
        public string[] PreferredPackages { get; set; }

        /// <summary>
        /// A collection of items that contain information of which packages get overridden
        /// by which packages before doing any other conflict resolution.
        /// </summary>
        /// <remarks>
        /// This is an optimization so AssemblyVersions, FileVersions, etc. don't need to be read
        /// in the default cases where platform packages (Microsoft.NETCore.App) should override specific packages
        /// (System.Console v4.3.0).
        /// </remarks>
        public ITaskItem[] PackageOverrides { get; set; }

        [Output]
        public ITaskItem[] ReferencesWithoutConflicts { get; set; }

        [Output]
        public ITaskItem[] AnalyzersWithoutConflicts { get; set; }


        [Output]
        public ITaskItem[] ReferenceCopyLocalPathsWithoutConflicts { get; set; }

        [Output]
        public ITaskItem[] Conflicts { get; set; }

        protected override void ExecuteCore()
        {
            
        }

        //  Concatenate two things, either of which may be null.  Interpret null as empty,
        //  and return null if the result would be empty.
        private ITaskItem[] SafeConcat(ITaskItem[] first, IEnumerable<ITaskItem> second)
        {
            if (first == null || first.Length == 0)
            {
                return second?.ToArray();
            }

            if (second == null || !second.Any())
            {
                return first;
            }

            return first.Concat(second).ToArray();
        }

        private ITaskItem[] CreateConflictTaskItems(ICollection<ConflictItem> conflicts)
        {
            var conflictItems = new ITaskItem[conflicts.Count];

            int i = 0;
            foreach(var conflict in conflicts)
            {
                conflictItems[i++] = CreateConflictTaskItem(conflict);
            }

            return conflictItems;
        }

        private ITaskItem CreateConflictTaskItem(ConflictItem conflict)
        {
            var item = new TaskItem(conflict.SourcePath);

            if (conflict.PackageId != null)
            {
                item.SetMetadata(nameof(ConflictItemType), conflict.ItemType.ToString());
                item.SetMetadata(MetadataKeys.NuGetPackageId, conflict.PackageId);
            }

            return item;
        }

        private IEnumerable<ConflictItem> GetConflictTaskItems(ITaskItem[] items, ConflictItemType itemType)
        {
            return (items != null) ? items.Select(i => new ConflictItem(i, itemType)) : Enumerable.Empty<ConflictItem>();
        }

        private void HandleCompileConflict(ConflictItem winner, ConflictItem loser)
        {
            if (loser.ItemType == ConflictItemType.Reference)
            {
                referenceConflicts.Add(loser.OriginalItem);

                if (winner.ItemType == ConflictItemType.Platform)
                {
                    compilePlatformWinners.Add(winner);
                }
            }
            allConflicts.Add(loser);
        }

        private void HandleAnalyzerConflict(ConflictItem winner, ConflictItem loser)
        {
            analyzerConflicts.Add(loser.OriginalItem);
            allConflicts.Add(loser);
        }

        private void HandleRuntimeConflict(ConflictItem winner, ConflictItem loser)
        {
            if (loser.ItemType == ConflictItemType.Reference)
            {
                loser.OriginalItem.SetMetadata(MetadataNames.Private, "False");
            }
            else if (loser.ItemType == ConflictItemType.CopyLocal)
            {
                copyLocalConflicts.Add(loser.OriginalItem);
            }
            allConflicts.Add(loser);
        }

        /// <summary>
        /// Filters conflicts from original, maintaining order.
        /// </summary>
        /// <param name="original"></param>
        /// <param name="conflicts"></param>
        /// <returns></returns>
        private ITaskItem[] RemoveConflicts(ITaskItem[] original, ICollection<ITaskItem> conflicts)
        {
            if (conflicts.Count == 0)
            {
                return original;
            }

            var result = new ITaskItem[original.Length - conflicts.Count];
            int index = 0;

            foreach (var originalItem in original)
            {
                if (!conflicts.Contains(originalItem))
                {
                    if (index >= result.Length)
                    {
                        throw new ArgumentException($"Items from {nameof(conflicts)} were missing from {nameof(original)}");
                    }
                    result[index++] = originalItem;
                }
            }

            //  If there are duplicates in the original list, then our size calculation for the result will have been wrong.
            //  So we have to re-allocate the array with the right size.
            //  Duplicates can happen if there are duplicate Reference items that are joined with a reference from a package in ResolveLockFileReferences
            if (index != result.Length)
            {
                return result.Take(index).ToArray();
            }

            return result;
        }
    }
}
