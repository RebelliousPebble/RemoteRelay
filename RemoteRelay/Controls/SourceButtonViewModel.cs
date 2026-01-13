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
   private SolidColorBrush _backgroundColor = new(Colors.Gray);
   private SolidColorBrush _foregroundColor = new(Colors.White);
   private Color _linkedColor = Colors.Gray;
   private bool _isEnabled = true;

   public SourceButtonViewModel(string sourceName)
   {
      SourceName = sourceName;

      var canExecute = _state
         .Select(state => state != SourceState.Selected)
         .CombineLatest(this.WhenAnyValue(vm => vm.IsEnabled), (stateAvailable, enabled) => stateAvailable && enabled)
         .DistinctUntilChanged();

      SelectSource =
         ReactiveCommand
            .CreateFromObservable(
               () => Observable.Return(Unit.Default),
               canExecute);

      _ = _state
         .CombineLatest(this.WhenAnyValue(vm => vm.IsEnabled), (state, enabled) => (state, enabled))
         .Select(tuple =>
         {
            if (!tuple.enabled)
            {
               return (Background: Colors.DarkSlateGray, Foreground: Colors.DimGray);
            }

            var bg = tuple.state switch
            {
               SourceState.Inactive => Colors.Gray,
               SourceState.Selected => Colors.Red,
               SourceState.Active => Colors.Red,
               SourceState.Linked => _linkedColor,
               _ => Colors.Pink
            };
            return (Background: bg, Foreground: Colors.White);
         })
         .ObserveOn(RxApp.MainThreadScheduler)
         .Subscribe(x => {
             BackgroundColor = new SolidColorBrush(x.Background);
             ForegroundColor = new SolidColorBrush(x.Foreground);
         });
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

   public SolidColorBrush ForegroundColor
   {
      get => _foregroundColor;
      set => this.RaiseAndSetIfChanged(ref _foregroundColor, value);
   }

   public bool IsEnabled
   {
      get => _isEnabled;
      set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
   }
   
   public void SetState(SourceState state, Color? linkedColor = null)
   {
      if (linkedColor.HasValue)
         _linkedColor = linkedColor.Value;

      Dispatcher.UIThread.Invoke(() => _state.OnNext(state));
   }
}

public enum SourceState
{
   Inactive,
   Selected,
   Active,
   Linked
}