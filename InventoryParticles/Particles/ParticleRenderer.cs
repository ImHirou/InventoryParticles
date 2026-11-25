using System;
using InventoryParticles.Api;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace InventoryParticles.Particles;

/// <summary> Draws the particles of one layer with the GUI shader that is bound during the ortho render stage. </summary>
internal sealed class ParticleRenderer
{
    private const float Depth = 50f;
    private const float MinDrawnSize = 0.3f;
    /// <summary> Size of a hanging particle when it appears, relative to its full size. </summary>
    private const float HangStartScale = 1f / 3f;
    private const float MaxStretch = 4f;

    private readonly Vec4f color = new();

    public void Render(IRenderAPI render, ParticleSystem system, ParticleTextures textures, EnumParticleLayer layer)
    {
        if (system.CountIn(layer) == 0)
        {
            return;
        }

        // Particles go on top of whatever was drawn before, whatever depth it used.
        render.GLDisableDepthTest();

        try
        {
            DrawPass(render, system, textures, layer, glow: false);
            DrawPass(render, system, textures, layer, glow: true);
        }
        finally
        {
            render.GLEnableDepthTest();
            render.GlToggleBlend(true, EnumBlendMode.Standard);
        }
    }

    private void DrawPass(IRenderAPI render, ParticleSystem system, ParticleTextures textures, EnumParticleLayer layer, bool glow)
    {
        bool blendSet = false;
        double time = system.Time;

        foreach (ref readonly var p in system.Particles)
        {
            if (p.Layer != layer || p.Glow != glow || !TryGetVisual(in p, time, out float x, out float y, out float width, out float height))
            {
                continue;
            }

            if (!blendSet)
            {
                render.GlToggleBlend(true, glow ? EnumBlendMode.Glow : EnumBlendMode.Standard);
                blendSet = true;
            }

            int texture = textures.GetTextureId(p.Texture, Math.Max(width, height));
            render.Render2DTexture(texture, x - width * 0.5f, y - height * 0.5f, width, height, Depth, color);
        }
    }

    /// <summary> Centre, size and color of a particle this frame. False if it is invisible. </summary>
    private bool TryGetVisual(in GuiParticle p, double time, out float x, out float y, out float width, out float height)
    {
        var props = p.Props;
        var owner = p.Owner;
        float t = Math.Clamp(p.Age / p.Life, 0f, 1f);
        x = y = 0f;

        float size = p.Size0;

        if (props.SizeEvolve != EvolvingNatFloat.NoValueSet)
        {
            size *= Math.Max(0f, props.SizeEvolve.nextFloat(1f, t));
        }

        bool hanging = p.Age < p.Hang;

        if (hanging)
        {
            size *= HangStartScale + (1f - HangStartScale) * (p.Age / p.Hang);
        }

        width = height = size;

        if (p.Stretch > 0f && !hanging)
        {
            height *= Math.Min(MaxStretch, 1f + Math.Abs(p.VY) * p.Stretch);
        }

        if (size < MinDrawnSize)
        {
            return false;
        }

        float alpha = props.OpacityEvolve != EvolvingNatFloat.NoValueSet ? props.OpacityEvolve.nextFloat(p.A0, t) : p.A0;

        if (props.FadeIn > 0f)
        {
            alpha *= Math.Min(1f, p.Age / props.FadeIn);
        }

        if (props.FadeOut > 0f)
        {
            alpha *= Math.Min(1f, (p.Life - p.Age) / props.FadeOut);
        }

        if (!double.IsNaN(p.FadeStart))
        {
            alpha *= 1f - (float)((time - p.FadeStart) / ParticleSystem.FastFadeSeconds);
        }

        if (alpha < 1f)
        {
            return false;
        }

        float red = props.RedEvolve != EvolvingNatFloat.NoValueSet ? props.RedEvolve.nextFloat(p.R0, t) : p.R0;
        float green = props.GreenEvolve != EvolvingNatFloat.NoValueSet ? props.GreenEvolve.nextFloat(p.G0, t) : p.G0;
        float blue = props.BlueEvolve != EvolvingNatFloat.NoValueSet ? props.BlueEvolve.nextFloat(p.B0, t) : p.B0;

        x = p.X;
        y = p.Y;

        if (p.Relative)
        {
            x += owner.AnchorX;
            y += owner.AnchorY;
        }

        if (p.WobbleAmp != 0f)
        {
            x += p.WobbleAmp * MathF.Sin(MathF.Tau * p.WobbleFreq * p.Age + p.WobblePhase);
        }

        color.X = Math.Clamp(red / 255f, 0f, 1f);
        color.Y = Math.Clamp(green / 255f, 0f, 1f);
        color.Z = Math.Clamp(blue / 255f, 0f, 1f);
        color.W = Math.Clamp(alpha / 255f, 0f, 1f);
        return true;
    }
}
