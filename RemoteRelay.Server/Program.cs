using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.IO;
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

      app.MapGet("/logo", async (SwitcherState switcherState, ILogger<Program> logger, IWebHostEnvironment env) =>
      {
         var appSettings = switcherState.GetSettings();
         if (appSettings == null || string.IsNullOrWhiteSpace(appSettings.LogoFile))
         {
            logger.LogWarning("Logo file path is not configured.");
            return Results.NotFound("Logo file path is not configured.");
         }

         var logoPath = appSettings.LogoFile;
         if (!Path.IsPathRooted(logoPath))
         {
            logoPath = Path.Combine(env.ContentRootPath, logoPath);
         }

         if (!File.Exists(logoPath))
         {
            logger.LogError($"Logo file not found at path: {logoPath}");
            return Results.NotFound($"Logo file not found at path: {logoPath}");
         }

         try
         {
            var fileBytes = await File.ReadAllBytesAsync(logoPath);
            logger.LogInformation($"Successfully served logo file: {logoPath}");
            return Results.File(fileBytes, "image/jpeg"); // Assuming JPEG
         }
         catch (Exception ex)
         {
            logger.LogError(ex, $"Error reading logo file: {logoPath}");
            return Results.Problem($"Error reading logo file: {ex.Message}");
         }
      });

      app.Run();
   }
}