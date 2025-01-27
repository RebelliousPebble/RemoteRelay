using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;

namespace RemoteRelay.Controls;

public class SourceButtonViewModel : ViewModelBase
{
   private readonly BehaviorSubject<SourceState> _state = new(SourceState.Inactive);
   private SolidColorBrush _backgroundColor = new(Colors.DarkGray);

   public SourceButtonViewModel(string sourceName)
   {
      SourceName = sourceName;

      SelectSource =
         ReactiveCommand
            .CreateFromObservable(
               () => Observable.Return(Unit.Default),
               _state.Select(x => x != SourceState.Active));

      _ = _state
         .Select(state => state switch
         {
            SourceState.Inactive => Colors.Gray,
            SourceState.Selected => Colors.DeepSkyBlue,
            SourceState.Active => Colors.Red,
            _ => Colors.Pink
         })
         .ObserveOn(SynchronizationContext.Current!)
         .Subscribe(x => BackgroundColor = new SolidColorBrush(x));
      ;
   }

   public string SourceName { get; }

   public ReactiveCommand<Unit, Unit> SelectSource { get; }

   public IObservable<Unit> Clicked => SelectSource;

   public SolidColorBrush BackgroundColor
   {
      get => _backgroundColor;
      set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
   }
   
   public void SetState(SourceState state)
   {
      Dispatcher.UIThread.Invoke(() => _state.OnNext(state));
   }
}

public enum SourceState
{
   Inactive,
   Selected,
   Active
}