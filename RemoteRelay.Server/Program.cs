using System.Text.Json;
using RemoteRelay.Common;
using System.IO;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

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

      var switcherState = app.Services.GetRequiredService<SwitcherState>();
      var hubContext = app.Services.GetRequiredService<IHubContext<RelayHub>>();

      var watcher = new FileSystemWatcher
      {
         Path = AppDomain.CurrentDomain.BaseDirectory, // Or specify the directory where config.json is
         Filter = "config.json",
         NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
         EnableRaisingEvents = true
      };

      watcher.Changed += async (sender, e) =>
      {
         watcher.EnableRaisingEvents = false;
         try
         {
            await Task.Delay(500); // Delay to ensure file write is complete

            var newSettingsText = await File.ReadAllTextAsync("config.json");
            var newSettings = JsonSerializer.Deserialize<AppSettings>(newSettingsText);

            if (newSettings != null)
            {
               switcherState.UpdateSettings(newSettings); // Placeholder for actual update logic
               await hubContext.Clients.All.SendAsync("Configuration", newSettings); // Placeholder for actual broadcast
               Console.WriteLine("config.json reloaded and clients notified.");
            }
            else
            {
               Console.WriteLine("Error: newSettings is null after deserialization.");
            }
         }
         catch (JsonException jsonEx)
         {
            Console.WriteLine($"Error deserializing config.json: {jsonEx.Message}");
         }
         catch (Exception ex)
         {
            Console.WriteLine($"An error occurred while reloading config.json: {ex.Message}");
         }
         finally
         {
            watcher.EnableRaisingEvents = true;
         }
      };

      app.MapGet("/", () => "This is a SignalR Server for Remote Relay, the hub is hosted at /relay");
      app.MapHub<RelayHub>("/relay");

      app.Run();

      // Ensure watcher is disposed (though less critical for server apps)
      // For a console app, you might do this in a finally block or via process exit event.
      // For ASP.NET Core, IHostApplicationLifetime events could be used for more graceful shutdown.
      watcher.Dispose();
   }
}