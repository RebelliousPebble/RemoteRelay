using System;
using Avalonia;
using Avalonia.ReactiveUI;
using RemoteRelay.Common;

namespace RemoteRelay;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--version")
        {
            Console.WriteLine(RemoteRelay.Common.VersionHelper.GetVersion());
            return;
        }

        BuildAvaloniaApp()
           .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
           .UsePlatformDetect()
           .WithInterFont()
           .LogToTrace().UseSkia().UseReactiveUI();
        ;
    }
}