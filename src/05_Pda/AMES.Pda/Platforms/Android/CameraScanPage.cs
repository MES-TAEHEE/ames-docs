using BarcodeScanning;

namespace AMES.Pda;

// AIM reads only the code under the crosshair (one label among several); AUTO reads the first code seen.
// The aim point is moved in EDIT and kept per device.
internal sealed class CameraScanPage : ContentPage
{
    private const int StableFrames = 2;
    private const string PrefAimX = "pda.camera.aimX";
    private const string PrefAimY = "pda.camera.aimY";
    private const string PrefAuto = "pda.camera.auto";
    private const double DefaultAimX = 0.5;
    private const double DefaultAimY = 1.0 / 3.0;
    // Bounding boxes come in native view pixels; 1D boxes are only a few pixels tall, hence the vertical margin.
    private const float AimSlackPx = 24f;
    private const double BeamHalf = 0.32;
    // Only the symbologies AMES prints: limiting them stops the phantom EAN/UPC reads seen on dense labels.
    private const BarcodeFormats Symbologies =
        BarcodeFormats.QRCode | BarcodeFormats.DataMatrix | BarcodeFormats.Code128 | BarcodeFormats.Code39;

    private static readonly Color Idle = Color.FromArgb("#1e3a5f");
    private static readonly Color Active = Color.FromArgb("#2563eb");

    private readonly TaskCompletionSource<string?> _result = new();
    private readonly CameraView _camera;
    private readonly GraphicsView _overlay;
    private readonly AimDrawable _aim = new();
    private readonly Label _hint;
    private readonly Button _torch, _mode, _edit;
    private bool _auto, _editing;
    private double _panStartX, _panStartY;
    private string? _candidate;
    private int _hits;

    public Task<string?> Result => _result.Task;

    public CameraScanPage()
    {
        BackgroundColor = Colors.Black;
        _aim.X = Preferences.Default.Get(PrefAimX, DefaultAimX);
        _aim.Y = Preferences.Default.Get(PrefAimY, DefaultAimY);
        _auto = Preferences.Default.Get(PrefAuto, false);

        _camera = new CameraView
        {
            AimMode = false,
            ViewfinderMode = true,
            TapToFocusEnabled = true,
            VibrationOnDetected = false,
            CaptureQuality = CaptureQuality.Highest,
            BarcodeSymbologies = Symbologies
        };
        _camera.OnDetectionFinished += OnDetected;

        _overlay = new GraphicsView { Drawable = _aim, InputTransparent = true, BackgroundColor = Colors.Transparent };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            if (e.GetPosition(_overlay) is { } p && _overlay.Width > 0 && _overlay.Height > 0)
                MoveAim(p.X / _overlay.Width, p.Y / _overlay.Height);
        };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPan;
        _overlay.GestureRecognizers.Add(tap);
        _overlay.GestureRecognizers.Add(pan);

        _hint = new Label
        {
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#99000000"),
            HorizontalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(12, 10),
            FontSize = 15
        };

        _torch = MakeButton("LIGHT", Idle);
        _torch.Clicked += (_, _) =>
        {
            _camera.TorchOn = !_camera.TorchOn;
            _torch.BackgroundColor = _camera.TorchOn ? Active : Idle;
        };
        _mode = MakeButton("", Idle);
        _mode.Clicked += (_, _) =>
        {
            if (_editing) SetEditing(false);
            _auto = !_auto;
            Preferences.Default.Set(PrefAuto, _auto);
            Refresh();
        };
        _edit = MakeButton("EDIT", Idle);
        _edit.Clicked += (_, _) => SetEditing(!_editing);
        var cancel = MakeButton("CANCEL", Color.FromArgb("#4b1d24"));
        cancel.Clicked += async (_, _) => await CloseAsync(null);

