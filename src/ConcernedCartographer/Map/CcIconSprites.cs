using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Loads the embedded cc:* marker sprites (RC8) and caches them
/// for the pin adapter, palette, and workbench previews.
///
/// The sprites are a RENDERING override only: the saved vanilla pin keeps
/// its registry fallback PinType, so uninstalling the mod (or opening the
/// atlas with an older version) degrades every marker to a sensible
/// vanilla icon — nothing about the sprite is ever persisted. Fails soft:
/// a missing/corrupt resource renders the vanilla sprite instead.
///
/// The PNGs are decoded here directly (RGBA8, non-interlaced, filter-0
/// rows — exactly what tools/generate_icon_sprites.py writes) because the
/// game's UnityEngine.ImageConversionModule targets netstandard 2.1 and
/// cannot be referenced from this net48 plugin at compile time.</summary>
internal static class CcIconSprites
{
    private const string ResourcePrefix = "CC.Icons.";

    private static readonly Dictionary<string, Sprite> Cache = new();
    private static readonly HashSet<string> Failed = new();

    /// <summary>The distinct CC sprite for an icon id, when the id is a
    /// registry entry that ships one and it loaded successfully.</summary>
    public static bool TryGet(string? iconId, out Sprite sprite)
    {
        sprite = null!;
        if (string.IsNullOrEmpty(iconId) ||
            !IconRegistry.TryResolve(iconId, out IconRegistry.IconDefinition definition) ||
            !definition.HasCustomSprite)
        {
            return false;
        }

        string key = definition.SpriteKey;
        if (Failed.Contains(key))
        {
            return false;
        }

        // Unity fake-null: a destroyed sprite (aggressive asset unload)
        // reloads transparently on the next request.
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null)
        {
            sprite = cached;
            return true;
        }

        Sprite? loaded = Load(key);
        if (loaded == null)
        {
            Failed.Add(key);
            return false;
        }

        Cache[key] = loaded;
        sprite = loaded;
        return true;
    }

    private static Sprite? Load(string key)
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(ResourcePrefix + key + ".png");
            if (stream is null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            if (!TryDecodePng(memory.ToArray(), out int width, out int height, out Color32[] pixels))
            {
                return null;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "CCIcon_" + key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = "CCIcon_" + key;
            return sprite;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Minimal decoder for this project's own sprite PNGs:
    /// 8-bit RGBA, non-interlaced, every scanline filter 0. Returns pixels
    /// bottom-up as Texture2D expects. Anything else fails soft.</summary>
    private static bool TryDecodePng(byte[] data, out int width, out int height, out Color32[] pixels)
    {
        width = 0;
        height = 0;
        pixels = null!;

        if (data.Length < 8 ||
            data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
        {
            return false;
        }

        using var idat = new MemoryStream();
        int position = 8;
        while (position + 8 <= data.Length)
        {
            int length = ReadBigEndianInt(data, position);
            string tag = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
            int body = position + 8;
            if (length < 0 || body + length > data.Length)
            {
                return false;
            }

            if (tag == "IHDR")
            {
                width = ReadBigEndianInt(data, body);
                height = ReadBigEndianInt(data, body + 4);
                byte bitDepth = data[body + 8];
                byte colorType = data[body + 9];
                byte interlace = data[body + 12];
                if (bitDepth != 8 || colorType != 6 || interlace != 0)
                {
                    return false;
                }
            }
            else if (tag == "IDAT")
            {
                idat.Write(data, body, length);
            }
            else if (tag == "IEND")
            {
                break;
            }

            position = body + length + 4;
        }

        if (width <= 0 || height <= 0 || width > 512 || height > 512 || idat.Length < 6)
        {
            return false;
        }

        // zlib wrapper: 2-byte header up front, 4-byte Adler at the end.
        idat.Position = 2;
        byte[] raw = new byte[(width * 4 + 1) * height];
        using (var inflate = new DeflateStream(idat, CompressionMode.Decompress, leaveOpen: true))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int chunk = inflate.Read(raw, read, raw.Length - read);
                if (chunk <= 0)
                {
                    return false;
                }

                read += chunk;
            }
        }

        pixels = new Color32[width * height];
        int stride = width * 4 + 1;
        for (int y = 0; y < height; y++)
        {
            if (raw[y * stride] != 0)
            {
                // Not a filter-0 scanline: not one of our sprites.
                return false;
            }

            // PNG rows are top-down; Texture2D pixel rows are bottom-up.
            int target = (height - 1 - y) * width;
            int source = y * stride + 1;
            for (int x = 0; x < width; x++)
            {
                pixels[target + x] = new Color32(
                    raw[source + (x * 4)],
                    raw[source + (x * 4) + 1],
                    raw[source + (x * 4) + 2],
                    raw[source + (x * 4) + 3]);
            }
        }

        return true;
    }

    private static int ReadBigEndianInt(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
