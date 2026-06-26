using System.Globalization;
using System.Text.Json;

namespace NextIntegrations.Common;

/// <summary>The shared display-format settings (persisted as one JSON config file).</summary>
public sealed class HorecaFormatConfig
{
    /// <summary>.NET date pattern, default <c>dd/MM/yyyy</c>.</summary>
    public string DateFormat { get; set; } = "dd/MM/yyyy";

    /// <summary>.NET time pattern, default <c>HH:mm</c>.</summary>
    public string TimeFormat { get; set; } = "HH:mm";
}

/// <summary>
/// Single source of truth for display formats across both apps and the integrations. Reads from one
/// shared config file — <c>LocalApplicationData/Horeca/horeca.config.json</c> — created with defaults
/// (date = <c>dd/MM/yyyy</c>) on first use, so the format can be changed in one place. Not static/hard-
/// coded: edit that file and restart to change every date shown anywhere.
/// </summary>
public static class HorecaFormats
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Lazy<HorecaFormatConfig> Current = new(Load);

    /// <summary>The configured date pattern (e.g. <c>dd/MM/yyyy</c>).</summary>
    public static string DatePattern => Current.Value.DateFormat;

    /// <summary>The configured time pattern (e.g. <c>HH:mm</c>).</summary>
    public static string TimePattern => Current.Value.TimeFormat;

    /// <summary>The shared config file path.</summary>
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horeca",
        "horeca.config.json");

    /// <summary>Formats a date using the shared pattern (culture-invariant so the pattern is honoured exactly).</summary>
    public static string Date(DateTimeOffset value) => value.ToString(DatePattern, CultureInfo.InvariantCulture);

    public static string Date(DateTime value) => value.ToString(DatePattern, CultureInfo.InvariantCulture);

    /// <summary>Formats a time using the shared pattern.</summary>
    public static string Time(DateTimeOffset value) => value.ToString(TimePattern, CultureInfo.InvariantCulture);

    private static HorecaFormatConfig Load()
    {
        try
        {
            string path = ConfigPath;
            if (File.Exists(path))
            {
                HorecaFormatConfig? loaded = JsonSerializer.Deserialize<HorecaFormatConfig>(File.ReadAllText(path), Options);
                if (loaded is not null)
                {
                    return Normalize(loaded);
                }
            }
            else
            {
                // Create the file with defaults so there is one place to change the format.
                var defaults = new HorecaFormatConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(defaults, Options));
                return defaults;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall back to defaults if the config can't be read/written.
        }

        return new HorecaFormatConfig();
    }

    private static HorecaFormatConfig Normalize(HorecaFormatConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.DateFormat))
        {
            config.DateFormat = "dd/MM/yyyy";
        }

        if (string.IsNullOrWhiteSpace(config.TimeFormat))
        {
            config.TimeFormat = "HH:mm";
        }

        return config;
    }
}
