using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

/// <summary>RC14 fix 2: the Atlas drawer's dragged position round-trips
/// as a durable preference, and a restored offset can never strand the
/// panel off-screen — not after a resolution change, not after a UI-scale
/// change, not from a hand-edited config value.</summary>
public class PanelPositionRuleTests
{
    [Fact]
    public void SerializedPosition_RoundTrips()
    {
        string stored = PanelPositionRule.Serialize(-412.5f, 137.25f);
        Assert.True(PanelPositionRule.TryParse(stored, out float x, out float y));
        Assert.Equal(-412.5f, x);
        Assert.Equal(137.25f, y);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-comma")]
    [InlineData("1,2,3")]
    [InlineData("abc,def")]
    [InlineData("NaN,0")]
    [InlineData("0,Infinity")]
    public void MalformedOrNonFiniteInput_ReadsAsNothingStored(string? stored)
    {
        // "Nothing stored" is the default-dock contract: a bad value must
        // degrade to the spawn position, never to a broken restore.
        Assert.False(PanelPositionRule.TryParse(stored, out _, out _));
    }

    [Fact]
    public void PositionInsideTheCanvas_RestoresUnchanged()
    {
        (float x, float y) = PanelPositionRule.Clamp(
            x: -500f, y: 50f,
            panelWidth: 380f, panelHeight: 700f, uiScale: 1f,
            canvasWidth: 1920f, canvasHeight: 1080f);
        Assert.Equal(-500f, x);
        Assert.Equal(50f, y);
    }

    [Fact]
    public void OffScreenCoordinate_ClampsFullyOnScreen()
    {
        // The relog scenario the owner hit generalized: a coordinate saved
        // against a larger canvas (or dragged half off) must come back
        // with the WHOLE panel inside the current canvas.
        (float x, float y) = PanelPositionRule.Clamp(
            x: -5000f, y: 4000f,
            panelWidth: 380f, panelHeight: 700f, uiScale: 1f,
            canvasWidth: 1920f, canvasHeight: 1080f);

        // Anchor (1, 0.5): panel center in canvas space.
        float centerX = 1920f + x;
        float centerY = (1080f / 2f) + y;
        Assert.InRange(centerX - 190f, 0f, 1920f);
        Assert.InRange(centerX + 190f, 0f, 1920f);
        Assert.InRange(centerY - 350f, 0f, 1080f);
        Assert.InRange(centerY + 350f, 0f, 1080f);
    }

    [Fact]
    public void UiScaleGrowsThePanel_ClampAccountsForTheScaledSize()
    {
        // At UiScale 1.6 the panel is 608 wide; an x that fit a 1.0-scale
        // panel right at the left edge must be pushed further right.
        (float xAtOne, _) = PanelPositionRule.Clamp(
            x: -1730f, y: 0f, panelWidth: 380f, panelHeight: 700f, uiScale: 1f,
            canvasWidth: 1920f, canvasHeight: 1080f);
        (float xAtBig, _) = PanelPositionRule.Clamp(
            x: -1730f, y: 0f, panelWidth: 380f, panelHeight: 700f, uiScale: 1.6f,
            canvasWidth: 1920f, canvasHeight: 1080f);
        Assert.Equal(-1730f, xAtOne);
        Assert.True(xAtBig > xAtOne, "the scaled panel must sit further inside the canvas");
        Assert.InRange(1920f + xAtBig - (380f * 1.6f / 2f), 0f, 1920f);
    }

    [Fact]
    public void PanelTallerThanTheCanvas_CentersInsteadOfOscillating()
    {
        // 700 × 1.6 = 1120 > 1080: no y keeps the whole panel inside, so
        // the axis centers (min > max would otherwise invert the clamp).
        (_, float y) = PanelPositionRule.Clamp(
            x: -400f, y: 900f, panelWidth: 380f, panelHeight: 700f, uiScale: 1.6f,
            canvasWidth: 1920f, canvasHeight: 1080f);
        Assert.Equal(0f, y);
    }
}

/// <summary>RC14 fix 4: a cached Jötunn overlay handle is only trusted
/// while its texture is alive. Presence alone was exactly the relog bug —
/// Jötunn destroys every overlay texture on Minimap teardown, so the
/// second session painted persisted roads into a dead texture and the
/// minimap stayed blank.</summary>
public class OverlayHandleRuleTests
{
    [Fact]
    public void NoCachedHandle_MustResolve()
    {
        Assert.True(OverlayHandleRule.MustReresolve(hasCachedHandle: false, cachedTextureAlive: false));
    }

