using System.Device.Gpio;
using RemoteRelay.Common;
using Microsoft.AspNetCore.SignalR; // Added for IHubContext
using System.Linq; // Added for LINQ methods

namespace RemoteRelay.Server;

public class SwitcherState
{
   private readonly GpioController _gpiController;
   private readonly AppSettings _settings;
   private readonly List<Source> _sources = new();
   private readonly int outputCount;
   private GpioPin? _inactiveRelayPin;
   private readonly IHubContext<RelayHub> _hubContext; // Added field

   // Debouncing for physical buttons
   private readonly Dictionary<int, DateTime> _lastPinEventTime = new();
   private static readonly TimeSpan _debounceTime = TimeSpan.FromMilliseconds(200);

   public SwitcherState(AppSettings settings, IHubContext<RelayHub> hubContext) // Added hubContext parameter
   {
      _settings = settings; // Store settings first
      _hubContext = hubContext; // Store hubContext

      if (IsGpiEnvironment())
         _gpiController = new GpioController(PinNumberingScheme.Logical);
      else
         _gpiController = new GpioController(PinNumberingScheme.Board, new MockGpioDriver());

      foreach (var source in settings.Sources)
      {
         var newSource = new Source(source);
         foreach (var outputName in
                  settings.Routes.Where(x => x.SourceName == source).Select(x => x.OutputName).Distinct())
         {
            var relayConfig = settings.Routes.First(x => x.SourceName == source && x.OutputName == outputName);
            newSource.AddOutputPin(ref _gpiController, relayConfig);
         }
         _sources.Add(newSource);
      }

      outputCount = settings.Outputs.Count;

      // Set default source
      if (settings.DefaultSource != null)
         SwitchSource(settings.DefaultSource,
            settings.Routes.First(x => x.SourceName == settings.DefaultSource).OutputName);

      // Initialize Inactive Relay Pin
      if (_settings.InactiveRelay != null && _settings.InactiveRelay.Pin > 0)
      {
         try
         {
            _inactiveRelayPin = _gpiController.OpenPin(_settings.InactiveRelay.Pin, PinMode.Output);
            _inactiveRelayPin.Write(_settings.InactiveRelay.GetActivePinValue());
            Console.WriteLine($"Inactive relay pin {_settings.InactiveRelay.Pin} initialized to active state ({_settings.InactiveRelay.GetActivePinValue()}).");
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Error initializing inactive relay pin {_settings.InactiveRelay.Pin}: {ex.Message}");
            _inactiveRelayPin = null; // Ensure it's null if setup failed
         }
      }

      // Setup physical buttons
      if (_settings.PhysicalSourceButtons != null && _gpiController != null)
      {
         foreach (var buttonEntry in _settings.PhysicalSourceButtons)
         {
            var sourceNameForButton = buttonEntry.Key;
            var buttonConfig = buttonEntry.Value;

            if (buttonConfig.PinNumber <= 0)
            {
               Console.WriteLine($"Skipping physical button for source '{sourceNameForButton}' due to invalid pin number: {buttonConfig.PinNumber}");
               continue;
            }

            Console.WriteLine($"Setting up physical button for source '{sourceNameForButton}' on pin {buttonConfig.PinNumber} with trigger state {buttonConfig.TriggerState}");
            try
            {
               _gpiController.OpenPin(buttonConfig.PinNumber, PinMode.InputPullUp);
               _gpiController.RegisterCallbackForPinValueChangedEvent(
                  buttonConfig.PinNumber,
                  buttonConfig.GetTriggerEventType(),
                  HandlePhysicalButtonChangeEvent);
               Console.WriteLine($"Successfully registered callback for pin {buttonConfig.PinNumber} for source '{sourceNameForButton}'.");
            }
            catch (Exception ex)
            {
               Console.WriteLine($"Error setting up physical button for source '{sourceNameForButton}' on pin {buttonConfig.PinNumber}: {ex.Message}");
            }
         }
      }
   }

