using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;

namespace AMES.Pda;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const string LogTag = "AMES-PDA-SCAN";
    private const string ClaimedBarcodeAction = "com.seyon.ames.pda.action.BARCODE_DATA";
    private const string HoneywellDecodePermission = "com.honeywell.decode.permission.DECODE";
    private const string ActionClaimScanner = "com.honeywell.aidc.action.ACTION_CLAIM_SCANNER";
    private const string ActionReleaseScanner = "com.honeywell.aidc.action.ACTION_RELEASE_SCANNER";
    private const string ExtraScanner = "com.honeywell.aidc.extra.EXTRA_SCANNER";
    private const string ExtraProfile = "com.honeywell.aidc.extra.EXTRA_PROFILE";
    private const string ExtraProperties = "com.honeywell.aidc.extra.EXTRA_PROPERTIES";

    private static readonly string[] BarcodeExtraKeys =
    [
        "data",
        "com.honeywell.decode.intent.extra.BARCODE_DATA",
        "com.honeywell.aidc.extra.EXTRA_BARCODE_DATA",
        "com.honeywell.aidc.extra.BARCODE_DATA"
    ];

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

    private void RegisterBarcodeBroadcastReceiver()
    {
        if (_barcodeReceiver is not null)
            return;

        _barcodeReceiver = new BarcodeBroadcastReceiver();
        var filter = new IntentFilter(ClaimedBarcodeAction);
        filter.AddCategory(Intent.CategoryDefault);

        // Honeywell's external decode service sends the claimed action. Its signature
        // permission rejects broadcasts from other apps while keeping the receiver exported.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            RegisterReceiver(_barcodeReceiver, filter, HoneywellDecodePermission, null, ReceiverFlags.Exported);
        else
            RegisterReceiver(_barcodeReceiver, filter, HoneywellDecodePermission, null);

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
            if (intent?.Action != ClaimedBarcodeAction)
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

            return null;
        }
    }
}
