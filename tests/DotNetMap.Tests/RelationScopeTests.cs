using DotNetMap.Core.Extraction;

namespace DotNetMap.Tests;

public class RelationScopeTests
{
    [Theory]
    [InlineData("full", RelationScopeKind.Full, null)]
    [InlineData("type:Demo.Core.Order", RelationScopeKind.Type, "Demo.Core.Order")]
    [InlineData("project:Demo.App", RelationScopeKind.Project, "Demo.App")]
    [InlineData("class:Foo", RelationScopeKind.Type, "Foo")]
    public void Parse_Valid(string spec, RelationScopeKind kind, string? name)
    {
        var s = RelationScope.Parse(spec);
        Assert.Equal(kind, s.Kind);
        Assert.Equal(name, s.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("type:")]
    [InlineData("unknown:x")]
    public void Parse_Invalid(string spec)
    {
        Assert.ThrowsAny<Exception>(() => RelationScope.Parse(spec));
    }
}
