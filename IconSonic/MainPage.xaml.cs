using IconSonic.Models;
using IconSonic.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace IconSonic;

public sealed partial class MainPage : Page
{
    private bool _isPainting;

    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.SelectedFrame) or nameof(ViewModel.SelectedPreview))
            {
                UpdateGridOverlay();
            }
        };
        UpdateToolChecks(PencilToolButton);
    }

    public static Visibility BoolToVisibility(bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

    public static SolidColorBrush ColorToBrush(Color color)
    {
        return new SolidColorBrush(color);
    }

    private async void OpenImageButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".tif");
        picker.FileTypeFilter.Add(".tiff");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.OpenImageAsync(file.Path);
            FrameListView.SelectedItem = ViewModel.SelectedFrame;
            UpdateGridOverlay();
        }
    }

    private async void OpenIconButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };
        picker.FileTypeFilter.Add(".ico");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.OpenIconAsync(file.Path);
            FrameListView.SelectedItem = ViewModel.SelectedFrame;
            UpdateGridOverlay();
        }
    }

    private async void ExportIconButton_Click(object sender, RoutedEventArgs e)
    {
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = ViewModel.DocumentName,
            DefaultFileExtension = ".ico"
        };
        picker.FileTypeChoices.Add("Windows icon", [".ico"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await ViewModel.ExportIconAsync(file.Path);
        }
    }

    private void NewBlankButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NewBlank();
        FrameListView.SelectedItem = ViewModel.SelectedFrame;
        UpdateGridOverlay();
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not AppBarToggleButton selected || selected.Tag is not string toolName)
        {
            return;
        }

        if (Enum.TryParse(toolName, out EditorTool tool))
        {
            ViewModel.ActiveTool = tool;
            UpdateToolChecks(selected);
        }
    }

    private void EditorColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        ViewModel.BrushColor = args.NewColor;
    }

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PaletteSwatch swatch })
        {
            ViewModel.BrushColor = swatch.Color;
            EditorColorPicker.Color = swatch.Color;
        }
    }

    private void IncludeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshFrameSummary();
    }

    private void FrameListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateGridOverlay();
    }

    private void EditorSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isPainting = true;
        EditorSurface.CapturePointer(e.Pointer);
        PaintFromPointer(e);
    }

    private void EditorSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        UpdatePointerText(e);
        if (_isPainting && ViewModel.ActiveTool is EditorTool.Pencil or EditorTool.Eraser)
        {
            PaintFromPointer(e);
        }
    }

    private void EditorSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPainting = false;
        EditorSurface.ReleasePointerCapture(e.Pointer);
    }

    private void EditorSurface_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPainting = false;
        ViewModel.SetPointerPosition(-1, -1);
    }

    private void EditorSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateGridOverlay();
    }

    private void PaintFromPointer(PointerRoutedEventArgs e)
    {
        if (TryGetPixelFromPointer(e, out int x, out int y))
        {
            ViewModel.ApplyToolAt(x, y);
            ViewModel.SetPointerPosition(x, y);
        }
    }

    private void UpdatePointerText(PointerRoutedEventArgs e)
    {
        if (TryGetPixelFromPointer(e, out int x, out int y))
        {
            ViewModel.SetPointerPosition(x, y);
        }
    }

    private bool TryGetPixelFromPointer(PointerRoutedEventArgs e, out int x, out int y)
    {
        x = -1;
        y = -1;
        if (ViewModel.SelectedFrame is null || EditorSurface.ActualWidth <= 0 || EditorSurface.ActualHeight <= 0)
        {
            return false;
        }

        Windows.Foundation.Point point = e.GetCurrentPoint(EditorSurface).Position;
        (double left, double top, double size) = GetDrawBounds();
        if (point.X < left || point.X >= left + size || point.Y < top || point.Y >= top + size)
        {
            return false;
        }

        int frameSize = ViewModel.SelectedFrame.Size;
        x = Math.Clamp((int)((point.X - left) / size * frameSize), 0, frameSize - 1);
        y = Math.Clamp((int)((point.Y - top) / size * frameSize), 0, frameSize - 1);
        return true;
    }

    private void UpdateToolChecks(AppBarToggleButton selected)
    {
        AppBarToggleButton[] buttons = [PencilToolButton, EraserToolButton, FillToolButton, EyedropperToolButton];
        foreach (AppBarToggleButton button in buttons)
        {
            button.IsChecked = ReferenceEquals(button, selected);
        }
    }

    private void UpdateGridOverlay()
    {
        GridOverlay.Children.Clear();
        if (ViewModel.SelectedFrame is null || EditorSurface.ActualWidth <= 0 || EditorSurface.ActualHeight <= 0)
        {
            return;
        }

        (double left, double top, double drawSize) = GetDrawBounds();
        int frameSize = ViewModel.SelectedFrame.Size;
        int step = frameSize <= 64 ? 1 : frameSize <= 128 ? 2 : 4;
        Brush lineBrush = CreateGridLineBrush();

        for (int i = 0; i <= frameSize; i += step)
        {
            double position = left + (drawSize * i / frameSize);
            GridOverlay.Children.Add(new Line
            {
                X1 = position,
                Y1 = top,
                X2 = position,
                Y2 = top + drawSize,
                Stroke = lineBrush,
                StrokeThickness = i % Math.Max(1, step * 4) == 0 ? 1.0 : 0.5
            });

            position = top + (drawSize * i / frameSize);
            GridOverlay.Children.Add(new Line
            {
                X1 = left,
                Y1 = position,
                X2 = left + drawSize,
                Y2 = position,
                Stroke = lineBrush,
                StrokeThickness = i % Math.Max(1, step * 4) == 0 ? 1.0 : 0.5
            });
        }
    }

    private (double Left, double Top, double Size) GetDrawBounds()
    {
        double drawSize = Math.Min(EditorSurface.ActualWidth, EditorSurface.ActualHeight);
        double left = (EditorSurface.ActualWidth - drawSize) / 2;
        double top = (EditorSurface.ActualHeight - drawSize) / 2;
        return (left, top, drawSize);
    }

    private static Brush CreateGridLineBrush()
    {
        if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out object value)
            && value is SolidColorBrush solidColorBrush)
        {
            return new SolidColorBrush(solidColorBrush.Color) { Opacity = 0.24 };
        }

        return new SolidColorBrush(Color.FromArgb(80, 128, 128, 128));
    }
}
