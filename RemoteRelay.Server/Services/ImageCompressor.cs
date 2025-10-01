using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RemoteRelay.Server.Services;

public static class ImageCompressor
{
    private const int MaxDimension = 1280;
    private const int JpegQuality = 75;

    public static async Task<(byte[] Data, string ContentType)> LoadAndCompressAsync(string path, CancellationToken cancellationToken = default)
    {
    await using var stream = File.OpenRead(path);
        using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken).ConfigureAwait(false);

        ResizeIfNecessary(image);
        FlattenTransparency(image);

        await using var output = new MemoryStream();
        var encoder = new JpegEncoder { Quality = JpegQuality };
        await image.SaveAsJpegAsync(output, encoder, cancellationToken).ConfigureAwait(false);
        return (output.ToArray(), "image/jpeg");
    }

    private static void ResizeIfNecessary(Image image)
    {
        var largestDimension = Math.Max(image.Width, image.Height);
        if (largestDimension <= MaxDimension)
        {
            return;
        }

        var scale = MaxDimension / (double)largestDimension;
        var newWidth = (int)Math.Round(image.Width * scale);
        var newHeight = (int)Math.Round(image.Height * scale);

        image.Mutate(ctx => ctx.Resize(newWidth, newHeight));
    }

    private static void FlattenTransparency(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];
                    if (pixel.A == byte.MaxValue)
                    {
                        continue;
                    }

                    var alpha = pixel.A / 255f;
                    var inverse = 1f - alpha;

                    pixel.R = (byte)Math.Clamp(pixel.R * alpha + 255f * inverse, 0f, 255f);
                    pixel.G = (byte)Math.Clamp(pixel.G * alpha + 255f * inverse, 0f, 255f);
                    pixel.B = (byte)Math.Clamp(pixel.B * alpha + 255f * inverse, 0f, 255f);
                    pixel.A = byte.MaxValue;
                }
            }
        });
    }
}
