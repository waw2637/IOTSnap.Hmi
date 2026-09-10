using MudBlazor;

namespace IOTSnap.Hmi.Theme;

public static class WavacMudTheme
{
    public static MudTheme Create()
    {
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#1F4FD7",
                PrimaryDarken = "#163AA7",
                PrimaryLighten = "#4E7BFF",
                Secondary = "#0F172A",
                SecondaryDarken = "#0B1220",
                SecondaryLighten = "#1E293B",
                Tertiary = "#10B981",
                Info = "#2563EB",
                Success = "#16A34A",
                Warning = "#D97706",
                Error = "#DC2626",
                Background = "#F7F9FC",
                BackgroundGray = "#EFF3F9",
                Surface = "#FFFFFF",
                AppbarBackground = "#1F4FD7",
                AppbarText = "#FFFFFF",
                DrawerBackground = "#FFFFFF",
                DrawerText = "#111827",
                DrawerIcon = "#4B5563",
                TextPrimary = "#111827",
                TextSecondary = "#4B5563",
                TextDisabled = "#9CA3AF",
                LinesDefault = "#E5E7EB",
                LinesInputs = "#D1D5DB",
                TableLines = "#E5E7EB",
                TableStriped = "#F3F4F6",
                Divider = "#E5E7EB",
                DividerLight = "#CBD5F5",
                ActionDefault = "#1F2937",
                ActionDisabled = "#9CA3AF",
                ActionDisabledBackground = "#E5E7EB",
                HoverOpacity = 0.08,
                RippleOpacity = 0.12,
                RippleOpacitySecondary = 0.2
            },
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = new[] { "Space Grotesk", "IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif" },
                    FontWeight = "400",
                    FontSize = "0.95rem",
                    LineHeight = "1.6",
                    LetterSpacing = "0.01em"
                },
                H1 = new H1Typography
                {
                    FontFamily = new[] { "Space Grotesk", "IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif" },
                    FontWeight = "600",
                    LetterSpacing = "-0.02em"
                },
                H2 = new H2Typography
                {
                    FontFamily = new[] { "Space Grotesk", "IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif" },
                    FontWeight = "600",
                    LetterSpacing = "-0.015em"
                },
                H3 = new H3Typography
                {
                    FontFamily = new[] { "Space Grotesk", "IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif" },
                    FontWeight = "600"
                },
                H4 = new H4Typography
                {
                    FontFamily = new[] { "Space Grotesk", "IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif" },
                    FontWeight = "600"
                },
                H5 = new H5Typography
                {
                    FontFamily = new[] { "Space Grotesk", "IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif" },
                    FontWeight = "600"
                },
                H6 = new H6Typography
                {
                    FontFamily = new[] { "Space Grotesk", "IBM Plex Sans", "Segoe UI", "system-ui", "sans-serif" },
                    FontWeight = "600"
                }
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "8px",
                DrawerWidthLeft = "260px",
                AppbarHeight = "64px"
            }
        };
    }
}
