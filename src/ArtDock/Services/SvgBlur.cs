namespace ArtDock.Services;

/// <summary>
/// A Gaussian blur as SVG defines one, over a plane of values: the shadow a drawn folder's symbol
/// casts (<see cref="FolderArt"/>) and the one an icon on the dock casts (<see cref="IconShadow"/>).
/// </summary>
internal static class SvgBlur
{
    /// <summary>
    /// Blurs one way, as SVG defines its Gaussian blur for a deviation of two pixels or more:
    /// three box blurs of d = ⌊s × 3√(2π) / 4 + ½⌋ pixels — or, for an even d, two of d centred
    /// half a pixel either side and one of d + 1 — which a browser draws the same.
    /// </summary>
    public static void Blur(float[] values, int width, int height, double deviation, bool across)
    {
        var d = (int)Math.Floor((deviation * 3 * Math.Sqrt(2 * Math.PI) / 4) + 0.5);
        if (d < 2)
        {
            return;
        }

        var half = d / 2;
        if (d % 2 == 1)
        {
            BoxBlur(values, width, height, half, half, across);
            BoxBlur(values, width, height, half, half, across);
            BoxBlur(values, width, height, half, half, across);
        }
        else
        {
            BoxBlur(values, width, height, half, half - 1, across);
            BoxBlur(values, width, height, half - 1, half, across);
            BoxBlur(values, width, height, half, half, across);
        }
    }

    /// <summary>
    /// A box blur one way: each value becomes the mean of those from <paramref name="before"/>
    /// before it to <paramref name="after"/> after, with nothing beyond the edge.
    /// </summary>
    private static void BoxBlur(float[] values, int width, int height, int before, int after, bool across)
    {
        var (lines, length, next, step) = across ? (height, width, width, 1) : (width, height, 1, width);
        var line = new float[length];
        var box = (double)(before + after + 1);
        for (var l = 0; l < lines; l++)
        {
            var start = l * next;
            for (var i = 0; i < length; i++)
            {
                line[i] = values[start + (i * step)];
            }

            var sum = 0.0;
            for (var i = 0; i <= after && i < length; i++)
            {
                sum += line[i];
            }

            for (var i = 0; i < length; i++)
            {
                values[start + (i * step)] = (float)(sum / box);
                if (i + after + 1 < length)
                {
                    sum += line[i + after + 1];
                }

                if (i - before >= 0)
                {
                    sum -= line[i - before];
                }
            }
        }
    }
}