    [Fact]
    public void CachedHandleWithLiveTexture_IsUsed()
    {
        Assert.False(OverlayHandleRule.MustReresolve(hasCachedHandle: true, cachedTextureAlive: true));
    }

    [Fact]
    public void RelogRegression_CachedHandleWithDeadTexture_MustReresolve()
    {
        // Session 1 cached the handle; Minimap teardown destroyed its
        // texture; session 2 must re-resolve instead of painting into the
        // corpse (the "roads gone from the minimap after relog" report).
        Assert.True(OverlayHandleRule.MustReresolve(hasCachedHandle: true, cachedTextureAlive: false));
    }
}

/// <summary>RC14 fix 3: armed Quick Pin owns its input. The capture click
/// must not attack, Escape must cancel without also opening the pause
/// menu on the same press, and every suppression ends the moment the
/// interaction ends — regardless of whether vanilla's update ran before
/// or after the mod's in the owned frame.</summary>
public class QuickPinInputGateTests
{
    [Fact]
    public void Arming_OwnsTheArmingFrame()
    {
        var gate = new QuickPinInputGate();
        Assert.False(gate.SuppressAttack(100));

        // The toolbar click that arms closes the map in the same frame;
        // vanilla must not read that click as an attack.
        gate.Arm(100);
        Assert.True(gate.Armed);
        Assert.True(gate.SuppressAttack(100));
        Assert.True(gate.SuppressMenu(100));
    }

    [Fact]
    public void EscapeCancels_AndTheSamePressNeverOpensTheMenu()
    {
        var gate = new QuickPinInputGate();
        gate.Arm(100);

        QuickPinInputGate.FrameAction action = gate.HandleFrame(140, cancelPressed: true, capturePressed: false);
        Assert.Equal(QuickPinInputGate.FrameAction.Cancel, action);
        Assert.False(gate.Armed);

        // Same frame: whether vanilla's Menu.Update runs before or after
        // the mod handled the cancel, the press stays swallowed.
        Assert.True(gate.SuppressMenu(140));

        // Next frame: normal input is back immediately.
        Assert.False(gate.SuppressMenu(141));
        Assert.False(gate.SuppressAttack(141));
    }

    [Fact]
    public void CaptureClick_NeverAttacks_AndReleasesTheNextFrame()
    {
        var gate = new QuickPinInputGate();
        gate.Arm(100);

        QuickPinInputGate.FrameAction action = gate.HandleFrame(160, cancelPressed: false, capturePressed: true);
        Assert.Equal(QuickPinInputGate.FrameAction.Capture, action);
        Assert.False(gate.Armed);
        Assert.True(gate.SuppressAttack(160));
        Assert.False(gate.SuppressAttack(161));
    }

    [Fact]
    public void WholeArmedLifetime_IsSuppressed()
    {
        var gate = new QuickPinInputGate();
        gate.Arm(100);
        for (int frame = 100; frame <= 130; frame++)
        {
            Assert.Equal(QuickPinInputGate.FrameAction.None, gate.HandleFrame(frame, false, false));
            Assert.True(gate.SuppressAttack(frame));
            Assert.True(gate.SuppressMenu(frame));
        }
    }

    [Fact]
    public void CancelWinsOverCapture_WhenBothArriveTogether()
    {
        // Escape is the player changing their mind; a same-frame click
        // must not still create a pin.
        var gate = new QuickPinInputGate();
        gate.Arm(100);
        Assert.Equal(
            QuickPinInputGate.FrameAction.Cancel,
            gate.HandleFrame(120, cancelPressed: true, capturePressed: true));
    }

