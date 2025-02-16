using RemoteRelay.Common;
using System.Runtime;
using System.Text.Json;

namespace RemoteRelay.Server
{
   public class Program
   {
      public static void Main(string[] args)
      {

         var _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText("config.json"));

         var builder = WebApplication.CreateBuilder(args);
         builder.Services.AddSignalR();
         builder.Services.AddSingleton<SwitcherState>(_ => new SwitcherState(_settings));

         var app = builder.Build();

         app.MapGet("/", () => "This is a SignalR Server for Remote Relay, the hub is hosted at /relay");
         app.MapHub<RelayHub>("/relay");

         app.Run();
      }
   }
}
