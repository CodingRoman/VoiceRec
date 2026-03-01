using Avalonia;
using Velopack;
using System;

namespace VoiceRec;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack initialization
        VelopackApp.Build().Run();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
