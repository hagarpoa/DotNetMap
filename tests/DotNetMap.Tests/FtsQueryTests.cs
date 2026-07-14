using DotNetMap.Core.Store;

namespace DotNetMap.Tests;

public class FtsQueryTests
{
    [Fact]
    public void ToMatchExpression_BuildsPrefixAnd()
    {
        var expr = FtsQuery.ToMatchExpression("Order Service");
        Assert.Contains("AND", expr);
        Assert.Contains("Order", expr);
        Assert.Contains("Service", expr);
        Assert.Contains("*", expr);
    }

    [Fact]
    public void ToMatchExpression_Empty_IsSafe()
    {
        var expr = FtsQuery.ToMatchExpression("   ");
        Assert.False(string.IsNullOrWhiteSpace(expr));
    }

    [Fact]
    public void ToLikePattern_WrapsPercent()
    {
        Assert.Equal("%Order%", FtsQuery.ToLikePattern("Order"));
    }
}
