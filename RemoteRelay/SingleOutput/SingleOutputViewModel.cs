using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Media.Imaging;
using ReactiveUI;
using RemoteRelay.Common;
using RemoteRelay.Controls;

namespace RemoteRelay.SingleOutput;

public class SingleOutputViewModel : ViewModelBase
{
   private readonly Subject<Unit> _cancel = new();
   private readonly Subject<IObservable<string>> _message = new();

   private readonly int _timeout = 3;

   private Dictionary<string, string> _currentStatus = new();
   private Bitmap _stationLogo;
   private string _statusMessage;


   public SingleOutputViewModel(AppSettings settings)
   {
      Inputs = settings.Sources.Select(x => new SourceButtonViewModel(x)).ToArray();

      var cancelRequested =
         Observable.Merge(_cancel,
            Cancel.Clicked,
            Server._stateChanged.Select(_ => Unit.Default));

      IObservable<SourceButtonViewModel?> selected = Inputs
         .Select(x => x.Clicked.Select(_ => x))
         .Merge()
         .Select(x => Observable
            .Return(x)
            .Merge(
               Observable
                  .Return((SourceButtonViewModel?)null)
                  .Delay(TimeSpan.FromSeconds(_timeout))))
         .Merge(cancelRequested.Select(_ => Observable.Return((SourceButtonViewModel?)null)))
         .Switch()
         .Scan((a, b) => a != b ? b : null);

      var connection = Output.Clicked
         .WithLatestFrom(
            selected,
            (output, input) => (Output: output, Input: input))
         .Where(x => x.Input != null);

      if (Path.Exists(settings.LogoFile))
      {
         if (Path.IsPathFullyQualified(settings.LogoFile))
            StationLogo = new Bitmap(settings.LogoFile);
         else
            StationLogo = new Bitmap(Path.Combine(Directory.GetCurrentDirectory(), settings.LogoFile));
      }


      // On selection of an input
      _ = selected.Subscribe(x =>
      {
         x?.SetState(SourceState.Selected);
         if (x is null) return;

         _message.OnNext(
            Observable
               .Range(0, _timeout)
               .Select(x => _timeout - x)
               .Zip(
                  Observable
                     .Return(Unit.Default)
                     .Delay(TimeSpan.FromSeconds(1))
                     .Repeat(_timeout - 1)
                     .StartWith(Unit.Default),
                  (i, _) => $"Press confirm in the next {i} seconds to switch"));
      });


      // On selection of an input, disable previous input
      _ = selected.SkipLast(1).Subscribe(x => { x?.SetState(SourceState.Inactive); });


      // On confirm
      _ = connection.Subscribe(x =>
      {
         _cancel.OnNext(Unit.Default);
         Server.SwitchSource(x.Input.SourceName, settings.Outputs.First());
         _message.OnNext(
            Observable
               .Return("No response received from server")
               .Delay(TimeSpan.FromSeconds(_timeout))
               .StartWith("Waiting for response from server"));
      });

      _ = selected
         .DistinctUntilChanged()
         .Where(x => x == null)
         .Subscribe(_ => { OnCancel(); });

      _ = _message.Switch().Subscribe(x => { StatusMessage = x; });

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
      set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
   }

   protected SwitcherClient Server => SwitcherClient.Instance;

   public Bitmap StationLogo
   {
      get => _stationLogo;
      set => this.RaiseAndSetIfChanged(ref _stationLogo, value);
   }

   private void OnCancel()
   {
      OnStatusUpdate(_currentStatus);
   }

   private void OnStatusUpdate(Dictionary<string, string> newStatus)
   {
      Debug.WriteLine("Beginning key setting");

      _message.OnNext(Observable.Return("Updating..."));
      // Update screen to show the new system status
      if (newStatus.Count != 0)
      {
         foreach (var key in newStatus)
            if (key.Value is not null or "")
            {
               Debug.WriteLine($"{key.Key} is active");
               Inputs.First(x => x.SourceName == key.Key).SetState(SourceState.Active);
               _message.OnNext(Observable.Return($"{key.Key} routed to {key.Value}"));
            }
            // Set all others to inactive
            else
            {
               Debug.WriteLine($"{key.Key} is inactive");
               Inputs.First(x => x.SourceName == key.Key).SetState(SourceState.Inactive);
            }

         _currentStatus = newStatus;
      }
      else
      {
         _message.OnNext(Observable.Return(""));
      }

      Debug.WriteLine("Ending key setting");
   }
}