namespace Demo.MultiTfm;

/// <summary>
/// Cross-TFM sample type (DNM-018). Available on net8.0 and net10.0.
/// </summary>
public static class SharedApi
{
    public static string FrameworkLabel =>
#if NET10_0_OR_GREATER
        "net10+";
#else
        "net8";
#endif

    public static int Add(int a, int b) => a + b;
}
