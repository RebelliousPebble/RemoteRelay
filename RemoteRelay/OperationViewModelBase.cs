using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;
using RemoteRelay.Common;
using RemoteRelay.Controls;

namespace RemoteRelay;

public abstract class OperationViewModelBase : ViewModelBase
{
   private readonly Subject<Unit> _cancelRequests = new();
   private readonly Subject<IObservable<string>> _messageQueue = new();

   private Bitmap? _stationLogo;
   private string _statusMessage = string.Empty;

   protected OperationViewModelBase(AppSettings settings, int timeoutSeconds = 3)
   {
      Settings = settings;
      TimeoutSeconds = timeoutSeconds;
      Cancel = new SourceButtonViewModel("Cancel");

      Cancel.Clicked.Subscribe(_ => _cancelRequests.OnNext(Unit.Default));

      _messageQueue
         .Switch()
         .ObserveOn(RxApp.MainThreadScheduler)
         .Subscribe(message => StatusMessage = message);

      _cancelRequests
         .ObserveOn(RxApp.MainThreadScheduler)
         .Subscribe(_ => HandleCancel());

      SwitcherClient.Instance._stateChanged
         .ObserveOn(RxApp.MainThreadScheduler)
         .Subscribe(status =>
         {
            CurrentStatus = status;
            HandleStatusUpdate(status);
         });

      _ = LoadStationLogoAsync();
   }

   protected AppSettings Settings { get; }

   protected IReadOnlyDictionary<string, string> CurrentStatus { get; private set; } =
      new Dictionary<string, string>();

   protected SwitcherClient Server => SwitcherClient.Instance;

   protected int TimeoutSeconds { get; }

   public SourceButtonViewModel Cancel { get; }

   public string StatusMessage
   {
      get => _statusMessage;
      protected set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
   }

   public Bitmap? StationLogo
   {
      get => _stationLogo;
      protected set => this.RaiseAndSetIfChanged(ref _stationLogo, value);
   }

   protected IObservable<Unit> CancelStream => _cancelRequests.AsObservable();

   protected void RequestCancel() => _cancelRequests.OnNext(Unit.Default);

   protected void PushStatusMessage(string message) =>
      _messageQueue.OnNext(Observable.Return(message));

   protected void PushStatusMessage(IObservable<string> messageStream) =>
      _messageQueue.OnNext(messageStream);

   protected virtual void HandleCancel()
   {
      HandleStatusUpdate(CurrentStatus);
   }

   protected abstract void HandleStatusUpdate(IReadOnlyDictionary<string, string> newStatus);

   private async Task LoadStationLogoAsync()
   {
      if (SwitcherClient.Instance.ServerUri == null)
      {
         Debug.WriteLine("Server URI is not configured. Cannot load station logo.");
         return;
      }

      try
      {
         var logoUrl = new Uri(SwitcherClient.Instance.ServerUri, "/logo");
         using var httpClient = new HttpClient();
         var response = await httpClient.GetAsync(logoUrl).ConfigureAwait(false);

         if (response.IsSuccessStatusCode)
         {
            var imageBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
               using var memoryStream = new MemoryStream(imageBytes);
               StationLogo = new Bitmap(memoryStream);
            });
            Debug.WriteLine($"Successfully loaded station logo from {logoUrl}");
         }
         else
         {
            Debug.WriteLine($"Failed to load station logo. Status code: {response.StatusCode} from {logoUrl}");
            StationLogo = null;
         }
      }
      catch (HttpRequestException ex)
      {
         Debug.WriteLine($"Error loading station logo: {ex.Message}");
         StationLogo = null;
      }
      catch (TaskCanceledException ex)
      {
         Debug.WriteLine($"Error loading station logo (timeout/cancelled): {ex.Message}");
         StationLogo = null;
      }
      catch (Exception ex)
      {
         Debug.WriteLine($"An unexpected error occurred while loading station logo: {ex.Message}");
      StationLogo = null;
      }
   }
}
