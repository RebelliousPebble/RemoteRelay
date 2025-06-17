using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.IO;
using RemoteRelay.Common;
using System.Device.Gpio; // Added for GpioController and PinValue
using Microsoft.AspNetCore.SignalR; // Added for IHubContext

namespace RemoteRelay.Server;

public class Program
{
   // Helper method to determine GPIO environment, similar to SwitcherState
   private static bool IsGpiEnvironment()
   {
      return Environment.OSVersion.Platform == PlatformID.Unix;
   }

   public static void Main(string[] args)
   {
      if (args.Length == 5 && args[0] == "set-inactive-relay" && args[1] == "--pin" && args[3] == "--state")
      {
         GpioController? gpioController = null;
         try
         {
            if (!int.TryParse(args[2], out int pin) || pin <= 0)
            {
               Console.Error.WriteLine($"Invalid pin number: {args[2]}. Must be a positive integer.");
               Environment.Exit(1);
               return;
            }

            string stateArg = args[4];
            PinValue valueToSet;

            if (stateArg.Equals("High", StringComparison.OrdinalIgnoreCase))
            {
               valueToSet = PinValue.High;
            }
            else if (stateArg.Equals("Low", StringComparison.OrdinalIgnoreCase))
            {
               valueToSet = PinValue.Low;
            }
            else
            {
               Console.Error.WriteLine($"Invalid state: {stateArg}. Must be 'High' or 'Low'.");
               Environment.Exit(1);
               return;
            }

            if (IsGpiEnvironment())
               gpioController = new GpioController(PinNumberingScheme.Logical);
            else
               gpioController = new GpioController(PinNumberingScheme.Board, new MockGpioDriver()); // Matched SwitcherState

            if (!gpioController.IsPinOpen(pin))
            {
               gpioController.OpenPin(pin, PinMode.Output);
            }
            else
            {
               var currentPinMode = gpioController.GetPinMode(pin);
               if (currentPinMode != PinMode.Output)
               {
                  Console.WriteLine($"Warning: Pin {pin} was already open with mode {currentPinMode}. Setting to Output mode.");
                  gpioController.SetPinMode(pin, PinMode.Output);
               }
            }

            gpioController.Write(pin, valueToSet);
            Console.WriteLine($"Successfully set pin {pin} to {valueToSet}");
            Environment.Exit(0);
         }
         catch (Exception ex)
         {
            Console.Error.WriteLine($"Error setting pin via command line: {ex.Message}");
            Environment.Exit(1);
         }
         finally
         {
            gpioController?.Dispose();
         }
         return; // Exit Main after handling command-line operation
      }

      // --- Existing Web Server Startup Code ---
      var _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText("config.json"));

      var builder = WebApplication.CreateBuilder(args);
      builder.Services.AddSignalR();
      builder.Services.AddSingleton<SwitcherState>(sp => new SwitcherState(_settings, sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<RelayHub>>()));
      builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(_settings.ServerPort); });

      var app = builder.Build();


      app.MapGet("/", () => "This is a SignalR Server for Remote Relay, the hub is hosted at /relay");
      app.MapHub<RelayHub>("/relay");

      // Configure application shutdown behavior
      var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
      var switcherStateInstance = app.Services.GetRequiredService<SwitcherState>();

      lifetime.ApplicationStopping.Register(() =>
      {
          Console.WriteLine("Application is stopping. Setting inactive relay to inactive state.");
          switcherStateInstance.SetInactiveRelayToInactiveState();
          // Assuming GpioController is disposed of when SwitcherState is disposed or app shuts down.
      });

      app.MapGet("/logo", async (SwitcherState switcherState, ILogger<Program> logger, IWebHostEnvironment env) =>
      {
         var appSettings = switcherState.GetSettings();
         if (string.IsNullOrWhiteSpace(appSettings.LogoFile))
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