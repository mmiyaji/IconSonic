using Windows.UI;

namespace IconSonic.Models;

public sealed class PaletteSwatch(Color color)
{
    public Color Color { get; } = color;
}
