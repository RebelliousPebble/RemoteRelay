using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Subjects;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;

namespace RemoteRelay.Controls;

public class SourceButtonViewModel : ViewModelBase
{
    private SolidColorBrush _backgroundColor = new(Colors.DarkGray);

    public SourceButtonViewModel(string sourceName)
    {
        SourceName = sourceName;
      SelectSource = ReactiveCommand.Create(() =>
      {
         Debug.WriteLine("Create did a thing"); _foo.OnNext(Unit.Default); Debug.WriteLine("Create did a thing again"); });

      IsSelected.Subscribe(_ => Debug.WriteLine("Proof it did a thing"));
   }

    public string SourceName { get; }
    
    public ReactiveCommand<Unit, Unit> SelectSource { get; }

    public IObservable<Unit> IsSelected => _foo;

   private readonly Subject<Unit> _foo = new();


    public SolidColorBrush BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }

    public void SetInactiveColour()
    {
       Debug.WriteLine("SettingInactive");
      Dispatcher.UIThread.Invoke(() => BackgroundColor = new SolidColorBrush(Colors.Gray));

    }

    public void SetSelectedColour()
    {
       Debug.WriteLine("SettingSelected");
       Dispatcher.UIThread.Invoke(() => BackgroundColor = new SolidColorBrush(Colors.DeepSkyBlue));
   }

    public void SetActiveColour()
    {
        BackgroundColor = new SolidColorBrush(Colors.Red);
    }
}