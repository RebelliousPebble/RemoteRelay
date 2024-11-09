using System;
using System.Reactive;
using Avalonia.Media;
using ReactiveUI;

namespace RemoteRelay.Controls;

public class SourceButtonViewModel : ViewModelBase
{
    private SolidColorBrush _backgroundColor;

    public SourceButtonViewModel(string sourceName)
    {
        SourceName = sourceName;
        SelectSource = ReactiveCommand.Create(() => { });
        IsSelected = SelectSource;
    }

    public string SourceName { get; }
    public ReactiveCommand<Unit, Unit> SelectSource { get; }

    public IObservable<Unit> IsSelected { get; }

    public SolidColorBrush BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
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