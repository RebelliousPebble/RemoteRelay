using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Media;
using ReactiveUI;
using RemoteRelay.Common;
using RemoteRelay.Controls;

namespace RemoteRelay.SingleOutput;

public class SingleOutputViewModel : OperationViewModelBase
{
    public SingleOutputViewModel(AppSettings settings)
       : base(settings)
    {
        Inputs = settings.Sources.Select(x => new SourceButtonViewModel(x)).ToArray();

        var cancelRequested =
           Observable.Merge(CancelStream,
              Server._stateChanged.Select(_ => Unit.Default));

        IObservable<SourceButtonViewModel?> selected = Inputs
           .Select(x => x.Clicked.Select(_ => x))
           .Merge()
           .Select(x => Observable
              .Return(x)
              .Merge(
                 Observable
                    .Return((SourceButtonViewModel?)null)
                    .Delay(TimeSpan.FromSeconds(TimeoutSeconds))))
           .Merge(cancelRequested.Select(_ => Observable.Return((SourceButtonViewModel?)null)))
           .Switch()
           .Scan((a, b) => a != b ? b : null);

        var connection = Output.Clicked
           .WithLatestFrom(
              selected,
              (output, input) => (Output: output, Input: input))
           .Where(tuple => tuple.Input is not null)
           .Select(tuple => (tuple.Output, Input: tuple.Input!));

        // On selection of an input
        Disposables.Add(selected.Subscribe(x =>
        {
            x?.SetState(SourceState.Selected);
            if (x is null) return;

            // Flash the selected input if FlashOnSelect is enabled
            if (FlashOnSelect)
            {
                // Use a bright orange color for single output mode
                x.StartFlashAnimation(Colors.Orange);
            }

            PushStatusMessage(
                Observable
             .Range(0, TimeoutSeconds)
             .Select(x => TimeoutSeconds - x)
                   .Zip(
                      Observable
                         .Return(Unit.Default)
                         .Delay(TimeSpan.FromSeconds(1))
                         .Repeat(Math.Max(TimeoutSeconds - 1, 0))
                         .StartWith(Unit.Default),
                      (i, _) => $"Press confirm in the next {i} seconds to switch"));
        }));


        // On selection of an input, disable previous input
        Disposables.Add(selected.SkipLast(1).Subscribe(x => { x?.SetState(SourceState.Inactive); }));


        // On confirm
        Disposables.Add(connection.Subscribe(x =>
           {
               RequestCancel();

               // Check if server is connected before attempting to switch
               if (!Server.IsConnected)
               {
                   PushStatusMessage("Server connection lost. Please wait for reconnection.");
                   return;
               }

               var defaultOutput = Settings.Outputs.FirstOrDefault();
               if (defaultOutput is null)
               {
                   PushStatusMessage("No outputs configured.");
                   return;
               }

               Server.SwitchSource(x.Input.SourceName, defaultOutput!);
               PushStatusMessage(
                Observable
                   .Return("No response received from server")
             .Delay(TimeSpan.FromSeconds(TimeoutSeconds))
                   .StartWith("Waiting for response from server"));
           }));

        Disposables.Add(selected
           .DistinctUntilChanged()
           .Where(x => x == null)
           .Subscribe(_ => { HandleCancel(); }));
    }

    public IEnumerable<SourceButtonViewModel> Inputs { get; }

    public SourceButtonViewModel Output { get; } = new("Confirm");

    protected override void HandleStatusUpdate(IReadOnlyDictionary<string, string> newStatus)
    {
        PushStatusMessage("Updating...");
        // Update screen to show the new system status
        if (newStatus.Count != 0)
        {
            foreach (var pair in newStatus)
            {
                var input = Inputs.FirstOrDefault(x => x.SourceName == pair.Key);
                if (input == null) continue;

                if (!string.IsNullOrEmpty(pair.Value))
                {
                    Debug.WriteLine($"{pair.Key} is active");
                    input.SetState(SourceState.Active);
                    PushStatusMessage($"{pair.Key} routed to {pair.Value}");
                }
                // Set all others to inactive
                else
                {
                    Debug.WriteLine($"{pair.Key} is inactive");
                    input.SetState(SourceState.Inactive);
                }
            }

        }
        else
        {
            PushStatusMessage(string.Empty);
        }
    }
}