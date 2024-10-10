using System;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;

namespace RemoteRelay.Controls;

public class SourceButtonViewModel : ViewModelBase
{
    public string SourceName { get; }
    public ReactiveCommand<Unit, Unit> SelectSource { get; }

    public IObservable<Unit> IsSelected { get; }
    private SolidColorBrush _backgroundColor;

    public SolidColorBrush BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }

    public SourceButtonViewModel(string sourceName)
    {
        SourceName = sourceName;
        SelectSource = ReactiveCommand.Create(() => { });
        IsSelected = SelectSource;
    }

    public void SetInactiveColour()
    {
        BackgroundColor = new SolidColorBrush(Colors.Gray);
    }
    
    public void SetSelectedColour()
    {
        BackgroundColor = new SolidColorBrush(Colors.DeepSkyBlue);
    }
    
    public void SetActiveColour()
    {
        BackgroundColor = new SolidColorBrush(Colors.Red);
    }


}
