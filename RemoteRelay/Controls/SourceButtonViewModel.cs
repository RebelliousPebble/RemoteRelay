using System;
using System.Reactive;
using System.Reactive.Disposables;
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
    private SolidColorBrush _backgroundColor = new(Colors.DarkSlateBlue);
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
                   SourceState.Inactive => Colors.DarkSlateBlue,
                   SourceState.Selected => Colors.Red,
                   SourceState.Active => Colors.Red,
                   SourceState.Linked => _linkedColor,
                   _ => Colors.Pink
               };
               return (Background: bg, Foreground: Colors.White);
           })
           .ObserveOn(RxApp.MainThreadScheduler)
           .Subscribe(x =>
           {
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

    private IDisposable? _flashDisposable;

    public void SetState(SourceState state, Color? linkedColor = null)
    {
        if (linkedColor.HasValue)
            _linkedColor = linkedColor.Value;

        // Stop any existing flash animation
        _flashDisposable?.Dispose();
        _flashDisposable = null;

        Dispatcher.UIThread.Invoke(() => _state.OnNext(state));
    }

    /// <summary>
    /// Start a flash animation that alternates between the linked color and a highlight color.
    /// Used when FlashOnSelect is enabled and an input is selected.
    /// </summary>
    public void StartFlashAnimation(Color flashColor)
    {
        // Stop any existing flash
        _flashDisposable?.Dispose();

        const int flashCount = 6;
        const int flashIntervalMs = 150;

        _flashDisposable = Observable
           .Interval(TimeSpan.FromMilliseconds(flashIntervalMs))
           .Take(flashCount)
           .ObserveOn(RxApp.MainThreadScheduler)
           .Subscribe(
              tick =>
              {
                // Alternate between flash color and dark/transparent
                var isOn = tick % 2 == 0;
                  BackgroundColor = new SolidColorBrush(isOn ? flashColor : Colors.DarkGray);
              },
              () =>
              {
                // Animation complete - restore to Selected state color (Red)
                BackgroundColor = new SolidColorBrush(Colors.Red);
              });
    }
}

public enum SourceState
{
    Inactive,
    Selected,
    Active,
    Linked
}