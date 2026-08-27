using System;
using System.IO;
using System.IO.Compression;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Bounded gzip for sync envelopes. Standard gzip format
/// (interoperable with the game's own Utils.Compress), but decompression
/// aborts mid-stream the moment output exceeds the cap — a hostile
/// envelope can never balloon memory (SEC-1.0-001 finding 1).</summary>
internal static class AtlasCompression
{
    public static byte[] Compress(byte[] input)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(input, 0, input.Length);
        }

        return output.ToArray();
    }

    /// <summary>Returns false (with a null result) when the stream is
    /// corrupt or would exceed <paramref name="maxOutputBytes"/>.</summary>
    public static bool TryDecompress(byte[] input, int maxOutputBytes, out byte[] output)
    {
        output = null!;
        try
        {
            using var source = new MemoryStream(input, writable: false);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var result = new MemoryStream();
            byte[] buffer = new byte[16 * 1024];
            int total = 0;
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxOutputBytes)
                {
                    return false;
                }

                result.Write(buffer, 0, read);
            }

            output = result.ToArray();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
