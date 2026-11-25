using System;
using System.Collections.Generic;
using InventoryParticles.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace InventoryParticles.Particles;

/// <summary>
/// White particle sprites with an alpha mask, generated on the first frame. Every shape exists in several sizes
/// because textures are minified without mipmaps: drawing a large sprite a few pixels wide makes it shimmer.
/// </summary>
internal sealed class ParticleTextures : IDisposable
{
    /// <summary> Keys at or above this refer to textures loaded from assets. </summary>
    public const int AssetKeyBase = 100;

    private static readonly int[] Sizes = { 4, 8, 16, 32, 64 };
    private static readonly int ShapeCount = Enum.GetValues<EnumGuiParticleShape>().Length;

    private readonly ICoreClientAPI capi;
    private readonly ILogger logger;
    private readonly LoadedTexture?[] generated = new LoadedTexture?[ShapeCount * Sizes.Length];
    private readonly List<int> assetTextureIds = new();
    private readonly Dictionary<string, int> assetKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool created;

    public ParticleTextures(ICoreClientAPI capi, ILogger logger)
    {
        this.capi = capi;
        this.logger = logger;
    }

    /// <summary> Texture key for new particles of an emitter. </summary>
    public int GetKey(GuiEmitterProps props)
    {
        if (string.IsNullOrWhiteSpace(props.Texture))
        {
            return (int)props.Shape;
        }

        if (assetKeys.TryGetValue(props.Texture, out int key))
        {
            return key;
        }

        int textureId = 0;

        try
        {
            textureId = capi.Render.GetOrLoadTexture(new AssetLocation(props.Texture));
        }
        catch (Exception e)
        {
            logger.Warning("Could not load particle texture {0}, using the {1} shape: {2}", props.Texture, props.Shape, e.Message);
        }

        if (textureId > 0)
        {
            key = AssetKeyBase + assetTextureIds.Count;
            assetTextureIds.Add(textureId);
        }
        else
        {
            key = (int)props.Shape;
        }

        assetKeys[props.Texture] = key;
        return key;
    }

    /// <summary> OpenGL texture for a particle drawn <paramref name="drawnSize"/> pixels wide. Must be called on the main thread. </summary>
    public int GetTextureId(int key, float drawnSize)
    {
        if (key >= AssetKeyBase)
        {
            return assetTextureIds[key - AssetKeyBase];
        }

        EnsureCreated();

        int shape = Math.Clamp(key, 0, ShapeCount - 1);
        int size = 0;

        while (size < Sizes.Length - 1 && Sizes[size] < drawnSize)
        {
            size++;
        }

        return generated[shape * Sizes.Length + size]?.TextureId ?? 0;
    }

    public void Dispose()
    {
        for (int i = 0; i < generated.Length; i++)
        {
            generated[i]?.Dispose();
            generated[i] = null;
        }

        // Asset textures belong to the game's texture cache.
        assetTextureIds.Clear();
        assetKeys.Clear();
        created = false;
    }

    private void EnsureCreated()
    {
        if (created)
        {
            return;
        }

        created = true;

        for (int shape = 0; shape < ShapeCount; shape++)
        {
            for (int s = 0; s < Sizes.Length; s++)
            {
                int n = Sizes[s];
                var texture = new LoadedTexture(capi, 0, n, n);
                capi.Render.LoadOrUpdateTextureFromRgba(CreatePixels((EnumGuiParticleShape)shape, n), true, 1, ref texture);
                generated[shape * Sizes.Length + s] = texture;
            }
        }
    }

    /// <summary> White pixels with the shape in the alpha channel. White is the same in every channel order. </summary>
    internal static int[] CreatePixels(EnumGuiParticleShape shape, int n)
    {
        var pixels = new int[n * n];

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                // Pixel centre in -1..1.
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                int alpha = (int)MathF.Round(Math.Clamp(Alpha(shape, u, v), 0f, 1f) * 255f);
                pixels[y * n + x] = (alpha << 24) | 0x00FFFFFF;
            }
        }

        return pixels;
    }

    /// <summary> A round body in the lower part and a point at the top. Row 0 of a texture is drawn at the top of the screen. </summary>
    private static float DropAlpha(float u, float v)
    {
        const float bodyY = 0.3f;
        const float bodyRadius = 0.62f;
        const float tipY = -0.95f;
        const float edge = 0.16f;

        float dy = v - bodyY;
        float body = (bodyRadius - MathF.Sqrt(u * u + dy * dy)) / edge;
        float tip = 0f;

        if (v < bodyY)
        {
            // The sides run from the point down to the widest part of the body.
            float t = Math.Clamp((v - tipY) / (bodyY - tipY), 0f, 1f);
            float halfWidth = bodyRadius * MathF.Pow(t, 1.2f);
            tip = MathF.Min((halfWidth - MathF.Abs(u)) / edge, (v - tipY) / edge);
        }

        float alpha = Math.Clamp(MathF.Max(body, tip), 0f, 1f);
        return alpha * alpha * (3f - 2f * alpha);
    }

    internal static float Alpha(EnumGuiParticleShape shape, float u, float v)
    {
        float r = MathF.Sqrt(u * u + v * v);

        switch (shape)
        {
            case EnumGuiParticleShape.Dot:
                float dot = Math.Clamp((1f - r) / 0.6f, 0f, 1f);
                return dot * dot * (3f - 2f * dot);
            case EnumGuiParticleShape.Puff:
                return MathF.Exp(-3f * r * r) * Math.Clamp((1f - r) / 0.3f, 0f, 1f);
            case EnumGuiParticleShape.Star:
                // A solid centre fading out by half the radius, plus two thin rays.
                float core = Math.Clamp((0.5f - r) / 0.35f, 0f, 1f);
                float horizontal = Math.Clamp(1f - MathF.Abs(v) / 0.14f, 0f, 1f) * Math.Clamp(1f - MathF.Abs(u), 0f, 1f);
                float vertical = Math.Clamp(1f - MathF.Abs(u) / 0.14f, 0f, 1f) * Math.Clamp(1f - MathF.Abs(v), 0f, 1f);
                return MathF.Max(core, MathF.Max(horizontal, vertical));
            case EnumGuiParticleShape.Drop:
                return DropAlpha(u, v);
            default:
                // Vanilla quads fade over the outer 10 % of each edge.
                float edge = MathF.Min(1f - MathF.Abs(u), 1f - MathF.Abs(v));
                return Math.Clamp(edge / 0.2f, 0f, 1f);
        }
    }
}
