using System;
using System.IO;
using Xunit;

namespace tktco.UdonSharpLinter.Tests;

public class UnityAssemblyReferenceTests
{
    [Fact]
    public void MissingDirectory_ReturnsEmptyList()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), "UdonSharpLinterTests_" + Guid.NewGuid());

        var references = Program.LoadUnityAssemblyReferences(missingDir);

        Assert.Empty(references);
    }

    [Fact]
    public void ArbitrarilyNamedDlls_AreAllReferenced()
    {
        // Modern Unity splits UnityEngine/VRC SDK across many per-module DLLs
        // (UnityEngine.CoreModule.dll, VRC.Udon.dll, ...) rather than the 3
        // fixed names this used to look for, so any *.dll should be picked up.
        var dir = Path.Combine(Path.GetTempPath(), "UdonSharpLinterTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            // Reuse real, valid assemblies already on disk under arbitrary
            // Unity-module-style names so MetadataReference.CreateFromFile succeeds.
            File.Copy(typeof(object).Assembly.Location, Path.Combine(dir, "UnityEngine.CoreModule.dll"));
            File.Copy(typeof(Console).Assembly.Location, Path.Combine(dir, "VRC.Udon.dll"));
            File.Copy(typeof(System.Linq.Enumerable).Assembly.Location, Path.Combine(dir, "VRC.SDK3.dll"));

            var references = Program.LoadUnityAssemblyReferences(dir);

            Assert.Equal(3, references.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

}
