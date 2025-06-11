using System.Device.Gpio;
using RemoteRelay.Common;

namespace RemoteRelay.Server;

public class SwitcherState
{
   private readonly GpioController _gpiController;
   private readonly AppSettings _settings;
   private readonly List<Source> _sources = new();
   private readonly int outputCount;
   private GpioPin? _inactiveRelayPin;

   public SwitcherState(AppSettings settings)
   {
      _settings = settings; // Store settings first

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