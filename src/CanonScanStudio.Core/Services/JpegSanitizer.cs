namespace CanonScanStudio.Services;

/// <summary>
/// Canon eSCL JPEGs often carry broken EXIF/ICC APP segments. ImageSharp and WPF
/// can throw or paint a white frame while still being able to Identify() the file.
/// </summary>
internal static class JpegSanitizer
{
    public static byte[] StripProblematicSegments(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return jpeg;
        }

        using var output = new MemoryStream(jpeg.Length);
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);
        var i = 2;
        while (i + 1 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                output.Write(jpeg, i, jpeg.Length - i);
                break;
            }

            var marker = jpeg[i + 1];
            if (marker == 0x00 || marker == 0xFF)
            {
                output.WriteByte(0xFF);
                i++;
                continue;
            }

            if (marker is 0xD8)
            {
                i += 2;
                continue;
            }

            if (marker is 0xD9)
            {
                output.WriteByte(0xFF);
                output.WriteByte(0xD9);
                break;
            }

            if (marker is >= 0xD0 and <= 0xD7)
            {
                output.WriteByte(0xFF);
                output.WriteByte(marker);
                i += 2;
                continue;
            }

            if (marker is 0xDA)
            {
                output.Write(jpeg, i, jpeg.Length - i);
                break;
            }

            if (i + 3 >= jpeg.Length)
            {
                output.Write(jpeg, i, jpeg.Length - i);
                break;
            }

            var payload = (jpeg[i + 2] << 8) | jpeg[i + 3];
            var total = 2 + payload;
            if (payload < 2 || i + total > jpeg.Length)
            {
                output.Write(jpeg, i, jpeg.Length - i);
                break;
            }

            // Keep JFIF (APP0) and Adobe (APP14) so YCCK/CMYK is still understood.
            var drop = marker is (>= 0xE1 and <= 0xED) or 0xEF or 0xFE;
            if (!drop)
            {
                output.Write(jpeg, i, total);
            }

            i += total;
        }

        return output.ToArray();
    }
}
