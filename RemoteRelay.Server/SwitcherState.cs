using System.Device.Gpio;
using RemoteRelay.Common;

namespace RemoteRelay.Server;

public class SwitcherState
{
   private GpioController _gpiController;
   private AppSettings _settings;
   private List<Source> _sources = new();
   private int outputCount;

   public SwitcherState(AppSettings settings)
   {
      if (IsGpiEnvironment())
         _gpiController = new GpioController(PinNumberingScheme.Logical);
      else
         _gpiController = new GpioController(PinNumberingScheme.Board, new MockGpioDriver());

      InitializeStateFromSettings(settings);
   }

   private void InitializeStateFromSettings(AppSettings newSettings)
   {
      _settings = newSettings;
      _sources.Clear(); // Clear existing sources, assuming GpioController handles pin cleanup or re-registration is safe

      // It might be necessary to explicitly dispose of pins in _gpiController if they were registered
      // and GpioController doesn't automatically release them when Source objects are cleared or pins are re-registered.
      // For now, we assume re-initializing _sources and their pins is sufficient.

      if (_gpiController == null) // Ensure GpioController is initialized
      {
         if (IsGpiEnvironment())
            _gpiController = new GpioController(PinNumberingScheme.Logical);
         else
            _gpiController = new GpioController(PinNumberingScheme.Board, new MockGpioDriver());
      }
      else // If GpioController exists, ensure it's not disposed if we plan to reuse it.
      {
         // If pins are registered and need to be closed before re-registering:
         // foreach (var sourceEntry in _sources) { sourceEntry.ClosePins(_gpiController); }
         // _sources.Clear();
         // For simplicity, we're currently relying on new Source objects re-registering.
      }


      foreach (var sourceName in newSettings.Sources)
      {
         var newSource = new Source(sourceName);
         if (newSettings.Routes != null)
         {
            foreach (var route in
                     newSettings.Routes.Where(x => x.SourceName == sourceName).Select(x => x.OutputName).Distinct())
               newSource.AddOutputPin(ref _gpiController, route,
                  newSettings.Routes.First(x => x.SourceName == sourceName && x.OutputName == route).RelayPin);
         }
         _sources.Add(newSource);
      }

      outputCount = newSettings.Outputs?.Count ?? 0;

      // Set default source
      if (newSettings.DefaultSource != null && newSettings.Routes != null && newSettings.Routes.Any(x => x.SourceName == newSettings.DefaultSource))
      {
         var defaultRoute = newSettings.Routes.FirstOrDefault(x => x.SourceName == newSettings.DefaultSource);
         if (defaultRoute != null) // Ensure a route exists for the default source
         {
            SwitchSource(newSettings.DefaultSource, defaultRoute.OutputName);
         }
      }
      else if (outputCount > 0 && _sources.Any() && newSettings.Outputs.Any()) // Fallback if no default or default is invalid
      {
          // If no default source, or default source has no routes, try to set a valid initial state.
          // For example, switch the first source to the first output.
          // This part might need more sophisticated logic based on application requirements.
          // For now, we ensure SwitchSource is called only with valid, existing source and output.
          var firstSourceWithRoutes = _sources.FirstOrDefault(s => newSettings.Routes.Any(r => r.SourceName == s._sourceName));
          if (firstSourceWithRoutes != null)
          {
              var firstRouteForSource = newSettings.Routes.First(r => r.SourceName == firstSourceWithRoutes._sourceName);
              SwitchSource(firstSourceWithRoutes._sourceName, firstRouteForSource.OutputName);
          }
      }
   }

   public void UpdateSettings(AppSettings newSettings)
   {
      InitializeStateFromSettings(newSettings);
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