using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotNetMap.Cli.Mcp;

public static class McpHost
{
    public static async Task<int> RunAsync(string databasePath, CancellationToken cancellationToken)
    {
        DotNetMapTools.DatabasePath = Path.GetFullPath(databasePath);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o =>
        {
            // MCP stdio: never write logs to stdout
            o.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "dotnetmap",
                    Version = "0.3.0"
                };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly()
            .WithPromptsFromAssembly()
            .WithResourcesFromAssembly();

        var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
