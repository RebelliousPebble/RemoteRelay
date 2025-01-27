using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Controls;
using ReactiveUI;
using RemoteRelay.Controls;

namespace RemoteRelay.SingleOutput;

public class SingleOutputViewModel : ViewModelBase
{
   private readonly IObservable<SourceButtonViewModel?> _selected;
   private readonly AppSettings _settings;
   private readonly TimeSpan _timeout = TimeSpan.FromSeconds(3);

   private string _currentRoute;
   private string _selectedSource;
   private Image _stationLogo;
   private string _statusMessage;

   public SingleOutputViewModel(AppSettings settings)
   {
      _settings = settings;
      Inputs = _settings.Sources.Select(x => new SourceButtonViewModel(x)).ToArray();

      _selected =
         Inputs
            .Select(
               x => x.IsSelected.Select(_ => x))
            .Merge()
            .Select(x =>
               Observable
                  .Return(x)
                  .Merge(
                     Observable
                        .Return((SourceButtonViewModel)null)
                        .Delay(_timeout)))
            .Merge(Cancel.IsSelected.Select(_ => Observable.Return((SourceButtonViewModel)null)))
            .Switch()
            .Scan((a, b) => a != b ? b : null);

      var connection = Output.IsSelected
         .WithLatestFrom(
            _selected,
            (output, input) => (Output: output, Input: input))
         .Where(x => x.Input != null);

      var foo = _selected.Where(x => x != null);


      // On selection of an input
      _ = _selected.Subscribe(x =>
      {
         x?.SetSelectedColour();
         if (x is not null) OnSelect(x);
      });


      // On selection of an input, disable previous input
      _ = _selected.SkipLast(1).Subscribe(x =>
      {
         if (x is not null)
            x.SetInactiveColour();
      });


      // On confirm
      _ = connection.Subscribe(x =>
      {
         SwitcherServer.Instance().SwitchSource(x.Input.SourceName, _settings.Outputs.First());
      });

      // On TCP status in
      SwitcherServer.Instance()._stateChanged.Subscribe(OnStatusUpdate);
   }

   public IEnumerable<SourceButtonViewModel> Inputs { get; }

   public SourceButtonViewModel Output { get; } = new("Confirm");

   public SourceButtonViewModel Cancel { get; } = new("Cancel");

   public string StatusMessage
   {
      get => _statusMessage;
      set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
   }

   public Image StationLogo
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
      StatusMessage = "Output routed to " + _currentRoute;
   }

   private void OnTimeoutCounter()
   {
      StatusMessage = "Timeout";
   }

   private void OnStatusUpdate(Dictionary<string, string> newStatus)
   {
      StatusMessage = "Updating";
      // Update screen to show thw new system status
      if (newStatus != null)
      {
         foreach (var key in newStatus)
         {
            if (key.Value != "")
            {
               Inputs.First(x => x.SourceName == key.Key).SetActiveColour();
               StatusMessage = $"{key.Key} routed to {key.Value}";
            }
            // Set all others to inactive
            else
            {
               Inputs.First(x => x.SourceName == key.Key).SetInactiveColour();
            }
         }
      }
   }
}