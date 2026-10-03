using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlutoniumLauncher;

public static class PlayerHead
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    public static async Task<ImageSource?> LoadAsync(string? url, string dataDirectory)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "textures.minecraft.net") return null;
        uri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
        var directory = Path.Combine(dataDirectory, "skins");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))) + ".png");
        var bytes = File.Exists(file) ? await File.ReadAllBytesAsync(file) : await Http.GetByteArrayAsync(uri);
        if (bytes.Length > 2_000_000) return null;
        using var stream = new MemoryStream(bytes);
        var skin = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        if (skin.PixelWidth < 64 || skin.PixelWidth > 1024 || skin.PixelWidth % 64 != 0) return null;
        int unit = skin.PixelWidth / 64;
        if (skin.PixelHeight < 32 * unit) return null;
        var face = new CroppedBitmap(skin, new Int32Rect(8 * unit, 8 * unit, 8 * unit, 8 * unit));
        var hat = new CroppedBitmap(skin, new Int32Rect(40 * unit, 8 * unit, 8 * unit, 8 * unit));
        // Compose the original pixels before scaling: RenderTargetBitmap resampling blurred the face.
        var basePixels = new byte[face.PixelWidth * face.PixelHeight * 4];
        var hatPixels = new byte[basePixels.Length];
        new FormatConvertedBitmap(face, PixelFormats.Bgra32, null, 0).CopyPixels(basePixels, face.PixelWidth * 4, 0);
        new FormatConvertedBitmap(hat, PixelFormats.Bgra32, null, 0).CopyPixels(hatPixels, face.PixelWidth * 4, 0);
        const int size = 1080;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            int source = ((y * face.PixelHeight / size) * face.PixelWidth + x * face.PixelWidth / size) * 4;
            int target = (y * size + x) * 4, alpha = hatPixels[source + 3];
            for (int c = 0; c < 3; c++) pixels[target + c] = (byte)((hatPixels[source + c] * alpha + basePixels[source + c] * (255 - alpha)) / 255);
            pixels[target + 3] = 255;
        }
        var head = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        head.Freeze();
        if (!File.Exists(file)) await File.WriteAllBytesAsync(file, bytes);
        return head;
    }
}
