using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Media;
using RemoteRelay.Common;
using RemoteRelay.Controls;
using ReactiveUI;

namespace RemoteRelay.MultiOutput;

public class MultiOutputViewModel : OperationViewModelBase
{
	private readonly Dictionary<string, SourceButtonViewModel> _inputLookup;
	private readonly Dictionary<string, SourceButtonViewModel> _outputLookup;
	private readonly Dictionary<string, Color> _palette;
	private SourceButtonViewModel? _activeSelection;

	public MultiOutputViewModel(AppSettings settings)
		: base(settings)
	{
		Inputs = settings.Sources.Select(source => new SourceButtonViewModel(source)).ToList();
		Outputs = settings.Outputs.Select(output => new SourceButtonViewModel(output)).ToList();

		_inputLookup = Inputs.ToDictionary(vm => vm.SourceName);
		_outputLookup = Outputs.ToDictionary(vm => vm.SourceName);
		_palette = BuildPalette(settings);

		var cancelRequested = Observable.Merge(
			CancelStream,
			Server._stateChanged.Select(_ => Unit.Default));

			var selectedInputStream = Inputs
				.Select(vm => vm.Clicked.Select(_ => vm))
				.Merge()
				.Select(vm => Observable
					.Return(vm)
					.Merge(
						Observable
							.Return((SourceButtonViewModel?)null)
							.Delay(TimeSpan.FromSeconds(TimeoutSeconds))))
				.Merge(cancelRequested.Select(_ => Observable.Return((SourceButtonViewModel?)null)))
				.Switch()
				.DistinctUntilChanged()
				.Publish()
				.RefCount();

	Disposables.Add(selectedInputStream
		.Subscribe(current =>
		{
			if (_activeSelection != null && _activeSelection != current)
			{
				RestoreInputState(_activeSelection);
			}

			_activeSelection = current;

			if (current != null)
			{
				UpdateOutputAvailability(current.SourceName);
				current.SetState(SourceState.Selected);
				PushStatusMessage(BuildCountdownMessage(current.SourceName));
			}
			else
			{
				UpdateOutputAvailability(null);
				foreach (var input in Inputs)
				{
					RestoreInputState(input);
				}
			}
		}));

					Disposables.Add(Server._stateChanged
						.Take(1)
						.Subscribe(status => HandleStatusUpdate(status)));

					if (CurrentStatus.Count > 0)
					{
						HandleStatusUpdate(CurrentStatus);
					}

			var connection = Outputs
			.Select(vm => vm.Clicked.Select(_ => vm))
			.Merge()
			.WithLatestFrom(selectedInputStream, (output, input) => (Output: output, Input: input))
			.Where(tuple => tuple.Input != null)
				.Select(tuple => (Output: tuple.Output, Input: tuple.Input!));

		Disposables.Add(connection.Subscribe(tuple =>
		{
			RequestCancel();

			if (!Server.IsConnected)
			{
				PushStatusMessage("Server connection lost. Please wait for reconnection.");
				return;
			}

			var inputName = tuple.Input.SourceName;
			var outputName = tuple.Output.SourceName;

			PushStatusMessage($"Routing {inputName} to {outputName}...");

			Server.SwitchSource(inputName, outputName);

			PushStatusMessage(
				Observable
					.Return("No response received from server")
					.Delay(TimeSpan.FromSeconds(TimeoutSeconds))
					.StartWith($"Waiting for {outputName} confirmation..."));
		}));

		Disposables.Add(selectedInputStream
			.Where(vm => vm == null)
			.Subscribe(_ => HandleCancel()));
	}

	public IReadOnlyList<SourceButtonViewModel> Inputs { get; }

	public IReadOnlyList<SourceButtonViewModel> Outputs { get; }

