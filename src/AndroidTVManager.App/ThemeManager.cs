using System.Windows;
using System.Windows.Media;

namespace AndroidTVManager.App;

public enum AppTheme
{
    Dark,
    PureBlack,
    White
}

public static class ThemeManager
{
    private static ResourceDictionary? _palette;

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        if (System.Windows.Application.Current is null)
            return;
        var resources = System.Windows.Application.Current.Resources;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"Themes/{theme switch { AppTheme.White => "Light", _ => theme.ToString() }}.xaml",
                UriKind.Relative)
        };
        resources.MergedDictionaries.Add(palette);
        if (_palette is not null)
            resources.MergedDictionaries.Remove(_palette);
        _palette = palette;

        SetBrush(resources, palette, "AppBackgroundColor", "AppBackgroundBrush", "WindowBackground");
        SetBrush(resources, palette, "NavigationBackgroundColor", "NavigationBackgroundBrush", "SidebarBackground");
        SetBrush(resources, palette, "HeaderBackgroundColor", "HeaderBackgroundBrush");
        SetBrush(resources, palette, "SurfacePrimaryColor", "SurfacePrimaryBrush", "PanelBackground");
        SetBrush(resources, palette, "SurfaceSecondaryColor", "SurfaceSecondaryBrush", "PanelRaisedBackground");
        SetBrush(resources, palette, "SurfaceElevatedColor", "SurfaceElevatedBrush");
        SetBrush(resources, palette, "CardBorderColor", "CardBorderBrush", "PanelBorder");
        SetBrush(resources, palette, "TextPrimaryColor", "TextPrimaryBrush", "TextPrimary");
        SetBrush(resources, palette, "TextSecondaryColor", "TextSecondaryBrush", "TextSecondary");
        SetBrush(resources, palette, "TextMutedColor", "TextMutedBrush", "TextMuted");
        SetBrush(resources, palette, "TextDisabledColor", "TextDisabledBrush");
        SetBrush(resources, palette, "AccentPrimaryColor", "AccentPrimaryBrush", "CyanAccent");
        SetBrush(resources, palette, "AccentSecondaryColor", "AccentSecondaryBrush", "VioletAccent");
        SetBrush(resources, palette, "SuccessColor", "SuccessBrush", "GreenAccent");
        SetBrush(resources, palette, "WarningColor", "WarningBrush", "AmberAccent");
        SetBrush(resources, palette, "DangerColor", "DangerBrush", "DangerAccent");
        SetBrush(resources, palette, "ControlBackgroundColor", "ControlBackgroundBrush");
        SetBrush(resources, palette, "ControlHoverColor", "ControlHoverBrush");
        SetBrush(resources, palette, "ControlPressedColor", "ControlPressedBrush");
        SetBrush(resources, palette, "CardBorderColor", "ControlBorderBrush");
        SetBrush(resources, palette, "SelectionBackgroundColor", "SelectionBackgroundBrush");
        SetBrush(resources, palette, "TextPrimaryColor", "SelectionForegroundBrush");
        SetBrush(resources, palette, "MenuBackgroundColor", "MenuBackgroundBrush");
        SetBrush(resources, palette, "TextPrimaryColor", "MenuForegroundBrush");
        SetBrush(resources, palette, "MenuHoverColor", "MenuHoverBrush");
        SetBrush(resources, palette, "CardBorderColor", "MenuSeparatorBrush");
        SetBrush(resources, palette, "ScrollBarTrackColor", "ScrollBarTrackBrush");
        SetBrush(resources, palette, "ScrollBarThumbColor", "ScrollBarThumbBrush");
        SetBrush(resources, palette, "ScrollBarThumbHoverColor", "ScrollBarThumbHoverBrush");

        CurrentTheme = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void SetBrush(
        ResourceDictionary resources,
        ResourceDictionary palette,
        string colorKey,
        params string[] brushKeys)
    {
        if (palette[colorKey] is not System.Windows.Media.Color color)
            return;
        foreach (var brushKey in brushKeys)
            if (FindResourceDictionary(resources, brushKey) is { } owner)
            {
                if (owner[brushKey] is SolidColorBrush brush && !brush.IsFrozen)
                    brush.Color = color;
                else
                    owner[brushKey] = new SolidColorBrush(color);
            }
    }

    private static ResourceDictionary? FindResourceDictionary(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
            return dictionary;
        for (var index = dictionary.MergedDictionaries.Count - 1; index >= 0; index--)
            if (FindResourceDictionary(dictionary.MergedDictionaries[index], key) is { } owner)
                return owner;
        return null;
    }
}
