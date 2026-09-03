using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>DEF-v1.0-006: the vector road layer must place every baked
/// vertex exactly where vanilla's MapPointToLocalGuiPos formula puts the
/// same map point, for any pan/zoom window, without per-vertex rebakes.</summary>
public class RoadVectorMathTests
{
    // Representative uv windows: full map, deep zoom, off-center pan, and
    // aspect-corrected windows where the x and y scales genuinely differ.
    public static TheoryData<float, float, float, float, float, float> UvWindows => new()
    {
        { 0f, 0f, 1f, 1f, 2048f, 2048f },
        { 0.45f, 0.47f, 0.05f, 0.05f, 2048f, 2048f },
        { 0.3f, 0.4f, 0.2f, 0.1125f, 1920f, 1080f },
        { 0.7f, 0.1f, 0.25f, 0.14f, 3840f, 2160f },
        { 0.05f, 0.85f, 0.0421f, 0.0261f, 1280f, 800f },
    };

    [Theory]
    [MemberData(nameof(UvWindows))]
    public void ContentTransform_ReproducesVanillaProjection_ForEveryMapPoint(
        float uvMinX, float uvMinY, float uvWidth, float uvHeight, float rectWidth, float rectHeight)
    {
        (float scaleX, float scaleY, float positionX, float positionY) = RoadVectorMath.ContentTransform(
            uvMinX, uvMinY, uvWidth, uvHeight, rectWidth, rectHeight);

        for (float mapX = 0f; mapX <= 1f; mapX += 0.125f)
        {
            for (float mapY = 0f; mapY <= 1f; mapY += 0.125f)
            {
                (float bakedX, float bakedY) = RoadVectorMath.Bake(mapX, mapY);
                float viaTransformX = positionX + (scaleX * bakedX);
                float viaTransformY = positionY + (scaleY * bakedY);

                (float vanillaX, float vanillaY) = RoadVectorMath.VanillaLocalGuiPosition(
                    mapX, mapY, uvMinX, uvMinY, uvWidth, uvHeight, rectWidth, rectHeight);

                // Float algebra over rect-scale magnitudes; the acceptance
                // bound for the whole feature is 2 screen px, so 0.01 GUI
                // units of numerical slack is far inside it.
                Assert.True(MathF.Abs(viaTransformX - vanillaX) < 0.01f,
                    $"x mismatch at m=({mapX}, {mapY}): {viaTransformX} vs {vanillaX}");
                Assert.True(MathF.Abs(viaTransformY - vanillaY) < 0.01f,
                    $"y mismatch at m=({mapX}, {mapY}): {viaTransformY} vs {vanillaY}");
            }
        }
    }

    [Fact]
    public void Bake_MapCenter_IsOrigin()
    {
        (float x, float y) = RoadVectorMath.Bake(0.5f, 0.5f);

        Assert.Equal(0f, x);
        Assert.Equal(0f, y);
    }

    [Fact]
    public void BakedHalfWidth_ScalesBackToRequestedScreenWidth()
    {
        const float screenPixels = 3f;
        const float uvWidth = 0.0421f;
        const float rectWidth = 1920f;

        float halfWidth = RoadVectorMath.BakedHalfWidth(screenPixels, uvWidth, rectWidth);
        float scaleX = rectWidth / (RoadVectorMath.ReferenceSize * uvWidth);
        float onScreen = halfWidth * 2f * scaleX;

        Assert.True(MathF.Abs(onScreen - screenPixels) < 0.001f, $"width came back as {onScreen}");
    }

    [Fact]
    public void BakedHalfWidth_DegenerateRect_FallsBackToHalfPixels()
    {
        Assert.Equal(1.5f, RoadVectorMath.BakedHalfWidth(3f, 0.1f, 0f));
    }

    [Fact]
    public void DeepZoom_SeparatesPointsHalfATexelApart()
    {
        // Two points half a 2048-texture texel apart: the texture overlay
        // renders them into the same texel; the vector layer must not.
        const float uvWidth = 0.05f;
        const float rectWidth = 2048f;
        const float mapX = 0.5f;
        const float halfTexel = 0.5f / 2048f;

        (float scaleX, _, float positionX, _) = RoadVectorMath.ContentTransform(
            0.475f, 0.475f, uvWidth, uvWidth, rectWidth, rectWidth);
        (float bakedA, _) = RoadVectorMath.Bake(mapX, 0.5f);
        (float bakedB, _) = RoadVectorMath.Bake(mapX + halfTexel, 0.5f);

        float guiA = positionX + (scaleX * bakedA);
        float guiB = positionX + (scaleX * bakedB);

        Assert.True(guiB - guiA > 5f,
            $"half a texel should span multiple GUI units at deep zoom, got {guiB - guiA}");
    }
}
