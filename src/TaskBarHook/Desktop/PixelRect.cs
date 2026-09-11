namespace TaskBarHook.Desktop;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int CenterX => Left + (Width / 2);

    public static PixelRect FromBounds(int x, int y, int width, int height) =>
        new(x, y, x + width, y + height);

    public PixelRect Inflate(int value) =>
        new(Left - value, Top - value, Right + value, Bottom + value);

    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return new PixelRect(left, top, right, bottom);
    }

    public bool Intersects(PixelRect other)
    {
        return Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    }

    public bool Contains(PixelRect inner)
    {
        return inner.Left >= Left && inner.Right <= Right && inner.Top >= Top && inner.Bottom <= Bottom;
    }

    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}
