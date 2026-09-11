using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace TaskBarHook.Theming;

public sealed record ThemePalette(
    string Name,
    Color Surface,
    Color SurfaceAlt,
    Color BorderSubtle,
    Color CompactBorder,
    Color TextPrimary,
    Color TextSecondary,
    Color PlaceholderIcon,
    Color HoverOverlay,
    Color PressedOverlay,
    Color HoverFill,
    Color PressedFill,
    Color HoverText,
    Color FocusRing,
    Color ProgressTrack,
    Color ProgressFill,
    Color ProgressRemainder,
    Color ProgressThumb,
    Color ErrorText,
    Color HintSurface);
