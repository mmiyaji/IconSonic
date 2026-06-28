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
        int size = (int)Math.Clamp(NewFrameSize, 16, 256);
        IconFrame frame = _imageService.CreateBlankFrame(size);
        Frames.Add(frame);
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

        IconFrame copy = new(SelectedFrame.Size, SelectedFrame.ClonePixels(), "Duplicate");
        Frames.Add(copy);
        SelectedFrame = copy;
        UpdateDocumentStats();
        StatusText = $"Duplicated {copy.SizeLabel}.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFrame))]
    public void ClearSelected()
    {
        if (SelectedFrame is null)
        {
            return;
        }

        SelectedFrame.ReplacePixels(new byte[SelectedFrame.Size * SelectedFrame.Size * 4]);
        RefreshPalette();
        StatusText = $"Cleared {SelectedFrame.SizeLabel}.";
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

        byte[] source = SelectedFrame.ClonePixels();
        int size = SelectedFrame.Size;
        byte[] target = new byte[source.Length];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Buffer.BlockCopy(source, ((y * size) + x) * 4, target, ((y * size) + (size - 1 - x)) * 4, 4);
            }
        }

        SelectedFrame.ReplacePixels(target);
        StatusText = $"Mirrored {SelectedFrame.SizeLabel} horizontally.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFrame))]
    public void MirrorVertical()
    {
        if (SelectedFrame is null)
        {
            return;
        }

        byte[] source = SelectedFrame.ClonePixels();
        int size = SelectedFrame.Size;
        byte[] target = new byte[source.Length];
        for (int y = 0; y < size; y++)
        {
            Buffer.BlockCopy(source, y * size * 4, target, (size - 1 - y) * size * 4, size * 4);
        }

        SelectedFrame.ReplacePixels(target);
        StatusText = $"Mirrored {SelectedFrame.SizeLabel} vertically.";
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
                SelectedFrame.SetPixel(x, y, BrushColor.R, BrushColor.G, BrushColor.B, BrushColor.A);
                SelectedFrame.RefreshPreview();
                RefreshPalette();
                break;
            case EditorTool.Eraser:
                SelectedFrame.SetPixel(x, y, 0, 0, 0, 0);
                SelectedFrame.RefreshPreview();
                RefreshPalette();
                break;
            case EditorTool.Fill:
                FloodFill(x, y);
                RefreshPalette();
                break;
            case EditorTool.Eyedropper:
                (byte r, byte g, byte b, byte a) = SelectedFrame.GetPixel(x, y);
                BrushColor = Color.FromArgb(a, r, g, b);
                StatusText = $"Picked #{a:X2}{r:X2}{g:X2}{b:X2}.";
                break;
        }
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
        foreach (IconFrame frame in loaded)
        {
            Frames.Add(frame);
        }

        SelectedFrame = Frames.FirstOrDefault(frame => frame.Size == 32)
            ?? Frames.OrderBy(frame => frame.Size).FirstOrDefault();

        RefreshPalette();
        UpdateDocumentStats();
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

    private void FloodFill(int startX, int startY)
    {
        if (SelectedFrame is null)
        {
            return;
        }

        int size = SelectedFrame.Size;
        if (startX < 0 || startY < 0 || startX >= size || startY >= size)
        {
            return;
        }

        byte[] pixels = SelectedFrame.Pixels;
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

        SelectedFrame.RefreshPreview();
        StatusText = $"Filled region at {startX}, {startY}.";
    }
}
