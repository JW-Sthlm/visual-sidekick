using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisualSidekick.Server;
using System.Diagnostics;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

if (args.Length > 0)
{
    using var diagnostics = new WindowCaptureService();

    if (args[0] == "--list-windows")
    {
        foreach (var window in diagnostics.ListWindows())
        {
            Console.WriteLine($"{window.Id}\t{window.ProcessName}\t{window.Width}x{window.Height}\t{window.Title}");
        }

        return;
    }

    if (args[0] == "--capture" && args.Length >= 3)
    {
        diagnostics.Start(args[1], null, 1000, showPicker: false);
        var frame = diagnostics.GetCurrentFrame(forceCapture: true);
        await File.WriteAllBytesAsync(args[2], frame.PngBytes);
        Console.WriteLine($"Captured {frame.Width}x{frame.Height} using {frame.CaptureMethod}.");
        return;
    }

    if (args[0] == "--sdk-test" && args.Length >= 2)
    {
        await using var analysis = new CopilotAnalysisService(
            ModelConfiguration.Resolve(args.Length >= 3 ? args[2] : null));
        var image = await File.ReadAllBytesAsync(args[1]);
        var response = await analysis.AnalyzeAsync(
            image,
            "Review this screen. State the main point in one concise sentence.",
            CancellationToken.None);
        Console.WriteLine(response);
        return;
    }

    if (args[0] == "--sdk-benchmark" && args.Length >= 3)
    {
        await using var analysis = new CopilotAnalysisService(args[2]);
        var image = await File.ReadAllBytesAsync(args[1]);
        for (var run = 1; run <= 2; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            double? firstDeltaSeconds = null;
            var response = await analysis.AnalyzeAsync(
                image,
                "Review this screen. If it contains a question, give the best answer and a brief reason.",
                CancellationToken.None,
                _ => firstDeltaSeconds ??= stopwatch.Elapsed.TotalSeconds);
            stopwatch.Stop();
            Console.WriteLine(
                $"RUN {run}: first text {firstDeltaSeconds?.ToString("F1") ?? "n/a"}s, " +
                $"complete {stopwatch.Elapsed.TotalSeconds:F1}s | {response}");
        }

        return;
    }

    if (args[0] == "--sdk-rotation-test" && args.Length >= 4)
    {
        await using var analysis = new CopilotAnalysisService(args[2]);
        var image = await File.ReadAllBytesAsync(args[1]);
        var count = int.Parse(args[3]);
        for (var run = 1; run <= count; run++)
        {
            var response = await analysis.AnalyzeAsync(
                image,
                $"Screen update {run}. Reply with only the number {run}.",
                CancellationToken.None);
            Console.WriteLine($"RUN {run}: {response}");
        }

        Console.WriteLine($"ROTATIONS: {analysis.SessionRotationCount}");
        return;
    }

    if (args[0] == "--live" && args.Length >= 2)
    {
        diagnostics.Start(args[1], null, 500, showPicker: false);
        await using var live = new LiveObserverManager(diagnostics);
        live.Start(ModelConfiguration.Resolve(args.Length >= 3 ? args[2] : null));
        while (live.IsRunning)
        {
            await Task.Delay(250);
        }

        return;
    }
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<WindowCaptureService>();
builder.Services.AddSingleton<LiveObserverManager>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<VisualSidekickTools>();

await builder.Build().RunAsync();
