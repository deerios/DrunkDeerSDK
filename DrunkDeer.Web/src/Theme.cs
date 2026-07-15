using MudBlazor;

namespace DrunkDeer.Web;

/// <summary>
/// The app's single MudBlazor palette. The whole UI themes off this one object
/// (MudBlazor drives CSS custom properties under the hood), so brand tweaks live
/// in exactly one place. Dark-first to match the on-screen keyboard's dark keycaps.
/// </summary>
public static class Theme
{
    public static readonly MudTheme DrunkDeer = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary          = "#776be7",   // deer violet — accent for actuation/press highlights
            Secondary        = "#00c2c7",
            Background       = "#1a1a27",
            BackgroundGray   = "#151521",
            Surface          = "#22222f",
            AppbarBackground = "#22222f",
            AppbarText       = "#ffffffcc",
            DrawerBackground = "#1a1a27",
            DrawerText       = "#b0b0c0",
            DrawerIcon       = "#b0b0c0",
            TextPrimary      = "#ffffffde",
            TextSecondary    = "#b0b0c0",
            ActionDefault    = "#adadb1",
            Success          = "#37c98b",
            Warning          = "#ffb547",
            Error            = "#f2545b",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
        },
    };
}
