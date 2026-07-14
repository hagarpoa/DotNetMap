using DotNetMap.Core.Domain;
using DotNetMap.Core.Export;
using DotNetMap.Core.Extraction;
using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class TokenOutputTests
{
    [Fact]
    public void RelationPresentation_SeparatesCallsFromSignatureDeps()
    {
        var json = """
            [
              {"kind":"calls","targetId":"method:A.B","targetName":"A.B()"},
              {"kind":"usesInSignature","targetId":"type:System.Threading.Tasks.Task","targetName":"System.Threading.Tasks.Task"},
              {"kind":"implements","targetId":"type:IFoo","targetName":"IFoo"}
            ]
            """;
        var slice = RelationPresentation.Slice(json, DetailLevel.Compact, maxRelations: 20);
        Assert.Single(slice.CallNames);
        Assert.Contains("A.B", slice.CallNames);
        Assert.Empty(slice.SignatureDepNames); // compact hides signature deps
        Assert.Single(slice.StructuralNames);

        var full = RelationPresentation.Slice(json, DetailLevel.Full, maxRelations: 20);
        Assert.Single(full.SignatureDepNames);
    }

    [Fact]
    public void MemberDetailToMarkdown_Compact_ListsCallsNotRawJson()
    {
        var m = new MemberDetail(
            "method:Demo.SaveAsync",
            "SaveAsync",
            "method",
            "public Task SaveAsync()",
            "public",
            "Task",
            "Persists",
            10,
            20,
            100,
            """[{"kind":"calls","targetId":"method:Demo.CalculateTotal","targetName":"Demo.App.OrderService.CalculateTotal(System.Collections.Generic.IReadOnlyList`1)"},{"kind":"usesInSignature","targetId":"type:System.Threading.Tasks.Task","targetName":"System.Threading.Tasks.Task"}]""",
            "[]",
            40,
            "Demo.App/OrderService.cs",
            "Demo.App.OrderService");

        var md = CompactExporter.MemberDetailToMarkdown(m, new ExportOptions { Detail = DetailLevel.Compact });
        Assert.Contains("Calls", md);
        Assert.Contains("CalculateTotal", md);
        Assert.DoesNotContain("usesInSignature", md);
        Assert.DoesNotContain("dependenciesJson", md);
    }

    [Fact]
    public void ExternalSymbolFilter_FlagsBcl()
    {
        Assert.True(ExternalSymbolFilter.IsLikelyExternalAssembly("System.Runtime"));
        Assert.True(ExternalSymbolFilter.IsLikelyExternalAssembly("System"));
        Assert.True(ExternalSymbolFilter.IsLikelyExternalAssembly("Microsoft.Extensions.Logging"));
        Assert.False(ExternalSymbolFilter.IsLikelyExternalAssembly("Demo.App"));
        Assert.False(ExternalSymbolFilter.IsLikelyExternalAssembly("Demo.Core"));
    }

    [Fact]
    public async Task Index_Default_FiltersExternalCalls_KeepsSolutionCalls()
    {
        var demo = FindDemo();
        var result = await new SolutionIndexer().IndexAsync(demo, new IndexOptions
        {
            LightDeps = true,
            IncludeExternalCalls = false,
            IncludeExternalSignatureDeps = false
        });

        var save = result.Map.Projects.SelectMany(p => p.Types)
            .SelectMany(t => t.Members)
            .First(m => m.Name == "SaveAsync");

        Assert.Contains(save.Dependencies, d => d.Kind == RelationKind.Calls
                                                && d.TargetName.Contains("CalculateTotal", StringComparison.Ordinal));

        // No call whose target *id* is a BCL method (param types may still mention System.* in display)
        Assert.DoesNotContain(save.Dependencies, d =>
            d.Kind == RelationKind.Calls
            && d.TargetId.StartsWith("method:System.", StringComparison.Ordinal));

        // Signature BCL types (Task, CancellationToken) filtered; solution type Order kept
        Assert.DoesNotContain(save.Dependencies, d =>
            d.Kind == RelationKind.UsesInSignature
            && d.TargetId.StartsWith("type:System.", StringComparison.Ordinal));
        Assert.Contains(save.Dependencies, d =>
            d.Kind == RelationKind.UsesInSignature
            && d.TargetName.Contains("Order", StringComparison.Ordinal));
    }

    private static string FindDemo()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "DemoSolution")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "samples", "DemoSolution")),
        };
        foreach (var c in candidates)
        {
            if (Directory.Exists(c))
                return c;
        }
        throw new DirectoryNotFoundException("DemoSolution not found");
    }
}
