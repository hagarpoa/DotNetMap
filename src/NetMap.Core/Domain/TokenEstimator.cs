namespace NetMap.Core.Domain;

/// <summary>Rough token estimate for AI context budgeting (~ chars/4).</summary>
public static class TokenEstimator
{
    public static int FromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    public static int FromParts(params string?[] parts)
    {
        var total = 0;
        foreach (var p in parts)
            total += FromText(p);
        return total;
    }

    public static int EstimateType(TypeNode type)
    {
        var n = FromParts(type.FullName, type.Summary, type.Kind.ToString());
        foreach (var m in type.Members)
            n += EstimateMember(m);
        foreach (var d in type.Dependencies)
            n += FromParts(d.TargetName, d.Kind.ToString());
        type.TokenEstimate = n;
        return n;
    }

    public static int EstimateMember(MemberNode member)
    {
        var n = FromParts(member.Signature, member.Summary, member.ReturnType);
        member.TokenEstimate = n;
        return n;
    }

    public static int EstimateOverview(SolutionMap map)
    {
        var n = FromParts(map.Name, map.Path);
        foreach (var p in map.Projects)
        {
            n += FromText(p.Name);
            foreach (var t in p.Types)
                n += EstimateType(t);
        }
        return n;
    }
}
