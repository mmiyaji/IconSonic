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
    private int _objectStartX;
    private int _objectStartY;

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
        if (!TryGetPixelFromPointer(e, out int x, out int y))
        {
            return;
        }

        _isPainting = true;
        EditorSurface.CapturePointer(e.Pointer);

        if (IsObjectTool(ViewModel.ActiveTool))
        {
            _objectStartX = x;
            _objectStartY = y;
            UpdateObjectPreview(x, y);
            ViewModel.SetPointerPosition(x, y);
            return;
        }

        ViewModel.ApplyToolAt(x, y);
        ViewModel.SetPointerPosition(x, y);
    }

    private void EditorSurface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        UpdatePointerText(e);
        if (_isPainting && IsObjectTool(ViewModel.ActiveTool) && TryGetPixelFromPointer(e, out int objectX, out int objectY))
        {
            UpdateObjectPreview(objectX, objectY);
            return;
        }

        if (_isPainting && ViewModel.ActiveTool is EditorTool.Pencil or EditorTool.Eraser)
        {
            PaintFromPointer(e);
        }
    }

    private void EditorSurface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isPainting && IsObjectTool(ViewModel.ActiveTool) && TryGetPixelFromPointer(e, out int x, out int y))
        {
            ViewModel.ApplyObjectTool(_objectStartX, _objectStartY, x, y);
        }

        _isPainting = false;
        ObjectPreviewOverlay.Children.Clear();
        EditorSurface.ReleasePointerCapture(e.Pointer);
    }

    private void EditorSurface_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPainting = false;
        ObjectPreviewOverlay.Children.Clear();
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
        AppBarToggleButton[] buttons =
        [
            PencilToolButton,
            EraserToolButton,
            FillToolButton,
            EyedropperToolButton,
            LineToolButton,
            ArrowToolButton,
            RectangleToolButton,
            EllipseToolButton,
            BezierToolButton
        ];
        foreach (AppBarToggleButton button in buttons)
        {
            button.IsChecked = ReferenceEquals(button, selected);
        }
    }

    private void UpdateGridOverlay()
    {
        GridOverlay.Children.Clear();
        ObjectPreviewOverlay.Children.Clear();
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

    private void UpdateObjectPreview(int endX, int endY)
    {
        ObjectPreviewOverlay.Children.Clear();
        if (ViewModel.SelectedFrame is null)
        {
            return;
        }

        Windows.Foundation.Point start = PixelToCanvasPoint(_objectStartX, _objectStartY);
        Windows.Foundation.Point end = PixelToCanvasPoint(endX, endY);
        Brush stroke = CreatePreviewBrush();
        double thickness = Math.Max(1, ViewModel.ObjectStrokeWidth);

        switch (ViewModel.ActiveTool)
        {
            case EditorTool.Line:
                AddPreviewLine(start, end, stroke, thickness);
                break;
            case EditorTool.Arrow:
                AddPreviewLine(start, end, stroke, thickness);
                AddPreviewArrowHead(start, end, stroke, thickness);
                break;
            case EditorTool.Rectangle:
                AddPreviewRectangle(start, end, stroke, thickness);
                break;
            case EditorTool.Ellipse:
                AddPreviewEllipse(start, end, stroke, thickness);
                break;
            case EditorTool.Bezier:
                AddPreviewBezier(start, end, stroke, thickness);
                break;
        }
    }

    private void AddPreviewLine(Windows.Foundation.Point start, Windows.Foundation.Point end, Brush stroke, double thickness)
    {
        ObjectPreviewOverlay.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = stroke,
            StrokeThickness = thickness
        });
    }

    private void AddPreviewArrowHead(Windows.Foundation.Point start, Windows.Foundation.Point end, Brush stroke, double thickness)
    {
        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        double headLength = Math.Max(10, length * 0.18);
        const double headAngle = Math.PI / 7;

        Windows.Foundation.Point wing1 = new(
            end.X - Math.Cos(angle - headAngle) * headLength,
            end.Y - Math.Sin(angle - headAngle) * headLength);
        Windows.Foundation.Point wing2 = new(
            end.X - Math.Cos(angle + headAngle) * headLength,
            end.Y - Math.Sin(angle + headAngle) * headLength);

        AddPreviewLine(end, wing1, stroke, thickness);
        AddPreviewLine(end, wing2, stroke, thickness);
    }

    private void AddPreviewRectangle(Windows.Foundation.Point start, Windows.Foundation.Point end, Brush stroke, double thickness)
    {
        Rectangle rectangle = new()
        {
            Width = Math.Abs(end.X - start.X),
            Height = Math.Abs(end.Y - start.Y),
            Stroke = stroke,
            StrokeThickness = thickness
        };
        Canvas.SetLeft(rectangle, Math.Min(start.X, end.X));
        Canvas.SetTop(rectangle, Math.Min(start.Y, end.Y));
        ObjectPreviewOverlay.Children.Add(rectangle);
    }

    private void AddPreviewEllipse(Windows.Foundation.Point start, Windows.Foundation.Point end, Brush stroke, double thickness)
    {
        Ellipse ellipse = new()
        {
            Width = Math.Abs(end.X - start.X),
            Height = Math.Abs(end.Y - start.Y),
            Stroke = stroke,
            StrokeThickness = thickness
        };
        Canvas.SetLeft(ellipse, Math.Min(start.X, end.X));
        Canvas.SetTop(ellipse, Math.Min(start.Y, end.Y));
        ObjectPreviewOverlay.Children.Add(ellipse);
    }

    private void AddPreviewBezier(Windows.Foundation.Point start, Windows.Foundation.Point end, Brush stroke, double thickness)
    {
        Polyline polyline = new()
        {
            Stroke = stroke,
            StrokeThickness = thickness
        };

        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double length = Math.Max(1, Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
        double normalX = -deltaY / length;
        double normalY = deltaX / length;
        double bend = length * 0.28;
        Windows.Foundation.Point control1 = new(start.X + deltaX * 0.33 + normalX * bend, start.Y + deltaY * 0.33 + normalY * bend);
        Windows.Foundation.Point control2 = new(start.X + deltaX * 0.66 + normalX * bend, start.Y + deltaY * 0.66 + normalY * bend);

        for (int i = 0; i <= 32; i++)
        {
            double t = i / 32.0;
            double inverse = 1 - t;
            double x = inverse * inverse * inverse * start.X
                + 3 * inverse * inverse * t * control1.X
                + 3 * inverse * t * t * control2.X
                + t * t * t * end.X;
            double y = inverse * inverse * inverse * start.Y
                + 3 * inverse * inverse * t * control1.Y
                + 3 * inverse * t * t * control2.Y
                + t * t * t * end.Y;
            polyline.Points.Add(new Windows.Foundation.Point(x, y));
        }

        ObjectPreviewOverlay.Children.Add(polyline);
    }

    private (double Left, double Top, double Size) GetDrawBounds()
    {
        double drawSize = Math.Min(EditorSurface.ActualWidth, EditorSurface.ActualHeight);
        double left = (EditorSurface.ActualWidth - drawSize) / 2;
        double top = (EditorSurface.ActualHeight - drawSize) / 2;
        return (left, top, drawSize);
    }

    private Windows.Foundation.Point PixelToCanvasPoint(int x, int y)
    {
        if (ViewModel.SelectedFrame is null)
        {
            return new Windows.Foundation.Point();
        }

        (double left, double top, double size) = GetDrawBounds();
        int frameSize = ViewModel.SelectedFrame.Size;
        return new Windows.Foundation.Point(
            left + (x + 0.5) * size / frameSize,
            top + (y + 0.5) * size / frameSize);
    }

    private static bool IsObjectTool(EditorTool tool)
    {
        return tool is EditorTool.Line
            or EditorTool.Arrow
            or EditorTool.Rectangle
            or EditorTool.Ellipse
            or EditorTool.Bezier;
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

    private static Brush CreatePreviewBrush()
    {
        if (Application.Current.Resources.TryGetValue("SystemAccentColor", out object value)
            && value is Color accentColor)
        {
            return new SolidColorBrush(accentColor);
        }

        return new SolidColorBrush(Color.FromArgb(220, 24, 104, 255));
    }
}
