// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Build.Framework;
using Xunit;
using static Microsoft.NET.Build.Tasks.ResolvePackageAssets;

namespace Microsoft.NET.Build.Tasks.UnitTests
{
    public class GivenThatWeWantToGetDependenciesViaDesignTimeBuild
    {
        [Fact(Skip = "TODO: fix packagePath from _packagePathResolver returns null")]
        public void ItShouldNotReturnPackagesWithUnknownTypes()
        {
            string projectAssetsJsonPath = Path.GetTempFileName();
            string projectCacheAssetsJsonPath = Path.GetTempFileName();
            var assetsContent =
"""
{
  "version" : 3,
  "targets" : {
    "net6.0" : {
      "Newtonsoft.Json/12.0.1" : {
        "type" : "package",
        "compile" : {
            "lib/netstandard2.0/Newtonsoft.Json.dll" : {
                "related" : ".pdb;.xml"
            }
        },
        "runtime" : {
            "lib/netstandard2.0/Newtonsoft.Json.dll" : {
                "related": ".pdb;.xml"
            }
        }
      },
      "Newtonsoft.Json.Bson/1.0.2" : {
          "type" : "package",
          "dependencies": {
              "Newtonsoft.Json" : "12.0.1"
          },
          "compile" : {
              "lib/netstandard2.0/Newtonsoft.Json.Bson.dll" : {
                  "related" : ".pdb;.xml"
              }
          },
          "runtime" : {
              "lib/netstandard2.0/Newtonsoft.Json.Bson.dll" : {
                  "related" : ".pdb;.xml"
               }
          }
      }
    }
  },
  "libraries" : {
    "Newtonsoft.Json/12.0.1" : {
        "sha512" : "xyz",
        "type" : "package",
        "path" : "newtonsoft.json/12.0.1",
        "files" : [
            "lib/net20/Newtonsoft.Json.dll",
            "lib/net20/Newtonsoft.Json.pdb",
            "lib/net20/Newtonsoft.Json.xml"
        ]
    },
    "Newtonsoft.Json.Bson/1.0.2" : {
        "sha512" : "abc",
        "type" : "package",
        "path" : "newtonsoft.json.bson/1.0.2",
        "files" : [
            "lib/net45/Newtonsoft.Json.Bson.dll",
            "lib/net45/Newtonsoft.Json.Bson.pdb",
            "lib/net45/Newtonsoft.Json.Bson.xml"
        ]
    }
  },
  "packageFolders": {
    "C:\\.nuget\\packages\\" : {}
  },
  "project" : {
      "version" : "1.0.0",
      "frameworks" : {
          "net6.0": {
              "targetAlias" : "net6.0",
              "dependencies" : {
                  "Newtonsoft.Json.Bson" : {
                      "target" : "Package",
                      "version" : "[1.0.2, )"
                   }
              }
          }
      }
  }
}
""";
            File.WriteAllText(projectAssetsJsonPath, assetsContent);
            var task = InitializeTask(out _);
            task.ProjectAssetsFile = projectAssetsJsonPath;
            task.ProjectAssetsCacheFile = projectCacheAssetsJsonPath;
            task.TargetFramework = "net6.0";
            task.Execute();

            var item = task.PackageDependenciesDesignTime.Single();

            Assert.Equal("", item.ItemSpec);
            Assert.Equal("", item.GetMetadata(MetadataKeys.Name));
            Assert.Equal("", item.GetMetadata(MetadataKeys.Version));
            Assert.Equal("", item.GetMetadata(MetadataKeys.IsImplicitlyDefined));
            Assert.Equal("", item.GetMetadata(MetadataKeys.Resolved));
            Assert.Equal("", item.GetMetadata(MetadataKeys.Path));
            Assert.Equal("", item.GetMetadata(MetadataKeys.DiagnosticLevel));
        }

        private ResolvePackageAssets InitializeTask(out IEnumerable<PropertyInfo> inputProperties)
        {
            inputProperties = typeof(ResolvePackageAssets)
                .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)
                .Where(p => !p.IsDefined(typeof(OutputAttribute)) &&
                            p.Name != nameof(ResolvePackageAssets.DesignTimeBuild))
                .OrderBy(p => p.Name, StringComparer.Ordinal);

            var requiredProperties = inputProperties
                .Where(p => p.IsDefined(typeof(RequiredAttribute)));

            var task = new ResolvePackageAssets();
            // Initialize all required properties as a genuine task invocation would. We do this
            // because HashSettings need not defend against required parameters being null.
            foreach (var property in requiredProperties)
            {
                property.PropertyType.Should().Be(
                    typeof(string),
                    because: $"this test hasn't been updated to handle non-string required task parameters like {property.Name}");

                property.SetValue(task, "_");
            }

            task.BuildEngine = new MockBuildEngine();

            return task;
        }
    }
}

