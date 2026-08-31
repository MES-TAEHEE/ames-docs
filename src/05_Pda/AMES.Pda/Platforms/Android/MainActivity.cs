using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using System.Text;

namespace AMES.Pda;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const string LogTag = "AMES-PDA-SCAN";
    private const string ClaimedBarcodeAction = "com.seyon.ames.pda.action.BARCODE_DATA";
    private const string ActionClaimScanner = "com.honeywell.aidc.action.ACTION_CLAIM_SCANNER";
    private const string ActionReleaseScanner = "com.honeywell.aidc.action.ACTION_RELEASE_SCANNER";
    private const string ExtraScanner = "com.honeywell.aidc.extra.EXTRA_SCANNER";
    private const string ExtraProfile = "com.honeywell.aidc.extra.EXTRA_PROFILE";
    private const string ExtraProperties = "com.honeywell.aidc.extra.EXTRA_PROPERTIES";

    private static readonly string[] BarcodeBroadcastActions =
    [
        ClaimedBarcodeAction,
        "com.honeywell.decode.intent.action.BARCODE_DATA",
        "com.honeywell.aidc.action.ACTION_BARCODE_READ_EVENT",
        "com.intermec.datacollection.action.BARCODE_DATA",
        "com.intermec.datacollectionservice.action.BARCODE_DATA",
        "android.intent.action.SCANRESULT",
        "com.android.server.scannerservice.broadcast",
        "com.honeywell.scan.intent.action.BARCODE_DATA"
    ];

    private static readonly string[] BarcodeExtraKeys =
    [
        "data",
        "barcodeData",
        "barcodedata",
        "BarcodeData",
        "SCAN_RESULT",
        "scan_result",
        "com.honeywell.decode.intent.extra.BARCODE_DATA",
        "com.honeywell.decode.intent.extra.BARCODE_STRING",
        "com.honeywell.aidc.extra.EXTRA_BARCODE_DATA",
        "com.honeywell.aidc.extra.BARCODE_DATA",
        "com.intermec.datacollection.data"
    ];

    private readonly StringBuilder _scanBuffer = new();
    private Handler? _scanHandler;
    private int _scanVersion;
    private BroadcastReceiver? _barcodeReceiver;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetSoftInputMode(SoftInput.StateAlwaysHidden | SoftInput.AdjustPan);
        Log.Debug(LogTag, "MainActivity created.");
    }

    protected override void OnResume()
    {
        base.OnResume();
        RegisterBarcodeBroadcastReceiver();
        ClaimHoneywellScanner();
    }

    protected override void OnPause()
    {
        ReleaseHoneywellScanner();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        UnregisterBarcodeBroadcastReceiver();
        base.OnDestroy();
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is null)
            return base.DispatchKeyEvent(e);

        if (e.Action != KeyEventActions.Down)
            return base.DispatchKeyEvent(e);

        Log.Debug(LogTag, $"DispatchKeyEvent key={e.KeyCode} unicode={e.UnicodeChar}");

        if (e.KeyCode is Keycode.Enter or Keycode.NumpadEnter or Keycode.Tab)
        {
            if (FlushScanBuffer())
                return true;
        }

        var unicode = e.UnicodeChar;
        if (unicode > 0)
        {
            var ch = (char)unicode;
            if (!char.IsControl(ch))
            {
                _scanBuffer.Append(ch);
                Log.Debug(LogTag, $"Buffered char '{ch}', length={_scanBuffer.Length}");
                ScheduleScanFlush();
                return true;
            }
        }

        return base.DispatchKeyEvent(e);
    }

    private void ScheduleScanFlush()
    {
        _scanHandler ??= new Handler(Looper.MainLooper!);
        var version = ++_scanVersion;
        _scanHandler.PostDelayed(() =>
        {
            if (version == _scanVersion)
                FlushScanBuffer();
        }, 160);
    }

    private bool FlushScanBuffer()
    {
        var text = _scanBuffer.ToString();
        _scanBuffer.Clear();
        _scanVersion++;

        if (text.Trim().Length < 3)
            return false;

        Log.Debug(LogTag, $"Dispatch scan flushed: {text}");
        PdaBarcodeHub.Publish(text);
        return true;
    }

    private void RegisterBarcodeBroadcastReceiver()
    {
        if (_barcodeReceiver is not null)
            return;

        _barcodeReceiver = new BarcodeBroadcastReceiver();
        var filter = new IntentFilter();
        foreach (var action in BarcodeBroadcastActions)
            filter.AddAction(action);

        filter.AddCategory(Intent.CategoryDefault);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(_barcodeReceiver, filter, ReceiverFlags.Exported);
        else
            RegisterReceiver(_barcodeReceiver, filter);

        Log.Debug(LogTag, "Barcode receiver registered.");
    }

    private void UnregisterBarcodeBroadcastReceiver()
    {
        if (_barcodeReceiver is null)
            return;

        try
        {
            UnregisterReceiver(_barcodeReceiver);
        }
        catch
        {
            // Ignore teardown races.
        }
        finally
        {
            _barcodeReceiver = null;
        }
    }

    private void ClaimHoneywellScanner()
    {
        try
        {
            var properties = new Bundle();
            properties.PutBoolean("DPR_DATA_INTENT", true);
            properties.PutString("DPR_DATA_INTENT_ACTION", ClaimedBarcodeAction);
            properties.PutInt("TRIG_AUTO_MODE_TIMEOUT", 2);
            properties.PutString("TRIG_SCAN_MODE", "readOnRelease");

            var intent = new Intent(ActionClaimScanner)
                .PutExtra(ExtraScanner, "dcs.scanner.imager")
                .PutExtra(ExtraProfile, "DEFAULT")
                .PutExtra(ExtraProperties, properties);

            SendHoneywellIntent(intent);
            Log.Debug(LogTag, $"Honeywell scanner claimed with action={ClaimedBarcodeAction}");
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Honeywell scanner claim failed: {ex}");
        }
    }

    private void ReleaseHoneywellScanner()
    {
        try
        {
            SendHoneywellIntent(new Intent(ActionReleaseScanner));
            Log.Debug(LogTag, "Honeywell scanner released.");
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Honeywell scanner release failed: {ex}");
        }
    }

    private void SendHoneywellIntent(Intent intent)
    {
        var matches = PackageManager?.QueryBroadcastReceivers(intent, 0);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O && matches is { Count: > 0 })
        {
            foreach (var resolveInfo in matches)
            {
                var activityInfo = resolveInfo.ActivityInfo;
                if (activityInfo?.ApplicationInfo?.PackageName is null || activityInfo.Name is null)
                    continue;

                var explicitIntent = new Intent(intent);
                explicitIntent.SetComponent(new ComponentName(activityInfo.ApplicationInfo.PackageName, activityInfo.Name));
                SendBroadcast(explicitIntent);
            }

            return;
        }

        SendBroadcast(intent);
    }

    private sealed class BarcodeBroadcastReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is null)
                return;

            Log.Debug(LogTag, $"Broadcast received action={intent.Action}");
            var barcode = ExtractBarcode(intent);
            if (string.IsNullOrWhiteSpace(barcode))
            {
                Log.Debug(LogTag, "Broadcast received without known barcode extra.");
                return;
            }

            Log.Debug(LogTag, $"Broadcast scan received: {barcode}");
            PdaBarcodeHub.Publish(barcode);
        }

        private static string? ExtractBarcode(Intent intent)
        {
            foreach (var key in BarcodeExtraKeys)
            {
                var value = intent.GetStringExtra(key);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            var extras = intent.Extras;
            if (extras is null)
                return null;

            foreach (var key in extras.KeySet())
            {
                var raw = extras.Get(key)?.ToString();
                Log.Debug(LogTag, $"Broadcast extra {key}={raw}");
                if (!string.IsNullOrWhiteSpace(raw) && raw.Length >= 3)
                    return raw;
            }

            return null;
        }
    }
}