		protected override void HandleStatusUpdate(IReadOnlyDictionary<string, string> newStatus)
	{
		PushStatusMessage("Updating...");

		if (newStatus.Count == 0)
		{
			foreach (var input in Inputs)
				input.SetState(SourceState.Inactive);

			foreach (var output in Outputs)
			{
				output.SetState(SourceState.Inactive);
				output.IsEnabled = true;
			}

			UpdateOutputAvailability(null);
			PushStatusMessage(string.Empty);
			return;
		}

			var outputAssignments = newStatus
				.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
				.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

			foreach (var input in Inputs)
			{
				if (newStatus.TryGetValue(input.SourceName, out var outputName) && !string.IsNullOrWhiteSpace(outputName))
				{
					var colour = ResolveColour(input.SourceName);
					input.SetState(SourceState.Linked, colour);
				}
				else
				{
					input.SetState(SourceState.Inactive);
				}
			}

			if (_activeSelection != null)
			{
				_activeSelection.SetState(SourceState.Selected);
			}

		foreach (var output in Outputs)
		{
			if (outputAssignments.TryGetValue(output.SourceName, out var sourceName))
			{
				var colour = ResolveColour(sourceName);
				output.SetState(SourceState.Linked, colour);
					output.IsEnabled = true;
			}
			else
			{
				output.SetState(SourceState.Inactive);
					output.IsEnabled = true;
			}
		}

			UpdateOutputAvailability(_activeSelection?.SourceName);

		var statusMessages = newStatus
			.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.Select(pair => string.IsNullOrWhiteSpace(pair.Value)
				? $"{pair.Key} unrouted"
				: $"{pair.Key} → {pair.Value}");

		PushStatusMessage(string.Join("  |  ", statusMessages));
	}

		protected override void HandleCancel()
		{
			_activeSelection = null;
			base.HandleCancel();
		}

	private void RestoreInputState(SourceButtonViewModel input)
	{
		if (CurrentStatus.TryGetValue(input.SourceName, out var output) && !string.IsNullOrWhiteSpace(output))
		{
			input.SetState(SourceState.Linked, ResolveColour(input.SourceName));
		}
		else
		{
			input.SetState(SourceState.Inactive);
		}
	}

		private void UpdateOutputAvailability(string? selectedSource)
		{
			if (string.IsNullOrWhiteSpace(selectedSource))
			{
				foreach (var output in Outputs)
				{
					output.IsEnabled = true;
				}
				return;
			}

			var availableOutputs = Settings.Routes
				.Where(route => string.Equals(route.SourceName, selectedSource, StringComparison.OrdinalIgnoreCase))
				.Select(route => route.OutputName)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach (var output in Outputs)
			{
				output.IsEnabled = availableOutputs.Contains(output.SourceName);
			}
		}

	private Dictionary<string, Color> BuildPalette(AppSettings settings)
	{
		var palette = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

		if (settings.SourceColorPalette != null && settings.SourceColorPalette.Count > 0)
		{
			foreach (var kvp in settings.SourceColorPalette)
			{
				palette[kvp.Key] = TryParseColour(kvp.Value);
			}
		}

		foreach (var source in settings.Sources)
		{
			if (!palette.ContainsKey(source))
			{
				palette[source] = GenerateFallbackColour(source);
			}
		}

		return palette;
	}

	private Color ResolveColour(string source)
	{
		if (_palette.TryGetValue(source, out var colour))
		{
			return colour;
		}

		var generated = GenerateFallbackColour(source);
		_palette[source] = generated;
		return generated;
	}

	private static Color TryParseColour(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return Colors.LightGray;

		try
		{
			return Color.Parse(value);
		}
		catch
		{
			return Colors.LightGray;
		}
	}

	private static Color GenerateFallbackColour(string seed)
	{
		unchecked
		{
			var hash = seed.Aggregate(17, (current, c) => current * 31 + c);
			var hue = (hash % 360 + 360) % 360;
			return FromHsl(hue / 360d, 0.6, 0.5);
		}
	}

	private static Color FromHsl(double h, double s, double l)
	{
		double r, g, b;

		if (s == 0)
		{
			r = g = b = l;
		}
		else
		{
			double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
			double p = 2 * l - q;
			r = HueToRgb(p, q, h + 1.0 / 3);
			g = HueToRgb(p, q, h);
			b = HueToRgb(p, q, h - 1.0 / 3);
		}

		return Color.FromArgb(255, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
	}

	private static double HueToRgb(double p, double q, double t)
	{
		if (t < 0) t += 1;
		if (t > 1) t -= 1;
		if (t < 1.0 / 6) return p + (q - p) * 6 * t;
		if (t < 1.0 / 2) return q;
		if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
		return p;
	}

	private IObservable<string> BuildCountdownMessage(string sourceName)
	{
		return Observable
			.Range(0, TimeoutSeconds)
			.Select(x => TimeoutSeconds - x)
			.Zip(
				Observable
					.Return(Unit.Default)
					.Delay(TimeSpan.FromSeconds(1))
					.Repeat(Math.Max(TimeoutSeconds - 1, 0))
					.StartWith(Unit.Default),
				(remaining, _) => $"Select output for {sourceName} – {remaining}s");
	}
}