    [Fact]
    public void CaptureIsOneShot()
    {
        var gate = new QuickPinInputGate();
        gate.Arm(100);
        gate.HandleFrame(120, cancelPressed: false, capturePressed: true);
        Assert.Equal(
            QuickPinInputGate.FrameAction.None,
            gate.HandleFrame(121, cancelPressed: false, capturePressed: true));
    }

    [Fact]
    public void UnarmedGate_NeverSuppressesAnything()
    {
        var gate = new QuickPinInputGate();
        Assert.Equal(QuickPinInputGate.FrameAction.None, gate.HandleFrame(50, true, true));
        Assert.False(gate.SuppressAttack(50));
        Assert.False(gate.SuppressMenu(50));
    }

    [Fact]
    public void ExternalDisarm_ReleasesImmediately_WithNoSameFrameTail()
    {
        // World switch / mod disable / dispose is not an owned press:
        // gameplay input must be back the same frame.
        var gate = new QuickPinInputGate();
        gate.Arm(100);
        gate.HandleFrame(110, false, false);
        gate.Disarm();
        Assert.False(gate.Armed);
        Assert.False(gate.SuppressAttack(110));
        Assert.False(gate.SuppressMenu(110));
    }
}

/// <summary>RC14 fix 1: the sprite-rebind decision behind "custom markers
/// become Dots after relog". A restart-claimed cc:* rendering (wanted
/// sprite, none recorded) must rebuild to regain its art; a genuine
/// vanilla pin must never be repainted; a sprite Unity destroyed across a
/// scene change counts as not applied.</summary>
public class SpriteRebindRuleTests
{
    [Fact]
    public void RelogRegression_ClaimedCcRendering_RebuildsToRegainItsSprite()
    {
        // After save → logout → reload, the reconcile claims the saved
        // vanilla rendering; the applied-sprite record is empty while the
        // stored pin still wants its cc:* art.
        Assert.True(SpriteRebindRule.MustRebuild(
            wantedIconId: "cc:road", appliedIconId: null, appliedSpriteAlive: true));
    }

    [Fact]
    public void VanillaPin_IsNeverRepainted()
    {
        // The owner's explicit constraint: vanilla Dot pins remain
        // vanilla — no wanted sprite and no applied sprite means no
        // rebuild, ever.
        Assert.False(SpriteRebindRule.MustRebuild(
            wantedIconId: null, appliedIconId: null, appliedSpriteAlive: false));
        Assert.False(SpriteRebindRule.MustRebuild(
            wantedIconId: null, appliedIconId: null, appliedSpriteAlive: true));
    }

    [Fact]
    public void MatchingLiveSprite_IsKept()
    {
        Assert.False(SpriteRebindRule.MustRebuild(
            wantedIconId: "cc:harbor", appliedIconId: "cc:harbor", appliedSpriteAlive: true));
    }

    [Fact]
    public void MatchingButDestroyedSprite_Rebuilds()
    {
        // The record says applied, but Unity destroyed the sprite across
        // a scene change — bookkeeping alone is not proof of pixels.
        Assert.True(SpriteRebindRule.MustRebuild(
            wantedIconId: "cc:harbor", appliedIconId: "cc:harbor", appliedSpriteAlive: false));
    }

    [Fact]
    public void IconChangeBetweenCcIds_SharingOneVanillaFallback_Rebuilds()
    {
        // Two cc:* ids can share a vanilla fallback type, so the type
        // comparison can't see this change — the rule must.
        Assert.True(SpriteRebindRule.MustRebuild(
            wantedIconId: "cc:fishing", appliedIconId: "cc:road", appliedSpriteAlive: true));
    }

    [Fact]
    public void RevertingToAVanillaIcon_RemovesTheCcSprite()
    {
        Assert.True(SpriteRebindRule.MustRebuild(
            wantedIconId: null, appliedIconId: "cc:road", appliedSpriteAlive: true));
    }
}
