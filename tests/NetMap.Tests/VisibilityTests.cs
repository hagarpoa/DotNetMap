using NetMap.Core.Extraction;

namespace NetMap.Tests;

public class VisibilityTests
{
    [Theory]
    [InlineData("MyApp.Tests", null, true)]
    [InlineData("MyApp.Test", null, true)]
    [InlineData("MyApp", null, false)]
    [InlineData("Demo", @"C:\src\Tests\Demo\Demo.csproj", true)]
    public void LooksLikeTestProject(string name, string? path, bool expected)
    {
        Assert.Equal(expected, Visibility.LooksLikeTestProject(name, path));
    }

    [Theory]
    [InlineData(@"C:\src\obj\Debug\foo.cs", true)]
    [InlineData(@"C:\src\bin\Debug\foo.cs", true)]
    [InlineData(@"C:\src\Data\Migrations\20240101.cs", true)]
    [InlineData(@"C:\src\Demo\Order.cs", false)]
    [InlineData(null, true)]
    public void ShouldSkipSourcePath(string? path, bool expected)
    {
        Assert.Equal(expected, Visibility.ShouldSkipSourcePath(path));
    }

    [Fact]
    public void ContentHasher_IsStable()
    {
        var a = ContentHasher.Sha256Hex("hello");
        var b = ContentHasher.Sha256Hex("hello");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }
}
