using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Media.Imaging;
using ReactiveUI;
using RemoteRelay.Controls;
using RemoteRelay.Common;

namespace RemoteRelay.SingleOutput;

public class SingleOutputViewModel : ViewModelBase
{
   private readonly IObservable<SourceButtonViewModel?> _selected;
   private readonly AppSettings _settings;
   private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

   private string _currentRoute;
   private Dictionary<string, string> _currentStatus;
   private string _lastStatusMessage;
   private string _selectedSource;
   private Bitmap _stationLogo;
   private string _statusMessage;

   public SingleOutputViewModel(AppSettings settings)
   {
      _settings = settings;
      Inputs = _settings.Sources.Select(x => new SourceButtonViewModel(x)).ToArray();

      _selected =
         Inputs
            .Select(x => x.Clicked.Select(_ => x))
            .Merge()
            .Select(x => Observable
               .Return(x)
               .Merge(
                  Observable
                     .Return((SourceButtonViewModel?)null)
                     .Delay(_timeout)))
            .Merge(Cancel.Clicked.Select(_ => Observable.Return((SourceButtonViewModel?)null)))
            .Merge(
               Server._stateChanged.Select(_ => Observable.Return((SourceButtonViewModel?)null)))
            .Switch()
            .Scan((a, b) => a != b ? b : null);

      var connection = Output.Clicked
         .WithLatestFrom(
            _selected,
            (output, input) => (Output: output, Input: input))
         .Where(x => x.Input != null);

      if (Path.Exists(_settings.LogoFile))
      {
         if (Path.IsPathFullyQualified(_settings.LogoFile))
            StationLogo = new Bitmap(_settings.LogoFile);
         else
            StationLogo = new Bitmap(Path.Combine(Directory.GetCurrentDirectory(), _settings.LogoFile));
      }


      // On selection of an input
      _ = _selected.Subscribe(x =>
      {
         x?.SetState(SourceState.Selected);
         if (x is not null) OnSelect(x);
      });


      // On selection of an input, disable previous input
      _ = _selected.SkipLast(1).Subscribe(x =>
      {
         if (x is not null)
            x.SetState(SourceState.Inactive);
      });


      // On confirm
      _ = connection.Subscribe(x => { Server.SwitchSource(x.Input.SourceName, _settings.Outputs.First()); });

      _ = _selected.Where(x => x == null).Distinct().Subscribe(_ => { OnCancel(); });

      // On TCP status in
      Server._stateChanged.Subscribe(x =>
      {
         foreach (var keyValuePair in x) Debug.WriteLine($"{keyValuePair.Key} : {keyValuePair.Value}");
         OnStatusUpdate(x);
      });
   }

   public IEnumerable<SourceButtonViewModel> Inputs { get; }

   public SourceButtonViewModel Output { get; } = new("Confirm");

   public SourceButtonViewModel Cancel { get; } = new("Cancel");

   public string StatusMessage
   {
      get => _statusMessage;
      set
      {
         _lastStatusMessage = _statusMessage;
         this.RaiseAndSetIfChanged(ref _statusMessage, value);
      }
   }

   protected SwitcherServer Server => SwitcherServer.Instance();

   public Bitmap StationLogo
   {
      get => _stationLogo;
      set => this.RaiseAndSetIfChanged(ref _stationLogo, value);
   }

   private void OnSelect(SourceButtonViewModel x)
   {
      _selectedSource = x.SourceName;
      StatusMessage = "Click Confirm within 10 seconds to route to " + x?.SourceName;
   }

   private void OnCancel()
   {
      OnStatusUpdate(_currentStatus);
   }

   private void OnStatusUpdate(Dictionary<string, string> newStatus)
   {
      Debug.WriteLine("Beginning key setting");

      StatusMessage = "Updating";
      // Update screen to show the new system status
      if (newStatus != null)
      {
         foreach (var key in newStatus)
            if (key.Value != "")
            {
               Debug.WriteLine($"{key.Key} is active");
               Inputs.First(x => x.SourceName == key.Key).SetState(SourceState.Active);
               StatusMessage = $"{key.Key} routed to {key.Value}";
            }
            // Set all others to inactive
            else
            {
               Debug.WriteLine($"{key.Key} is inactive");
               Inputs.First(x => x.SourceName == key.Key).SetState(SourceState.Inactive);
            }

         _currentStatus = newStatus;
      }

      Debug.WriteLine("Ending key setting");
   }
}