   private void HandlePhysicalButtonChangeEvent(object sender, PinValueChangedEventArgs e)
   {
      DateTime now = DateTime.UtcNow;
      if (_lastPinEventTime.TryGetValue(e.PinNumber, out DateTime lastEventTime))
      {
         if (now - lastEventTime < _debounceTime)
         {
            Console.WriteLine($"Bounce detected on pin {e.PinNumber}. Ignoring event.");
            return;
         }
      }
      _lastPinEventTime[e.PinNumber] = now;

      Console.WriteLine($"Pin change event: Pin {e.PinNumber}, Type {e.ChangeType} (debounced)");

      var matchedButtonEntry = _settings.PhysicalSourceButtons
                                     .FirstOrDefault(b => b.Value.PinNumber == e.PinNumber);

      if (matchedButtonEntry.Key == null) // KeyValuePair is a struct, so Key will be null if not found by FirstOrDefault
      {
         Console.WriteLine($"Error: No configured button found for pin {e.PinNumber}.");
         return;
      }

      var sourceName = matchedButtonEntry.Key;
      var buttonConfig = matchedButtonEntry.Value;

      if (e.ChangeType == buttonConfig.GetTriggerEventType())
      {
         Console.WriteLine($"Physical button pressed for source '{sourceName}' on pin {e.PinNumber}.");

         var route = _settings.Routes.FirstOrDefault(r => r.SourceName == sourceName);
         if (route == null)
         {
            Console.WriteLine($"Error: No route found for source '{sourceName}' triggered by pin {e.PinNumber}.");
            return;
         }
         var targetOutputName = route.OutputName;

         SwitchSource(sourceName, targetOutputName);
         Console.WriteLine($"Switched to source '{sourceName}' output '{targetOutputName}' due to physical button press on pin {e.PinNumber}.");

         try
         {
            _hubContext.Clients.All.SendAsync("SystemState", GetSystemState()).Wait();
            Console.WriteLine($"Sent SystemState update to clients after physical button press for pin {e.PinNumber}.");
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Error sending SystemState update after physical button press for pin {e.PinNumber}: {ex.Message}");
         }
      }
      else
      {
         Console.WriteLine($"Pin event {e.ChangeType} for pin {e.PinNumber} (source '{sourceName}') was not the configured trigger event type ({buttonConfig.GetTriggerEventType()}).");
      }
   }

   public void SetInactiveRelayToInactiveState()
   {
      if (_inactiveRelayPin != null && _settings.InactiveRelay != null)
      {
         try
         {
            var inactiveValue = _settings.InactiveRelay.GetInactivePinValue();
            _inactiveRelayPin.Write(inactiveValue);
            Console.WriteLine($"Inactive relay pin {_settings.InactiveRelay.Pin} set to inactive state ({inactiveValue}).");
         }
         catch (Exception ex)
         {
            Console.WriteLine($"Error setting inactive relay pin {_settings.InactiveRelay.Pin} to inactive state: {ex.Message}");
         }
      }
   }

   public void SwitchSource(string source, string output)
   {
      if (outputCount == 1)
         foreach (var x in _sources)
            if (x._sourceName == source)
               x.EnableOutput(output);
            else
               x.DisableOutput();
      else
         foreach (var x in _sources)
            if (x._sourceName == source)
               x.EnableOutput(output);
   }

   // first value is index of source, second value is output index (-1 is not outputting)
   public Dictionary<string, string> GetSystemState()
   {
      var state = new Dictionary<string, string>();
      foreach (var x in _sources) state.Add(x._sourceName, x.GetCurrentRoute());

      return state;
   }

   private bool IsGpiEnvironment()
   {
      // Check if we are running on a Raspberry Pi or other unix-like environment
      return Environment.OSVersion.Platform == PlatformID.Unix;
   }

   public AppSettings GetSettings()
   {
      return _settings;
   }
}