// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.NET.Build.Tasks
{
    /// <summary>
    /// Raises Nuget LockFile representation to MSBuild items and resolves
    /// assets specified in the lock file.
    /// </summary>
    public sealed class ResolvePackageDependencies : TaskBase
    {
        private readonly Dictionary<string, string> _fileTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private IPackageResolver _packageResolver;
        private LockFile _lockFile;

        #region Output Items

        private readonly List<ITaskItem> _targetDefinitions = new List<ITaskItem>();
        private readonly List<ITaskItem> _packageDefinitions = new List<ITaskItem>();
        private readonly List<ITaskItem> _fileDefinitions = new List<ITaskItem>();
        private readonly List<ITaskItem> _packageDependencies = new List<ITaskItem>();
        private readonly List<ITaskItem> _fileDependencies = new List<ITaskItem>();

        /// <summary>
        /// All the targets in the lock file.
        /// </summary>
        [Output]
        public ITaskItem[] TargetDefinitions
        {
            get { return _targetDefinitions.ToArray(); }
        }

        /// <summary>
        /// All the libraries/packages in the lock file.
        /// </summary>
        [Output]
        public ITaskItem[] PackageDefinitions
        {
            get { return _packageDefinitions.ToArray(); }
        }

        /// <summary>
        /// All the files in the lock file.
        /// </summary>
        [Output]
        public ITaskItem[] FileDefinitions
        {
            get { return _fileDefinitions.ToArray(); }
        }

        /// <summary>
        /// All the dependencies between packages. Each package has metadata 'ParentPackage'
        /// to refer to the package that depends on it. For top level packages this value is blank.
        /// </summary>
        [Output]
        public ITaskItem[] PackageDependencies
        {
            get { return _packageDependencies.ToArray(); }
        }

        /// <summary>
        /// All the dependencies between files and packages, labeled by the group containing
        /// the file (e.g. CompileTimeAssembly, RuntimeAssembly, etc.).
        /// </summary>
        [Output]
        public ITaskItem[] FileDependencies
        {
            get { return _fileDependencies.ToArray(); }
        }

        #endregion

        #region Inputs

        /// <summary>
        /// The path to the current project.
        /// </summary>
        [Required]
        public string ProjectPath
        {
            get; set;
        }

        /// <summary>
        /// The assets file to process
        /// </summary>
        public string ProjectAssetsFile
        {
            get; set;
        }

        /// <summary>
        /// Optional the Project Language (E.g. C#, VB)
        /// </summary>
        public string ProjectLanguage
        {
            get; set;
        }

        public string TargetFramework { get; set; }

        #endregion

        public ResolvePackageDependencies()
        {
        }

        #region Test Support

        internal ResolvePackageDependencies(LockFile lockFile, IPackageResolver packageResolver)
            : this()
        {
            _lockFile = lockFile;
            _packageResolver = packageResolver;
        }

        #endregion

        private IPackageResolver PackageResolver => _packageResolver ??= NuGetPackageResolver.CreateResolver(LockFile);

        private LockFile LockFile => _lockFile ??= new LockFileCache(this).GetLockFile(ProjectAssetsFile);

        /// <summary>
        /// Raise Nuget LockFile representation to MSBuild items
        /// </summary>
        protected override void ExecuteCore()
        {

        }

        // save file type metadata based on the group the file appears in
        private void SaveFileKeyType(string fileKey, FileGroup fileGroup)
        {
            string fileType = fileGroup.GetTypeMetadata();
            if (fileType != null)
            {
                if (!_fileTypes.TryGetValue(fileKey, out string currentFileType))
                {
                    _fileTypes.Add(fileKey, fileType);
                }
                else if (currentFileType != fileType)
                {
                    throw new BuildErrorException(Strings.UnexpectedFileType, fileKey, fileType, currentFileType);
                }
            }
        }

        private string ResolvePackagePath(LockFileLibrary package)
        {
            if (package.IsProject())
            {
                var relativeMSBuildProjectPath = package.MSBuildProject;

                if (string.IsNullOrEmpty(relativeMSBuildProjectPath))
                {
                    throw new BuildErrorException(Strings.ProjectAssetsConsumedWithoutMSBuildProjectPath, package.Name, ProjectAssetsFile); 
                }

                return GetAbsolutePathFromProjectRelativePath(relativeMSBuildProjectPath);
            }
            else
            {
                return PackageResolver.GetPackageDirectory(package.Name, package.Version);
            }
        }

        private static string ResolveFilePath(string relativePath, string resolvedPackagePath)
        {
            if (NuGetUtils.IsPlaceholderFile(relativePath))
            {
                return null;
            }

            if (resolvedPackagePath == null)
            {
                return string.Empty;
            }

            if (Path.DirectorySeparatorChar != '/')
            {
                relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            }
            
            if (Path.DirectorySeparatorChar != '\\')
            {
                relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);
            }
            
            return Path.Combine(resolvedPackagePath, relativePath);
        }

        private string GetAbsolutePathFromProjectRelativePath(string path)
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ProjectPath), path));
        }
    }
}
