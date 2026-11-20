using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;

namespace AMES.Pda.Services;

internal sealed record PdaDefectPhoto(byte[] Bytes, string ContentType)
{
    public string Preview => $"data:{ContentType};base64,{Convert.ToBase64String(Bytes)}";
    public static async Task<PdaDefectPhoto?> PickAsync(bool capture)
    {
        if (capture && !MediaPicker.Default.IsCaptureSupported)
            throw new InvalidOperationException("No camera is available. Use SELECT PHOTO instead.");
        var file = await MainThread.InvokeOnMainThreadAsync(() => capture
            ? MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
            {
                MaximumWidth = 1600, MaximumHeight = 1600, CompressionQuality = 80,
                RotateImage = true, PreserveMetaData = false
            })
            : FilePicker.Default.PickAsync(new PickOptions { FileTypes = FilePickerFileType.Images, PickerTitle = "Select defect photo" }));
        if (file is null) return null;
        await using var input = await file.OpenReadAsync();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length + count > 3 * 1024 * 1024)
                throw new InvalidOperationException("Photo is larger than 3 MB. Take a new photo or select a smaller JPEG/PNG image.");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        var type = bytes.Length > 8 && bytes[0] == 137 && bytes[1] == 80 ? "image/png"
            : bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 ? "image/jpeg" : null;
        if (type is null) throw new InvalidOperationException("Select a JPEG or PNG defect photo.");
        return new(bytes, type);
    }
}
