using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace IconSonic.Models;

public partial class IconFrame : ObservableObject
{
    private readonly WriteableBitmap _preview;

    public IconFrame(int size, byte[] pixels, string sourceLabel)
    {
        Size = size;
        Pixels = pixels;
        SourceLabel = sourceLabel;
        _preview = new WriteableBitmap(size, size);
        RefreshPreview();
    }

    public int Size { get; }

    public byte[] Pixels { get; private set; }

    public WriteableBitmap Preview => _preview;

    public string SizeLabel => $"{Size} x {Size}";

    public string SizeListLabel => Size.ToString();

    public string PixelCountLabel => $"{Size * Size:N0} px";

    public string SourceLabel { get; }

    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = true;

    public byte[] ClonePixels() => (byte[])Pixels.Clone();

    public void ReplacePixels(byte[] pixels)
    {
        if (pixels.Length != Pixels.Length)
        {
            throw new ArgumentException("Pixel buffer size does not match the frame size.", nameof(pixels));
        }

        Pixels = pixels;
        RefreshPreview();
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size)
        {
            return;
        }

        int offset = ((y * Size) + x) * 4;
        Pixels[offset] = b;
        Pixels[offset + 1] = g;
        Pixels[offset + 2] = r;
        Pixels[offset + 3] = a;
    }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size)
        {
            return (0, 0, 0, 0);
        }

        int offset = ((y * Size) + x) * 4;
        return (Pixels[offset + 2], Pixels[offset + 1], Pixels[offset], Pixels[offset + 3]);
    }

    public void RefreshPreview()
    {
        using Stream pixelStream = _preview.PixelBuffer.AsStream();
        pixelStream.Seek(0, SeekOrigin.Begin);
        pixelStream.Write(Pixels, 0, Pixels.Length);
        _preview.Invalidate();
        OnPropertyChanged(nameof(Preview));
    }
}
