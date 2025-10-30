using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Terminal.Gui;

public class ThemeService
{
    private const string ThemeFileName = "theme.json";
    private static ThemeService _instance;
    public static ThemeService Instance => _instance ?? (_instance = new ThemeService());

    public Theme CurrentTheme { get; private set; }

    private ThemeService()
    {
        LoadTheme();
    }

    public void LoadTheme(string filePath = null)
    {
        string themeFile = filePath ?? ThemeFileName;
        if (File.Exists(themeFile))
        {
            try
            {
                var json = File.ReadAllText(themeFile);
                // Try to parse as nested config { "Theme": { ... } }
                var configObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (configObj != null && configObj.ContainsKey("Theme"))
                {
                    var themeJson = JsonConvert.SerializeObject(configObj["Theme"]);
                    CurrentTheme = JsonConvert.DeserializeObject<Theme>(themeJson);
                }
                else
                {
                    // Fallback: try to parse as direct Theme
                    CurrentTheme = JsonConvert.DeserializeObject<Theme>(json);
                }
            }
            catch
            {
                CurrentTheme = Theme.Default;
            }
        }
        else
        {
            CurrentTheme = Theme.Default;
        }
    }

    public Color GetColor(string key)
    {
        if (CurrentTheme.Colors.TryGetValue(key, out var color))
            return color;
        // Fallback to default theme color for the key
        if (Theme.Default.Colors.TryGetValue(key, out var defaultColor))
            return defaultColor;
        // If not found in default, fallback to white
        return Color.White;
    }
}

public class Theme
{
    public Dictionary<string, Color> Colors { get; set; } = new Dictionary<string, Color>();

    public static Theme Default => new Theme
    {
        Colors = new Dictionary<string, Color>
        {
            { "Background", Color.Black },
            { "Foreground", Color.White },
            { "Accent", Color.Cyan },
            { "Error", Color.Red },
            { "Warning", Color.BrightYellow },
            { "Info", Color.BrightBlue }
        }
    };
}
