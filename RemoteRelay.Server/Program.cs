using System.Text.Json;
using RemoteRelay.Common;

namespace RemoteRelay.Server;

public class Program
{
   public static void Main(string[] args)
   {
      var _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText("config.json"));

      var builder = WebApplication.CreateBuilder(args);
      builder.Services.AddSignalR();
      builder.Services.AddSingleton<SwitcherState>(_ => new SwitcherState(_settings));
      builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(_settings.ServerPort); });

      var app = builder.Build();


      app.MapGet("/", () => "This is a SignalR Server for Remote Relay, the hub is hosted at /relay");
      app.MapHub<RelayHub>("/relay");

      app.Run();
   }
}