namespace AMES.Web.Services;

/// <summary>DB 에 바이트로 저장된 이미지(MD_SparePart.SparePartImage 등)를 화면에 보이기 위한 도우미 — 형식은 매직 바이트로 판별한다.</summary>
public static class ImageBytes
{
    public static string ContentType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "image/gif";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return "image/webp";
        return "application/octet-stream";
    }

    public static string DataUrl(byte[] bytes) => $"data:{ContentType(bytes)};base64,{Convert.ToBase64String(bytes)}";
}
