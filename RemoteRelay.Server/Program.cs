using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR; // Added for IHubContext
using Microsoft.Extensions.Logging;
using System.Device.Gpio;
using RemoteRelay.Common;
using RemoteRelay.Server.Configuration;
using RemoteRelay.Server.Services;

namespace RemoteRelay.Server;

public class Program
{
   // Helper method to determine GPIO environment
   private static bool IsGpiEnvironment(bool useMockGpio)
   {
      if (useMockGpio)
         return false;

      if (Environment.OSVersion.Platform != PlatformID.Unix)
         return false;

      // Check if GPIO is actually available on the system
      // On Linux, GPIO hardware is typically exposed via /sys/class/gpio
      return Directory.Exists("/sys/class/gpio");
   }

   public static void Main(string[] args)
   {
      if (args.Length > 0 && args[0] == "--version")
      {
         Console.WriteLine(RemoteRelay.Common.VersionHelper.GetVersion());
         return;
      }
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

            if (IsGpiEnvironment(false))
               gpioController = new GpioController();
            else
               gpioController = new GpioController(new MockGpioDriver());

            if (!gpioController!.IsPinOpen(pin))
            {
               gpioController!.OpenPin(pin, PinMode.Output);
            }
            else
            {
               var currentPinMode = gpioController!.GetPinMode(pin);
               if (currentPinMode != PinMode.Output)
               {
                  Console.WriteLine($"Warning: Pin {pin} was already open with mode {currentPinMode}. Setting to Output mode.");
                  gpioController!.SetPinMode(pin, PinMode.Output);
               }
            }

            gpioController!.Write(pin, valueToSet);
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
      var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
      var initialSettings = LoadInitialSettings(configPath);

      var builder = WebApplication.CreateBuilder(args);
      builder.Services.AddSignalR();
      builder.Services.AddSingleton<SwitcherState>(sp =>
      {
         var hubContext = sp.GetRequiredService<IHubContext<RelayHub>>();
         var logger = sp.GetRequiredService<ILogger<SwitcherState>>();
         return new SwitcherState(initialSettings, hubContext, logger);
      });
      builder.Services.AddSingleton(sp =>
      {
         var switcherState = sp.GetRequiredService<SwitcherState>();
         var logger = sp.GetRequiredService<ILogger<ConfigurationWatcher>>();
         return new ConfigurationWatcher(configPath, switcherState, logger);
      });
      builder.Services.AddHostedService(sp => sp.GetRequiredService<ConfigurationWatcher>());

      builder.WebHost.ConfigureKestrel(options => { options.ListenAnyIP(initialSettings.ServerPort); });

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

      app.MapGet("/logo", async (SwitcherState switcherState, ILogger<Program> logger, IWebHostEnvironment env, CancellationToken cancellationToken) =>
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
            var (data, contentType) = await ImageCompressor.LoadAndCompressAsync(logoPath, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Successfully served compressed logo file: {LogoPath} (content-type: {ContentType})", logoPath, contentType);
            return Results.File(data, contentType);
         }
         catch (Exception ex)
         {
            logger.LogError(ex, $"Error reading logo file: {logoPath}");
            return Results.Problem($"Error reading logo file: {ex.Message}");
         }
      });

      app.Run();
   }

   private static AppSettings LoadInitialSettings(string configPath)
   {
      if (!File.Exists(configPath))
      {
         throw new FileNotFoundException($"Configuration file not found at '{configPath}'.", configPath);
      }

      var json = File.ReadAllText(configPath);
      var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
      {
         PropertyNameCaseInsensitive = true,
         ReadCommentHandling = JsonCommentHandling.Skip,
         AllowTrailingCommas = true
      });

      if (settings.Routes == null)
      {
         throw new InvalidOperationException("Unable to deserialize configuration file into AppSettings.");
      }

      if (!AppSettingsValidator.TryValidate(settings, out var summary))
      {
         throw new InvalidOperationException(summary);
      }

      return settings;
   }
}