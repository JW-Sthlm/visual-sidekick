using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VisualSidekick.Server;

internal static class VisualChangeDetector
{
    internal const int MeaningfulChangeThreshold = 2;

    internal static bool IsMeaningful(int distance) =>
        distance >= MeaningfulChangeThreshold;

    internal static byte[] ComputeSignature(Bitmap source)
    {
        using var sample = new Bitmap(64, 36, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(sample))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(source, new Rectangle(0, 0, sample.Width, sample.Height));
        }

        var signature = new byte[sample.Width * sample.Height * 3];
        var index = 0;
        for (var y = 0; y < sample.Height; y++)
        {
            for (var x = 0; x < sample.Width; x++)
            {
                var pixel = sample.GetPixel(x, y);
                signature[index++] = pixel.R;
                signature[index++] = pixel.G;
                signature[index++] = pixel.B;
            }
        }

        return signature;
    }

    internal static int ComputeDistance(byte[] first, byte[] second)
    {
        if (first.Length != second.Length || first.Length == 0)
        {
            return 255;
        }

        long channelDifference = 0;
        var changedPixels = 0;
        var pixelCount = first.Length / 3;

        for (var index = 0; index < first.Length; index += 3)
        {
            var red = Math.Abs(first[index] - second[index]);
            var green = Math.Abs(first[index + 1] - second[index + 1]);
            var blue = Math.Abs(first[index + 2] - second[index + 2]);
            var pixelDifference = red + green + blue;

            channelDifference += pixelDifference;
            if (pixelDifference >= 45)
            {
                changedPixels++;
            }
        }

        var averageChannelDifference = (double)channelDifference / first.Length;
        var changedPixelPercentage = pixelCount == 0
            ? 0
            : changedPixels * 100d / pixelCount;

        return (int)Math.Round(Math.Max(averageChannelDifference, changedPixelPercentage));
    }
}
