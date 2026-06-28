using IconSonic.Models;
using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace IconSonic.Services;

public sealed class IconImageService
{
    public static readonly int[] DefaultIconSizes = [16, 24, 32, 48, 64, 128, 256];

    public async Task<IReadOnlyList<IconFrame>> LoadRasterAsync(string path, IReadOnlyList<int>? sizes = null)
    {
        await using FileStream input = File.OpenRead(path);
        using IRandomAccessStream stream = input.AsRandomAccessStream();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        uint sourceWidth = decoder.PixelWidth;
        uint sourceHeight = decoder.PixelHeight;

        List<IconFrame> frames = [];
        foreach (int size in sizes ?? DefaultIconSizes)
        {
            byte[] pixels = await DecodeContainAsync(decoder, sourceWidth, sourceHeight, size);
            frames.Add(new IconFrame(size, pixels, "Image"));
        }

        return frames;
    }

    public async Task<IReadOnlyList<IconFrame>> LoadIconAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        if (bytes.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)) != 1)
        {
            throw new InvalidDataException("The file is not a valid ICO image.");
        }

        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        List<IconFrame> frames = [];

        for (int i = 0; i < count; i++)
        {
            int entryOffset = 6 + (i * 16);
            if (entryOffset + 16 > bytes.Length)
            {
                break;
            }

            int width = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
            uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOffset + 8, 4));
            uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entryOffset + 12, 4));

            if (dataOffset + dataSize > bytes.Length)
            {
                continue;
            }

            byte[] imageBytes = bytes.AsSpan((int)dataOffset, (int)dataSize).ToArray();
            byte[] pixels = IsPng(imageBytes)
                ? await DecodePngBytesAsync(imageBytes, width)
                : DecodeDibIconFrame(imageBytes, width);

            frames.Add(new IconFrame(width, pixels, "ICO"));
        }

        if (frames.Count == 0)
        {
            throw new InvalidDataException("No supported ICO frames were found.");
        }

        return frames.OrderBy(f => f.Size).ToArray();
    }

    public async Task ExportIconAsync(string path, IEnumerable<IconFrame> frames)
    {
        IconFrame[] exportFrames = frames
            .Where(frame => frame.IsIncluded)
            .OrderBy(frame => frame.Size)
            .ToArray();

        if (exportFrames.Length == 0)
        {
            throw new InvalidOperationException("Select at least one size before exporting.");
        }

        List<byte[]> pngFrames = [];
        foreach (IconFrame frame in exportFrames)
        {
            pngFrames.Add(await EncodePngAsync(frame.Size, frame.Pixels));
        }

        await using FileStream output = File.Create(path);
        using BinaryWriter writer = new(output);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)exportFrames.Length);

        int imageOffset = 6 + (exportFrames.Length * 16);
        for (int i = 0; i < exportFrames.Length; i++)
        {
            IconFrame frame = exportFrames[i];
            byte[] png = pngFrames[i];
            writer.Write((byte)(frame.Size >= 256 ? 0 : frame.Size));
            writer.Write((byte)(frame.Size >= 256 ? 0 : frame.Size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)png.Length);
            writer.Write((uint)imageOffset);
            imageOffset += png.Length;
        }

        foreach (byte[] png in pngFrames)
        {
            writer.Write(png);
        }
    }

    public IconFrame CreateBlankFrame(int size)
    {
        return new IconFrame(size, new byte[size * size * 4], "Blank");
    }

    public IconFrame ResizeFrame(IconFrame source, int size)
    {
        return new IconFrame(size, ResizeBgraNearest(source.Pixels, source.Size, source.Size, size, size), $"{source.SizeLabel} resize");
    }

    private static async Task<byte[]> DecodeContainAsync(BitmapDecoder decoder, uint sourceWidth, uint sourceHeight, int targetSize)
    {
        double scale = Math.Min(targetSize / (double)sourceWidth, targetSize / (double)sourceHeight);
        uint scaledWidth = Math.Max(1, (uint)Math.Round(sourceWidth * scale));
        uint scaledHeight = Math.Max(1, (uint)Math.Round(sourceHeight * scale));

        BitmapTransform transform = new()
        {
            ScaledWidth = scaledWidth,
            ScaledHeight = scaledHeight,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        PixelDataProvider provider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        byte[] scaled = provider.DetachPixelData();
        byte[] target = new byte[targetSize * targetSize * 4];
        int offsetX = (targetSize - (int)scaledWidth) / 2;
        int offsetY = (targetSize - (int)scaledHeight) / 2;

        for (int y = 0; y < scaledHeight; y++)
        {
            int sourceRow = y * (int)scaledWidth * 4;
            int targetRow = ((y + offsetY) * targetSize + offsetX) * 4;
            System.Buffer.BlockCopy(scaled, sourceRow, target, targetRow, (int)scaledWidth * 4);
        }

        return target;
    }

    private static async Task<byte[]> DecodePngBytesAsync(byte[] bytes, int expectedSize)
    {
        using InMemoryRandomAccessStream randomAccessStream = new();
        await randomAccessStream.WriteAsync(bytes.AsBuffer());
        randomAccessStream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream);

        PixelDataProvider provider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        byte[] pixels = provider.DetachPixelData();
        if (decoder.PixelWidth == expectedSize && decoder.PixelHeight == expectedSize)
        {
            return pixels;
        }

        return ResizeBgraNearest(pixels, (int)decoder.PixelWidth, (int)decoder.PixelHeight, expectedSize, expectedSize);
    }

    private static async Task<byte[]> EncodePngAsync(int size, byte[] pixels)
    {
        using InMemoryRandomAccessStream stream = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        using SoftwareBitmap bitmap = new(BitmapPixelFormat.Bgra8, size, size, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(pixels.AsBuffer());
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        stream.Seek(0);
        byte[] output = new byte[stream.Size];
        using Stream managed = stream.AsStreamForRead();
        await managed.ReadExactlyAsync(output);
        return output;
    }

    private static byte[] DecodeDibIconFrame(byte[] bytes, int entryWidth)
    {
        if (bytes.Length < 40)
        {
            throw new InvalidDataException("Unsupported ICO bitmap frame.");
        }

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        int dibHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
        ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14, 2));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4));
        uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32, 4));

        if (compression != 0 || (bitCount != 32 && bitCount != 24))
        {
            throw new InvalidDataException("Only uncompressed 24-bit and 32-bit ICO bitmap frames are supported.");
        }

        int height = Math.Abs(dibHeight) / 2;
        if (width <= 0)
        {
            width = entryWidth;
        }

        int paletteSize = colorsUsed > 0 ? (int)colorsUsed * 4 : 0;
        int pixelOffset = headerSize + paletteSize;
        int xorStride = ((width * bitCount + 31) / 32) * 4;
        int andStride = ((width + 31) / 32) * 4;
        int maskOffset = pixelOffset + (xorStride * height);

        byte[] output = new byte[width * height * 4];
        bool hasExplicitAlpha = false;

        for (int y = 0; y < height; y++)
        {
            int sourceY = height - 1 - y;
            int sourceRow = pixelOffset + (sourceY * xorStride);
            int targetRow = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                int source = sourceRow + x * (bitCount / 8);
                int target = targetRow + x * 4;
                output[target] = bytes[source];
                output[target + 1] = bytes[source + 1];
                output[target + 2] = bytes[source + 2];
                output[target + 3] = bitCount == 32 ? bytes[source + 3] : (byte)255;
                hasExplicitAlpha |= bitCount == 32 && output[target + 3] != 0;
            }
        }

        if ((bitCount == 24 || !hasExplicitAlpha) && maskOffset + (andStride * height) <= bytes.Length)
        {
            for (int y = 0; y < height; y++)
            {
                int sourceY = height - 1 - y;
                int maskRow = maskOffset + (sourceY * andStride);
                for (int x = 0; x < width; x++)
                {
                    int maskByte = bytes[maskRow + (x / 8)];
                    bool transparent = ((maskByte >> (7 - (x % 8))) & 1) == 1;
                    if (transparent)
                    {
                        output[((y * width) + x) * 4 + 3] = 0;
                    }
                }
            }
        }

        return output;
    }

    private static byte[] ResizeBgraNearest(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        byte[] target = new byte[targetWidth * targetHeight * 4];

        for (int y = 0; y < targetHeight; y++)
        {
            int sourceY = Math.Min(sourceHeight - 1, (int)Math.Floor(y * sourceHeight / (double)targetHeight));
            for (int x = 0; x < targetWidth; x++)
            {
                int sourceX = Math.Min(sourceWidth - 1, (int)Math.Floor(x * sourceWidth / (double)targetWidth));
                int sourceOffset = ((sourceY * sourceWidth) + sourceX) * 4;
                int targetOffset = ((y * targetWidth) + x) * 4;
                target[targetOffset] = source[sourceOffset];
                target[targetOffset + 1] = source[sourceOffset + 1];
                target[targetOffset + 2] = source[sourceOffset + 2];
                target[targetOffset + 3] = source[sourceOffset + 3];
            }
        }

        return target;
    }

    private static bool IsPng(byte[] bytes)
    {
        return bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A;
    }
}