        var buttons = new Grid
        {
            ColumnDefinitions = { new(), new(), new(), new() },
            ColumnSpacing = 8,
            Padding = new Thickness(10, 14, 10, 34),
            BackgroundColor = Color.FromArgb("#99000000")
        };
        buttons.Add(_torch, 0);
        buttons.Add(_mode, 1);
        buttons.Add(_edit, 2);
        buttons.Add(cancel, 3);

        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        foreach (var layer in new View[] { _camera, _overlay })
        {
            root.Add(layer, 0, 0);
            Grid.SetRowSpan(layer, 3);
        }
        root.Add(_hint, 0, 0);
        root.Add(buttons, 0, 2);
        Content = root;
        Refresh();
    }

    private static Button MakeButton(string text, Color color) => new()
    {
        Text = text, BackgroundColor = color, TextColor = Colors.White, FontSize = 18, FontAttributes = FontAttributes.Bold,
        HeightRequest = 72, CornerRadius = 10, Padding = new Thickness(2, 0)
    };

    private void Refresh()
    {
        _mode.Text = _auto ? "AUTO" : "AIM";
        _mode.BackgroundColor = _auto ? Active : Idle;
        _edit.Text = _editing ? "DONE" : "EDIT";
        _edit.BackgroundColor = _editing ? Active : Idle;
        _edit.IsEnabled = !_auto;
        _hint.Text = _editing ? "Drag or tap to move the aim point, then DONE"
            : _auto ? "AUTO: point the camera at the code"
            : "Aim the red crosshair at the code";
        _aim.Visible = !_auto;
        _aim.Editing = _editing;
        _overlay.InputTransparent = !_editing;
        _overlay.Invalidate();
    }

    private void SetEditing(bool editing)
    {
        _editing = editing;
        _camera.PauseScanning = editing;
        if (!editing)
        {
            Preferences.Default.Set(PrefAimX, _aim.X);
            Preferences.Default.Set(PrefAimY, _aim.Y);
        }
        _candidate = null;
        _hits = 0;
        Refresh();
    }

    private void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            _panStartX = _aim.X;
            _panStartY = _aim.Y;
        }
        else if (e.StatusType == GestureStatus.Running && _overlay.Width > 0 && _overlay.Height > 0)
        {
            MoveAim(_panStartX + e.TotalX / _overlay.Width, _panStartY + e.TotalY / _overlay.Height);
        }
    }

    private void MoveAim(double x, double y)
    {
        _aim.X = Math.Clamp(x, 0.08, 0.92);
        _aim.Y = Math.Clamp(y, 0.12, 0.85);
        _overlay.Invalidate();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            _result.TrySetException(new InvalidOperationException("Camera permission is required to scan."));
            await Navigation.PopModalAsync();
            return;
        }
        _camera.CameraEnabled = true;
    }

    protected override void OnDisappearing()
    {
        _camera.CameraEnabled = false;
        _camera.TorchOn = false;
        _result.TrySetResult(null);
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_editing) SetEditing(false);
        else _ = CloseAsync(null);
        return true;
    }

    private void OnDetected(object? sender, OnDetectionFinishedEventArg e)
    {
        if (_result.Task.IsCompleted || _editing) return;
        foreach (var r in e.BarcodeResults)
            Android.Util.Log.Debug("AMES-PDA-CAM", $"view={_camera.Width:0}x{_camera.Height:0} d={DeviceDisplay.Current.MainDisplayInfo.Density} fmt={r.BarcodeFormat} box={r.PreviewBoundingBox} aimed={Aimed(r)} v={r.RawValue}");

        var value = (_auto ? e.BarcodeResults.FirstOrDefault() : Nearest(e.BarcodeResults))?.RawValue;
        if (string.IsNullOrEmpty(value))
        {
            _candidate = null;
            _hits = 0;
            return;
        }

        // The same value on consecutive frames avoids grabbing a code the aim only brushed past.
        if (value == _candidate) _hits++;
        else { _candidate = value; _hits = 1; }
        if (_hits < StableFrames) return;

        try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(80)); } catch { }
        MainThread.BeginInvokeOnMainThread(async () => await CloseAsync(value));
    }

    // Like a laser line: the code must lie on the horizontal beam; of several on it, the one nearest the centre wins.
    private bool Aimed(BarcodeResult result)
    {
        var (x, y, half) = AimPx();
        var box = result.PreviewBoundingBox;
        return y >= box.Top - AimSlackPx && y <= box.Bottom + AimSlackPx
            && box.Right >= x - half && box.Left <= x + half;
    }

    private BarcodeResult? Nearest(IEnumerable<BarcodeResult> results)
    {
        var (x, y, _) = AimPx();
        return results.Where(Aimed)
            .OrderBy(r => Math.Abs(r.PreviewBoundingBox.Center.X - x) + Math.Abs(r.PreviewBoundingBox.Center.Y - y))
            .FirstOrDefault();
    }

    private (double X, double Y, double Half) AimPx()
    {
        var density = DeviceDisplay.Current.MainDisplayInfo.Density;
        return (_camera.Width * density * _aim.X, _camera.Height * density * _aim.Y, _camera.Width * density * BeamHalf);
    }

    // Laser-style red crosshair: a long horizontal beam like a Zebra imager, with a short vertical tick.
    private sealed class AimDrawable : IDrawable
    {
        public double X { get; set; } = DefaultAimX;
        public double Y { get; set; } = DefaultAimY;
        public bool Visible { get; set; } = true;
        public bool Editing { get; set; }

        public void Draw(ICanvas canvas, RectF rect)
        {
            if (!Visible) return;
            float cx = (float)(rect.Width * X), cy = (float)(rect.Height * Y);
            float beam = (float)(rect.Width * BeamHalf), tick = 22f;

            canvas.StrokeLineCap = LineCap.Round;
            void Beam(Color color, float size)
            {
                canvas.StrokeColor = color;
                canvas.StrokeSize = size;
                canvas.DrawLine(cx - beam, cy, cx + beam, cy);
                canvas.DrawLine(cx, cy - tick, cx, cy + tick);
            }
            Beam(Color.FromArgb("#40ff0000"), 10);
            Beam(Color.FromArgb("#90ff1a1a"), 5);
            Beam(Color.FromArgb("#ff3b3b"), 2);

            if (Editing)
            {
                canvas.StrokeColor = Colors.White;
                canvas.StrokeSize = 2;
                canvas.StrokeDashPattern = [6, 6];
                canvas.DrawCircle(cx, cy, 34);
                canvas.StrokeDashPattern = null;
            }
        }
    }

    private async Task CloseAsync(string? value)
    {
        if (!_result.TrySetResult(value)) return;
        _camera.CameraEnabled = false;
        if (Navigation.ModalStack.Contains(this))
            await Navigation.PopModalAsync();
    }

    public static async Task<string?> ScanAsync()
    {
        var host = Application.Current?.Windows.FirstOrDefault()?.Page
            ?? throw new InvalidOperationException("Camera scan is not available.");
        var page = new CameraScanPage();
        await host.Navigation.PushModalAsync(page);
        return await page.Result;
    }
}
