using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconSonic.Models;
using IconSonic.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using Windows.UI;

namespace IconSonic.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly IconImageService _imageService = new();

    public MainPageViewModel()
    {
        foreach (int size in IconImageService.DefaultIconSizes)
        {
            Frames.Add(_imageService.CreateBlankFrame(size));
        }

        SelectedFrame = Frames.FirstOrDefault(frame => frame.Size == 32) ?? Frames.FirstOrDefault();
        UpdateDocumentStats();
        StatusText = "Blank Windows icon set is ready.";
    }

    public ObservableCollection<IconFrame> Frames { get; } = [];

    public ObservableCollection<PaletteSwatch> Palette { get; } = [];

    public IReadOnlyList<int> SizeChoices { get; } = IconImageService.DefaultIconSizes;

    [ObservableProperty]
    public partial IconFrame? SelectedFrame { get; set; }

    [ObservableProperty]
    public partial EditorTool ActiveTool { get; set; } = EditorTool.Pencil;

    [ObservableProperty]
    public partial Color BrushColor { get; set; } = Color.FromArgb(255, 24, 104, 255);

    [ObservableProperty]
    public partial double NewFrameSize { get; set; } = 32;

    [ObservableProperty]
    public partial bool IsApplyToAllFrames { get; set; }

    [ObservableProperty]
    public partial double ObjectStrokeWidth { get; set; } = 1;

    [ObservableProperty]
    public partial string DocumentName { get; set; } = "Untitled icon";

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PointerText { get; set; } = "x -, y -";

    [ObservableProperty]
    public partial string FrameSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool HasSelectedFrame => SelectedFrame is not null;

    public bool HasFrames => Frames.Count > 0;

    public WriteableBitmap? SelectedPreview => SelectedFrame?.Preview;

    public string SelectedFrameTitle => SelectedFrame is null ? "No frame selected" : $"{SelectedFrame.SizeLabel} pixel editor";

    partial void OnSelectedFrameChanged(IconFrame? value)
    {
        OnPropertyChanged(nameof(HasSelectedFrame));
        OnPropertyChanged(nameof(SelectedPreview));
        OnPropertyChanged(nameof(SelectedFrameTitle));
        DuplicateSelectedCommand.NotifyCanExecuteChanged();
        ClearSelectedCommand.NotifyCanExecuteChanged();
        MirrorHorizontalCommand.NotifyCanExecuteChanged();
        MirrorVerticalCommand.NotifyCanExecuteChanged();
        StatusText = value is null ? "No frame selected." : $"Editing {value.SizeLabel}.";
    }

    partial void OnIsBusyChanged(bool value)
    {
        OpenImageCommand.NotifyCanExecuteChanged();
        OpenIconCommand.NotifyCanExecuteChanged();
        ExportIconCommand.NotifyCanExecuteChanged();
        GenerateCommonSizesCommand.NotifyCanExecuteChanged();
    }

    public bool CanRunCommand() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    public async Task OpenImageAsync(string path)
    {
        await RunBusyAsync(async () =>
        {
            IReadOnlyList<IconFrame> loaded = await _imageService.LoadRasterAsync(path);
            ReplaceFrames(loaded);
            DocumentName = Path.GetFileNameWithoutExtension(path);
            StatusText = "Image loaded and scaled into Windows icon sizes.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    public async Task OpenIconAsync(string path)
    {
        await RunBusyAsync(async () =>
        {
            IReadOnlyList<IconFrame> loaded = await _imageService.LoadIconAsync(path);
            ReplaceFrames(loaded);
            DocumentName = Path.GetFileNameWithoutExtension(path);
            StatusText = "ICO frames loaded.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    public async Task ExportIconAsync(string path)
    {
        await RunBusyAsync(async () =>
        {
            await _imageService.ExportIconAsync(path, Frames);
            StatusText = $"Exported {Frames.Count(frame => frame.IsIncluded)} ICO frame(s).";
        });
    }

    [RelayCommand]
    public void NewBlank()
    {
        Frames.Clear();
        foreach (int size in IconImageService.DefaultIconSizes)
        {
            Frames.Add(_imageService.CreateBlankFrame(size));
        }

        SelectedFrame = Frames.FirstOrDefault(frame => frame.Size == 32) ?? Frames.FirstOrDefault();
        DocumentName = "Untitled icon";
        RefreshPalette();
        UpdateDocumentStats();
        StatusText = "Blank Windows icon set is ready.";
    }

    [RelayCommand]
    public void AddBlankFrame()
    {
        int size = NormalizeFrameSize(NewFrameSize);
        if (TrySelectExistingFrame(size, $"A {size} x {size} frame already exists."))
        {
            return;
        }

        IconFrame frame = _imageService.CreateBlankFrame(size);
        Frames.Add(frame);
        SortFrames();
        SelectedFrame = frame;
        UpdateDocumentStats();
        StatusText = $"Added a blank {size} x {size} frame.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFrame))]
    public void DuplicateSelected()
    {
        if (SelectedFrame is null)
        {
            return;
        }

        int size = NormalizeFrameSize(NewFrameSize);
        if (TrySelectExistingFrame(size, $"A {size} x {size} frame already exists."))
        {
            return;
        }

        IconFrame copy = _imageService.ResizeFrame(SelectedFrame, size);
        Frames.Add(copy);
        SortFrames();
        SelectedFrame = copy;
        UpdateDocumentStats();
        StatusText = $"Copied the selected image into {copy.SizeLabel}.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFrame))]
    public void ClearSelected()
    {
        if (SelectedFrame is null)
        {
            return;
        }

        foreach (IconFrame frame in GetAffectedFrames())
        {
            frame.ReplacePixels(new byte[frame.Size * frame.Size * 4]);
        }

        RefreshPalette();
        StatusText = IsApplyToAllFrames ? "Cleared all sizes." : $"Cleared {SelectedFrame.SizeLabel}.";
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    public void GenerateCommonSizes()
    {
        if (SelectedFrame is null)
        {
            StatusText = "Select a frame before generating sizes.";
            return;
        }

        IconFrame source = SelectedFrame;
        Frames.Clear();
        foreach (int size in IconImageService.DefaultIconSizes)
        {
            Frames.Add(size == source.Size
                ? new IconFrame(size, source.ClonePixels(), "Source")
                : _imageService.ResizeFrame(source, size));
        }

        SelectedFrame = Frames.FirstOrDefault(frame => frame.Size == source.Size) ?? Frames.FirstOrDefault();
        RefreshPalette();
        UpdateDocumentStats();
        StatusText = "Generated the standard Windows ICO size set.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFrame))]
    public void MirrorHorizontal()
    {
        if (SelectedFrame is null)
        {
            return;
        }

        foreach (IconFrame frame in GetAffectedFrames())
        {
            byte[] source = frame.ClonePixels();
            int size = frame.Size;
            byte[] target = new byte[source.Length];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Buffer.BlockCopy(source, ((y * size) + x) * 4, target, ((y * size) + (size - 1 - x)) * 4, 4);
                }
            }

            frame.ReplacePixels(target);
        }

        StatusText = IsApplyToAllFrames ? "Mirrored all sizes horizontally." : $"Mirrored {SelectedFrame.SizeLabel} horizontally.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFrame))]
    public void MirrorVertical()
    {
        if (SelectedFrame is null)
        {
            return;
        }

        foreach (IconFrame frame in GetAffectedFrames())
        {
            byte[] source = frame.ClonePixels();
            int size = frame.Size;
            byte[] target = new byte[source.Length];
            for (int y = 0; y < size; y++)
            {
                Buffer.BlockCopy(source, y * size * 4, target, (size - 1 - y) * size * 4, size * 4);
            }

            frame.ReplacePixels(target);
        }

        StatusText = IsApplyToAllFrames ? "Mirrored all sizes vertically." : $"Mirrored {SelectedFrame.SizeLabel} vertically.";
    }

    public void ApplyToolAt(int x, int y)
    {
        if (SelectedFrame is null)
        {
            return;
        }

        switch (ActiveTool)
        {
            case EditorTool.Pencil:
                foreach ((IconFrame frame, int targetX, int targetY) in GetMappedTargetPoints(x, y))
                {
                    frame.SetPixel(targetX, targetY, BrushColor.R, BrushColor.G, BrushColor.B, BrushColor.A);
                    frame.RefreshPreview();
                }

                RefreshPalette();
                break;
            case EditorTool.Eraser:
                foreach ((IconFrame frame, int targetX, int targetY) in GetMappedTargetPoints(x, y))
                {
                    frame.SetPixel(targetX, targetY, 0, 0, 0, 0);
                    frame.RefreshPreview();
                }

                RefreshPalette();
                break;
            case EditorTool.Fill:
                foreach ((IconFrame frame, int targetX, int targetY) in GetMappedTargetPoints(x, y))
                {
                    FloodFill(frame, targetX, targetY);
                }

                RefreshPalette();
                break;
            case EditorTool.Eyedropper:
                (byte r, byte g, byte b, byte a) = SelectedFrame.GetPixel(x, y);
                BrushColor = Color.FromArgb(a, r, g, b);
                StatusText = $"Picked #{a:X2}{r:X2}{g:X2}{b:X2}.";
                break;
        }
    }

    public void ApplyObjectTool(int startX, int startY, int endX, int endY)
    {
        if (SelectedFrame is null)
        {
            return;
        }

        EditorTool tool = ActiveTool;
        if (!IsObjectTool(tool))
        {
            return;
        }

        int sourceSize = SelectedFrame.Size;
        foreach (IconFrame frame in GetAffectedFrames())
        {
            int x1 = MapCoordinate(startX, sourceSize, frame.Size);
            int y1 = MapCoordinate(startY, sourceSize, frame.Size);
            int x2 = MapCoordinate(endX, sourceSize, frame.Size);
            int y2 = MapCoordinate(endY, sourceSize, frame.Size);
            int strokeWidth = Math.Max(1, (int)Math.Round(ObjectStrokeWidth * frame.Size / sourceSize));
            DrawObject(frame, tool, x1, y1, x2, y2, strokeWidth);
            frame.RefreshPreview();
        }

        RefreshPalette();
        StatusText = IsApplyToAllFrames ? $"Placed {tool} on all sizes." : $"Placed {tool} on {SelectedFrame.SizeLabel}.";
    }

    public void SetPointerPosition(int x, int y)
    {
        if (SelectedFrame is null || x < 0 || y < 0)
        {
            PointerText = "x -, y -";
            return;
        }

        PointerText = $"x {x}, y {y} / {SelectedFrame.SizeLabel}";
    }

    public void RefreshPalette()
    {
        Palette.Clear();

        Dictionary<uint, int> counts = [];
        foreach (IconFrame frame in Frames)
        {
            byte[] pixels = frame.Pixels;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte alpha = pixels[i + 3];
                if (alpha < 16)
                {
                    continue;
                }

                uint key = ((uint)alpha << 24) | ((uint)pixels[i + 2] << 16) | ((uint)pixels[i + 1] << 8) | pixels[i];
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        foreach (uint color in counts.OrderByDescending(pair => pair.Value).Take(12).Select(pair => pair.Key))
        {
            Palette.Add(new PaletteSwatch(Color.FromArgb(
                (byte)(color >> 24),
                (byte)(color >> 16),
                (byte)(color >> 8),
                (byte)color)));
        }
    }

    public void RefreshFrameSummary()
    {
        UpdateDocumentStats();
    }

    private void ReplaceFrames(IReadOnlyList<IconFrame> loaded)
    {
        Frames.Clear();
        foreach (IconFrame frame in loaded
            .GroupBy(frame => frame.Size)
            .Select(group => group.First())
            .OrderBy(frame => frame.Size))
        {
            Frames.Add(frame);
        }

        SelectedFrame = Frames.FirstOrDefault(frame => frame.Size == 32)
            ?? Frames.OrderBy(frame => frame.Size).FirstOrDefault();

        RefreshPalette();
        UpdateDocumentStats();
    }

    private static bool IsObjectTool(EditorTool tool)
    {
        return tool is EditorTool.Line
            or EditorTool.Arrow
            or EditorTool.Rectangle
            or EditorTool.Ellipse
            or EditorTool.Bezier;
    }

    private IconFrame[] GetAffectedFrames()
    {
        if (SelectedFrame is null)
        {
            return [];
        }

        return IsApplyToAllFrames ? Frames.ToArray() : [SelectedFrame];
    }

    private IEnumerable<(IconFrame Frame, int X, int Y)> GetMappedTargetPoints(int x, int y)
    {
        if (SelectedFrame is null)
        {
            yield break;
        }

        int sourceSize = SelectedFrame.Size;
        foreach (IconFrame frame in GetAffectedFrames())
        {
            yield return (frame, MapCoordinate(x, sourceSize, frame.Size), MapCoordinate(y, sourceSize, frame.Size));
        }
    }

    private bool TrySelectExistingFrame(int size, string message)
    {
        IconFrame? existingFrame = Frames.FirstOrDefault(frame => frame.Size == size);
        if (existingFrame is null)
        {
            return false;
        }

        SelectedFrame = existingFrame;
        StatusText = message;
        return true;
    }

    private void SortFrames()
    {
        IconFrame? selectedFrame = SelectedFrame;
        IconFrame[] orderedFrames = Frames.OrderBy(frame => frame.Size).ToArray();
        Frames.Clear();
        foreach (IconFrame frame in orderedFrames)
        {
            Frames.Add(frame);
        }

        if (selectedFrame is not null && Frames.Contains(selectedFrame))
        {
            SelectedFrame = selectedFrame;
        }
    }

    private static int NormalizeFrameSize(double size)
    {
        return (int)Math.Clamp(Math.Round(size), 16, 256);
    }

    private static int MapCoordinate(int coordinate, int sourceSize, int targetSize)
    {
        if (sourceSize <= 1)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Round(coordinate * (targetSize - 1) / (double)(sourceSize - 1)), 0, targetSize - 1);
    }

    private void DrawObject(IconFrame frame, EditorTool tool, int x1, int y1, int x2, int y2, int strokeWidth)
    {
        switch (tool)
        {
            case EditorTool.Line:
                DrawLine(frame, x1, y1, x2, y2, strokeWidth);
                break;
            case EditorTool.Arrow:
                DrawArrow(frame, x1, y1, x2, y2, strokeWidth);
                break;
            case EditorTool.Rectangle:
                DrawRectangle(frame, x1, y1, x2, y2, strokeWidth);
                break;
            case EditorTool.Ellipse:
                DrawEllipse(frame, x1, y1, x2, y2, strokeWidth);
                break;
            case EditorTool.Bezier:
                DrawBezier(frame, x1, y1, x2, y2, strokeWidth);
                break;
        }
    }

    private void DrawRectangle(IconFrame frame, int x1, int y1, int x2, int y2, int strokeWidth)
    {
        int left = Math.Min(x1, x2);
        int right = Math.Max(x1, x2);
        int top = Math.Min(y1, y2);
        int bottom = Math.Max(y1, y2);

        DrawLine(frame, left, top, right, top, strokeWidth);
        DrawLine(frame, right, top, right, bottom, strokeWidth);
        DrawLine(frame, right, bottom, left, bottom, strokeWidth);
        DrawLine(frame, left, bottom, left, top, strokeWidth);
    }

    private void DrawEllipse(IconFrame frame, int x1, int y1, int x2, int y2, int strokeWidth)
    {
        double centerX = (x1 + x2) / 2.0;
        double centerY = (y1 + y2) / 2.0;
        double radiusX = Math.Abs(x2 - x1) / 2.0;
        double radiusY = Math.Abs(y2 - y1) / 2.0;

        if (radiusX < 0.5 || radiusY < 0.5)
        {
            DrawLine(frame, x1, y1, x2, y2, strokeWidth);
            return;
        }

        int steps = Math.Max(24, (int)Math.Ceiling(Math.Max(radiusX, radiusY) * 8));
        int previousX = (int)Math.Round(centerX + radiusX);
        int previousY = (int)Math.Round(centerY);
        for (int i = 1; i <= steps; i++)
        {
            double angle = Math.Tau * i / steps;
            int nextX = (int)Math.Round(centerX + Math.Cos(angle) * radiusX);
            int nextY = (int)Math.Round(centerY + Math.Sin(angle) * radiusY);
            DrawLine(frame, previousX, previousY, nextX, nextY, strokeWidth);
            previousX = nextX;
            previousY = nextY;
        }
    }

    private void DrawArrow(IconFrame frame, int x1, int y1, int x2, int y2, int strokeWidth)
    {
        DrawLine(frame, x1, y1, x2, y2, strokeWidth);

        double angle = Math.Atan2(y2 - y1, x2 - x1);
        double length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        double headLength = Math.Max(3, length * 0.25);
        const double headAngle = Math.PI / 7;

        int wing1X = (int)Math.Round(x2 - Math.Cos(angle - headAngle) * headLength);
        int wing1Y = (int)Math.Round(y2 - Math.Sin(angle - headAngle) * headLength);
        int wing2X = (int)Math.Round(x2 - Math.Cos(angle + headAngle) * headLength);
        int wing2Y = (int)Math.Round(y2 - Math.Sin(angle + headAngle) * headLength);

        DrawLine(frame, x2, y2, wing1X, wing1Y, strokeWidth);
        DrawLine(frame, x2, y2, wing2X, wing2Y, strokeWidth);
    }

    private void DrawBezier(IconFrame frame, int x1, int y1, int x2, int y2, int strokeWidth)
    {
        double deltaX = x2 - x1;
        double deltaY = y2 - y1;
        double length = Math.Max(1, Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
        double normalX = -deltaY / length;
        double normalY = deltaX / length;
        double bend = length * 0.28;

        double control1X = x1 + deltaX * 0.33 + normalX * bend;
        double control1Y = y1 + deltaY * 0.33 + normalY * bend;
        double control2X = x1 + deltaX * 0.66 + normalX * bend;
        double control2Y = y1 + deltaY * 0.66 + normalY * bend;

        int previousX = x1;
        int previousY = y1;
        int steps = Math.Max(20, (int)Math.Ceiling(length * 2));
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            double inverse = 1 - t;
            double nextX = inverse * inverse * inverse * x1
                + 3 * inverse * inverse * t * control1X
                + 3 * inverse * t * t * control2X
                + t * t * t * x2;
            double nextY = inverse * inverse * inverse * y1
                + 3 * inverse * inverse * t * control1Y
                + 3 * inverse * t * t * control2Y
                + t * t * t * y2;

            DrawLine(frame, previousX, previousY, (int)Math.Round(nextX), (int)Math.Round(nextY), strokeWidth);
            previousX = (int)Math.Round(nextX);
            previousY = (int)Math.Round(nextY);
        }
    }

    private void DrawLine(IconFrame frame, int x1, int y1, int x2, int y2, int strokeWidth)
    {
        int dx = Math.Abs(x2 - x1);
        int dy = -Math.Abs(y2 - y1);
        int stepX = x1 < x2 ? 1 : -1;
        int stepY = y1 < y2 ? 1 : -1;
        int error = dx + dy;
        int x = x1;
        int y = y1;

        while (true)
        {
            DrawBrush(frame, x, y, strokeWidth);
            if (x == x2 && y == y2)
            {
                break;
            }

            int e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x += stepX;
            }

            if (e2 <= dx)
            {
                error += dx;
                y += stepY;
            }
        }
    }

    private void DrawBrush(IconFrame frame, int centerX, int centerY, int strokeWidth)
    {
        int radius = Math.Max(0, strokeWidth / 2);
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if (radius == 0 || Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2) <= radius * radius)
                {
                    frame.SetPixel(x, y, BrushColor.R, BrushColor.G, BrushColor.B, BrushColor.A);
                }
            }
        }
    }

    private void UpdateDocumentStats()
    {
        OnPropertyChanged(nameof(HasFrames));
        DuplicateSelectedCommand.NotifyCanExecuteChanged();
        ClearSelectedCommand.NotifyCanExecuteChanged();
        MirrorHorizontalCommand.NotifyCanExecuteChanged();
        MirrorVerticalCommand.NotifyCanExecuteChanged();

        int included = Frames.Count(frame => frame.IsIncluded);
        FrameSummary = $"{Frames.Count} size(s), {included} included for export";
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await operation();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateDocumentStats();
        }
    }

    private void FloodFill(IconFrame frame, int startX, int startY)
    {
        int size = frame.Size;
        if (startX < 0 || startY < 0 || startX >= size || startY >= size)
        {
            return;
        }

        byte[] pixels = frame.Pixels;
        int startOffset = ((startY * size) + startX) * 4;
        byte targetB = pixels[startOffset];
        byte targetG = pixels[startOffset + 1];
        byte targetR = pixels[startOffset + 2];
        byte targetA = pixels[startOffset + 3];

        if (targetR == BrushColor.R && targetG == BrushColor.G && targetB == BrushColor.B && targetA == BrushColor.A)
        {
            return;
        }

        Queue<(int X, int Y)> queue = new();
        bool[] visited = new bool[size * size];
        queue.Enqueue((startX, startY));

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            if (x < 0 || y < 0 || x >= size || y >= size)
            {
                continue;
            }

            int index = (y * size) + x;
            if (visited[index])
            {
                continue;
            }

            visited[index] = true;
            int offset = index * 4;
            if (pixels[offset] != targetB || pixels[offset + 1] != targetG || pixels[offset + 2] != targetR || pixels[offset + 3] != targetA)
            {
                continue;
            }

            pixels[offset] = BrushColor.B;
            pixels[offset + 1] = BrushColor.G;
            pixels[offset + 2] = BrushColor.R;
            pixels[offset + 3] = BrushColor.A;

            queue.Enqueue((x + 1, y));
            queue.Enqueue((x - 1, y));
            queue.Enqueue((x, y + 1));
            queue.Enqueue((x, y - 1));
        }

        frame.RefreshPreview();
        StatusText = $"Filled region at {startX}, {startY}.";
    }
}
