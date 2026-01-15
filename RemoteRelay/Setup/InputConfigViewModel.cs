using System;
using System.Collections.ObjectModel;
using ReactiveUI;
using System.Windows.Input;

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

    public InputConfigViewModel(string sourceName, Action<InputConfigViewModel> deleteAction)
    {
        _sourceName = sourceName;
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
    }

    private void AddOutputRoute()
    {
        var newName = $"Output {OutputRoutes.Count + 1}";
        OutputRoutes.Add(new RouteConfigViewModel(newName, 0, true, false, string.Empty, () => RemoveRoute));
    }

    public void RemoveRoute(RouteConfigViewModel route)
    {
        OutputRoutes.Remove(route);
    }
}
