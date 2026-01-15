using System.Text;
using System;
using System.Collections.Generic;

namespace RemoteRelay.Common;

public static class AppSettingsValidator
{
    public static bool TryValidate(AppSettings settings, out string validationSummary)
    {
        var errors = new List<string>();

        ValidateRoutes(settings, errors);
        ValidateServerPort(settings, errors);
        ValidateDefaultRoutes(settings, errors);
        ValidatePhysicalButtons(settings, errors);
        ValidateInactiveRelay(settings, errors);

        if (errors.Count == 0)
        {
            validationSummary = string.Empty;
            return true;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Configuration validation failed:");
        foreach (var error in errors)
        {
            builder.Append(" - ");
            builder.AppendLine(error);
        }

        validationSummary = builder.ToString();
        return false;
    }

    private static void ValidateRoutes(AppSettings settings, List<string> errors)
    {
        if (settings.Routes == null || settings.Routes.Count == 0)
        {
            errors.Add("At least one route must be configured.");
            return;
        }

        var seen = new HashSet<(string Source, string Output)>(new RouteEqualityComparer());
        foreach (var route in settings.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.SourceName))
            {
                errors.Add("A route is missing a SourceName.");
            }

            if (string.IsNullOrWhiteSpace(route.OutputName))
            {
                errors.Add("Route for source '" + (route.SourceName ?? "<unknown>") + "' is missing an OutputName.");
            }

            if (route.RelayPin <= 0)
            {
                errors.Add($"Route {route.SourceName}->{route.OutputName} has an invalid relay pin '{route.RelayPin}'. Pin must be greater than 0.");
            }
            else if (route.RelayPin > 40)
            {
                errors.Add($"Route {route.SourceName}->{route.OutputName} has relay pin '{route.RelayPin}' which exceeds maximum valid pin (40).");
            }

            if (!string.IsNullOrWhiteSpace(route.SourceName) && !string.IsNullOrWhiteSpace(route.OutputName))
            {
                if (!seen.Add((route.SourceName, route.OutputName)))
                {
                    errors.Add($"Duplicate route detected for '{route.SourceName}' -> '{route.OutputName}'.");
                }
            }
        }
    }

    private static void ValidateServerPort(AppSettings settings, List<string> errors)
    {
        if (settings.ServerPort <= 0 || settings.ServerPort > 65535)
        {
            errors.Add($"ServerPort '{settings.ServerPort}' must be between 1 and 65535.");
        }
    }

    private static void ValidateDefaultRoutes(AppSettings settings, List<string> errors)
    {
        if (settings.DefaultRoutes == null)
        {
            return;
        }

        var validSources = new HashSet<string>(settings.Sources, StringComparer.OrdinalIgnoreCase);
        var validOutputs = new HashSet<string>(settings.Outputs, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in settings.DefaultRoutes)
        {
            if (!validSources.Contains(pair.Key))
            {
                errors.Add($"Default route references unknown source '{pair.Key}'.");
            }

            if (!string.IsNullOrWhiteSpace(pair.Value) && !validOutputs.Contains(pair.Value))
            {
                errors.Add($"Default route for source '{pair.Key}' references unknown output '{pair.Value}'.");
            }
        }
    }

    private static void ValidatePhysicalButtons(AppSettings settings, List<string> errors)
    {
        if (settings.PhysicalSourceButtons == null)
        {
            return;
        }

        var validSources = new HashSet<string>(settings.Sources, StringComparer.OrdinalIgnoreCase);
        foreach (var button in settings.PhysicalSourceButtons)
        {
            if (!validSources.Contains(button.Key))
            {
                errors.Add($"Physical button configured for unknown source '{button.Key}'.");
                continue;
            }

            if (button.Value.PinNumber <= 0)
            {
                errors.Add($"Physical button for source '{button.Key}' has invalid pin number '{button.Value.PinNumber}'. Pin must be greater than 0.");
            }
            else if (button.Value.PinNumber > 40)
            {
                errors.Add($"Physical button for source '{button.Key}' has pin '{button.Value.PinNumber}' which exceeds maximum valid pin (40).");
            }
        }
    }

    private static void ValidateInactiveRelay(AppSettings settings, List<string> errors)
    {
        if (settings.InactiveRelay == null)
        {
            return;
        }

        if (settings.InactiveRelay.Pin <= 0)
        {
            errors.Add($"Inactive relay pin '{settings.InactiveRelay.Pin}' must be greater than zero.");
        }
        else if (settings.InactiveRelay.Pin > 40)
        {
            errors.Add($"Inactive relay pin '{settings.InactiveRelay.Pin}' exceeds maximum valid pin (40).");
        }
    }

    private sealed class RouteEqualityComparer : IEqualityComparer<(string Source, string Output)>
    {
        public bool Equals((string Source, string Output) x, (string Source, string Output) y)
        {
            return string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.Output, y.Output, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Source, string Output) obj)
        {
            var sourceHash = obj.Source is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source);
            var outputHash = obj.Output is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Output);
            return HashCode.Combine(sourceHash, outputHash);
        }
    }
}
