using AMES.Api.Endpoints;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgDefectPhotoTests
{
    private static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6Z9sAAAAASUVORK5CYII=");

    [Fact]
    public void Accepts_png_evidence() => Assert.Equal("png", FgDefectPhotos.Validate(Png, "image/png"));

    [Fact]
    public void Rejects_forged_mime_type_and_non_image_payload()
    {
        Assert.Throws<ArgumentException>(() => FgDefectPhotos.Validate(Png, "image/jpeg"));
        Assert.Throws<ArgumentException>(() => FgDefectPhotos.Validate(new byte[50], "image/png"));
        Assert.Throws<ArgumentException>(() => FgDefectPhotos.Validate(System.Text.Encoding.UTF8.GetBytes("<svg onload='alert(1)'></svg>"), "image/svg+xml"));
    }

    [Fact]
    public void Requires_evidence_with_a_bounded_size()
    {
        Assert.Throws<ArgumentException>(() => FgDefectPhotos.Validate(null, "image/jpeg"));
        Assert.Throws<ArgumentException>(() => FgDefectPhotos.Validate([], "image/png"));
        Assert.Throws<ArgumentException>(() => FgDefectPhotos.Validate(new byte[FgDefectPhotos.MaxBytes + 1], "image/png"));
    }
}
