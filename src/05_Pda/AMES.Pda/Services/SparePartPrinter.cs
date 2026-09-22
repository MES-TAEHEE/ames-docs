using System.Text;
using System.Globalization;
#if ANDROID
using Android.Bluetooth;
using Android.Content;
#endif

namespace AMES.Pda.Services;

public sealed class SparePartPrinter
{
    public const string DefaultModel = "ZT411";

    public sealed record Device(string Address, string? Name)
    {
        public bool HasName => !string.IsNullOrWhiteSpace(Name) && !string.Equals(Name, Address, StringComparison.OrdinalIgnoreCase);
        public string DisplayName => HasName ? Name! : "Unnamed device";
        public Device WithName(string? name) => string.IsNullOrWhiteSpace(name)
            || string.Equals(name.Trim(), Address, StringComparison.OrdinalIgnoreCase)
                ? this : this with { Name = name.Trim() };
    }

    public static string SelectAddress(IEnumerable<Device> devices, string? currentAddress)
    {
        var rows = devices.ToList();
        if (!string.IsNullOrWhiteSpace(currentAddress))
            return currentAddress;
        return rows.FirstOrDefault(x => x.DisplayName.Contains(DefaultModel, StringComparison.OrdinalIgnoreCase))?.Address ?? "";
    }

    public static bool IsValidAddress(string? address)
    {
        var parts = address?.Split(':');
        return parts is { Length: 6 }
            && parts.All(part => part.Length == 2
                && byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _));
    }

    public async Task<List<Device>> DiscoverPrintersAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        var adapter = await BluetoothAdapterAsync();
        var found = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
        void Add(BluetoothDevice? device, string? name = null)
        {
            if (device is null || string.IsNullOrWhiteSpace(device.Address)) return;
            lock (found)
            {
                var entry = (found.GetValueOrDefault(device.Address) ?? new Device(device.Address, null))
                    .WithName(device.Name).WithName(name);
                if ((entry.Name ?? "").Contains("RS5100", StringComparison.OrdinalIgnoreCase))
                    found.Remove(device.Address);
                else
                    found[device.Address] = entry;
            }
        }
        foreach (var device in adapter.BondedDevices ?? []) Add(device);
        cancellationToken.ThrowIfCancellationRequested();
        if (adapter.IsDiscovering) adapter.CancelDiscovery();
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var receiver = new DiscoveryReceiver(Add, () => finished.TrySetResult(true));
        using var filter = new IntentFilter(BluetoothDevice.ActionFound);
        filter.AddAction(BluetoothDevice.ActionNameChanged);
        filter.AddAction(BluetoothAdapter.ActionDiscoveryFinished);
        var context = Android.App.Application.Context;
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            context.RegisterReceiver(receiver, filter, ReceiverFlags.Exported);
        else
            context.RegisterReceiver(receiver, filter);
        try
        {
            if (!adapter.StartDiscovery())
                throw new InvalidOperationException("Bluetooth search could not start. Check Bluetooth and Nearby devices permission, then retry.");
            try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); }
            catch (TimeoutException) { } // Return everything found within one discovery cycle.
        }
        finally
        {
            try { if (adapter.IsDiscovering) adapter.CancelDiscovery(); }
            finally { context.UnregisterReceiver(receiver); }
        }
        // Discovery may resolve the friendly name after the first ACTION_FOUND broadcast.
        foreach (var address in found.Keys.ToArray())
        {
            using var device = adapter.GetRemoteDevice(address);
            Add(device);
        }
        return found.Values.OrderByDescending(d => d.HasName)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Address).ToList();
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Bluetooth printer search is available on the Android PDA.");
#endif
    }

    public async Task SendAsync(string address, string zpl)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("Select a printer first.");
#if ANDROID
        var adapter = await BluetoothAdapterAsync();
        if (adapter.IsDiscovering) adapter.CancelDiscovery();
        var bytes = Encoding.UTF8.GetBytes(zpl);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var device = adapter.GetRemoteDevice(address);
        using var uuid = Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;
        using var socket = device?.CreateRfcommSocketToServiceRecord(uuid)
            ?? throw new InvalidOperationException("Could not open the Bluetooth printer.");
        using var cancel = timeout.Token.Register(() => { try { socket.Close(); } catch { } });
        await Task.Run(() => socket.Connect(), timeout.Token);
        var output = socket.OutputStream ?? throw new InvalidOperationException("Printer output is unavailable.");
        await output.WriteAsync(bytes, timeout.Token);
        await output.FlushAsync(timeout.Token);
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Bluetooth printing is available on the Android PDA.");
#endif
        // Never retry automatically: a failed write may already have printed a label.
    }

#if ANDROID
    private static async Task<Android.Bluetooth.BluetoothAdapter> BluetoothAdapterAsync()
    {
        if (await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<PrinterBluetoothPermission>()) != PermissionStatus.Granted)
            throw new InvalidOperationException("Allow Nearby devices permission to search and connect the printer (Location permission on Android 11 or earlier).");
        var manager = (Android.Bluetooth.BluetoothManager?)Android.App.Application.Context
            .GetSystemService(Android.Content.Context.BluetoothService);
        var adapter = manager?.Adapter;
        if (adapter is null || !adapter.IsEnabled)
            throw new InvalidOperationException("Turn on Bluetooth and make the Zebra printer discoverable.");
        return adapter;
    }

    public sealed class PrinterBluetoothPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(31)
                ? [("android.permission.BLUETOOTH_CONNECT", true), ("android.permission.BLUETOOTH_SCAN", true)]
                : [("android.permission.BLUETOOTH", false), ("android.permission.BLUETOOTH_ADMIN", false),
                   ("android.permission.ACCESS_FINE_LOCATION", true)];
    }

    private sealed class DiscoveryReceiver(Action<BluetoothDevice?, string?> found, Action finished) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == BluetoothDevice.ActionFound || intent?.Action == BluetoothDevice.ActionNameChanged)
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    using var type = Java.Lang.Class.FromType(typeof(BluetoothDevice));
                    found(intent.GetParcelableExtra(BluetoothDevice.ExtraDevice, type) as BluetoothDevice,
                        intent.GetStringExtra(BluetoothDevice.ExtraName));
                }
                else
                    found(intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice,
                        intent.GetStringExtra(BluetoothDevice.ExtraName));
            }
            else if (intent?.Action == BluetoothAdapter.ActionDiscoveryFinished) finished();
        }
    }
#endif
}
