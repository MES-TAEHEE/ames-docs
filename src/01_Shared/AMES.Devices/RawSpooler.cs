using System.Runtime.InteropServices;

namespace AMES.Devices;

/// <summary>
/// Windows 프린터 큐에 RAW 바이트를 직접 보낸다 — USB 등 로컬 연결 프린터용.
/// 드라이버 렌더링을 거치지 않으므로 ZPL 이 그대로 프린터에 전달된다. Windows 전용.
/// </summary>
internal static class RawSpooler
{
    public static void Send(string printerName, byte[] data, string docName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Spooler mode is only supported on Windows");

        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new InvalidOperationException(
                $"OpenPrinter failed for '{printerName}' (win32 {Marshal.GetLastWin32Error()})");
        try
        {
            var doc = new DOC_INFO_1 { pDocName = docName, pOutputFile = null, pDataType = "RAW" };
            if (!StartDocPrinter(handle, 1, ref doc))
                throw new InvalidOperationException(
                    $"StartDocPrinter failed for '{printerName}' (win32 {Marshal.GetLastWin32Error()})");
            try
            {
                StartPagePrinter(handle);
                if (!WritePrinter(handle, data, data.Length, out var written) || written != data.Length)
                    throw new InvalidOperationException(
                        $"WritePrinter failed for '{printerName}' (win32 {Marshal.GetLastWin32Error()})");
                EndPagePrinter(handle);
            }
            finally { EndDocPrinter(handle); }
        }
        finally { ClosePrinter(handle); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        public string  pDocName;
        public string? pOutputFile;
        public string  pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr handle, int level, ref DOC_INFO_1 di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] data, int count, out int written);
}
