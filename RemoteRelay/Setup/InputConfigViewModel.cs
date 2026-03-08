using System;
using System.Collections.ObjectModel;
using ReactiveUI;
using System.Windows.Input;
using Avalonia.Media;

namespace RemoteRelay.Setup;

/// <summary>
/// View model for a single input card in the setup UI.
/// </summary>
public class InputConfigViewModel : ViewModelBase
{
    private readonly Action<InputConfigViewModel> _deleteAction;

    private string _sourceName = string.Empty;
    public string SourceName
    {
        get => _sourceName;
        set => this.RaiseAndSetIfChanged(ref _sourceName, value);
    }

    private Color _customColorValue = Colors.LightGray;
    public Color CustomColorValue
    {
        get => _customColorValue;
        set
        {
            this.RaiseAndSetIfChanged(ref _customColorValue, value);
            _customColor = value.ToString(); // Generates #AARRGGBB
            this.RaisePropertyChanged(nameof(CustomColor));
        }
    }

    private string _customColor = string.Empty;
    public string CustomColor
    {
        get => _customColor;
        set
        {
            this.RaiseAndSetIfChanged(ref _customColor, value);
            if (!string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out var parsedColor))
            {
                _customColorValue = parsedColor;
            }
            else
            {
                _customColorValue = Colors.LightGray;
            }
            this.RaisePropertyChanged(nameof(CustomColorValue));
        }
    }

    private int _physicalButtonPin;
    public int PhysicalButtonPin
    {
        get => _physicalButtonPin;
        set => this.RaiseAndSetIfChanged(ref _physicalButtonPin, value);
    }

    private string _physicalButtonTrigger = "Low";
    public string PhysicalButtonTrigger
    {
        get => _physicalButtonTrigger;
        set => this.RaiseAndSetIfChanged(ref _physicalButtonTrigger, value);
    }

    public ObservableCollection<RouteConfigViewModel> OutputRoutes { get; } = new();

    public ICommand AddOutputRouteCommand { get; }
    public ICommand DeleteInputCommand { get; }
    public ICommand TestPhysicalButtonCommand { get; }
    public ICommand ClearColorCommand { get; }

    public InputConfigViewModel(string sourceName, string customColor, Action<InputConfigViewModel> deleteAction)
    {
        _sourceName = sourceName;
        _customColor = customColor;
        _deleteAction = deleteAction;

        AddOutputRouteCommand = ReactiveCommand.Create(AddOutputRoute);
        DeleteInputCommand = ReactiveCommand.Create(() => _deleteAction(this));
        TestPhysicalButtonCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (PhysicalButtonPin > 0)
            {
                // For input buttons, we typically just read state, but we can still test by toggling
                await SwitcherClient.Instance.TestPinAsync(PhysicalButtonPin, PhysicalButtonTrigger == "Low", true);
                await System.Threading.Tasks.Task.Delay(200);
                await SwitcherClient.Instance.TestPinAsync(PhysicalButtonPin, PhysicalButtonTrigger == "Low", false);
            }
        });

        ClearColorCommand = ReactiveCommand.Create(() => CustomColor = string.Empty);
    }

    private void AddOutputRoute()
    {
        var newName = $"Output {OutputRoutes.Count + 1}";
        OutputRoutes.Add(new RouteConfigViewModel(newName, 0, true, string.Empty, () => RemoveRoute));
    }

    public void RemoveRoute(RouteConfigViewModel route)
    {
        OutputRoutes.Remove(route);
    }
}
