using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

/// <summary>DEF-v1.0-001 regression: the workbench input block must be
/// provably balanced. Jötunn's BlockInput is reference-counted, so one
/// logical modal lifetime may issue at most one true request and must
/// end with exactly one false request — no matter how the panel
/// transitions (adopt prompt → managed editor re-shows without hiding,
/// repeated open/close cycles, exception fail-closed paths).</summary>
public class ModalInputBlockTests
{
    [Fact]
    public void Acquire_AppliesTheBlockExactlyOnce()
    {
        var calls = new List<bool>();
        var block = new ModalInputBlock(calls.Add);

        block.Acquire();
        block.Acquire();
        block.Acquire();

        Assert.Equal(new[] { true }, calls);
        Assert.True(block.Owned);
    }

    [Fact]
    public void Release_WithoutOwnership_NeverTouchesTheBackend()
    {
        var calls = new List<bool>();
        var block = new ModalInputBlock(calls.Add);

        block.Release();
        block.Release();

        Assert.Empty(calls);
        Assert.False(block.Owned);
    }

    [Fact]
    public void AdoptTransition_ShowShownTwiceHiddenOnce_IsBalanced()
    {
        // The exact DEF-v1.0-001 shape: adopt prompt shows the panel,
        // AdoptClicked re-shows it for the managed editor, Apply hides it.
        var calls = new List<bool>();
        var block = new ModalInputBlock(calls.Add);

        block.Acquire(); // OpenAdoptPrompt → Show(true)
        block.Acquire(); // AdoptClicked → OpenForManaged → Show(true)
        block.Release(); // Apply/Close → Show(false)

        Assert.Equal(new[] { true, false }, calls);
        Assert.False(block.Owned);
    }

    [Fact]
    public void RepeatedCycles_NeverAccumulateOutstandingRequests()
    {
        var calls = new List<bool>();
        var block = new ModalInputBlock(calls.Add);

        for (int cycle = 0; cycle < 100; cycle++)
        {
            block.Acquire();
            block.Acquire(); // re-entrant open (drawer result while visible)
            block.Release();
            block.Release(); // double close (Escape + external map close)
        }

        // Strictly alternating true/false starting with true, and balanced.
        Assert.Equal(200, calls.Count);
        for (int index = 0; index < calls.Count; index++)
        {
            Assert.Equal(index % 2 == 0, calls[index]);
        }

        Assert.False(block.Owned);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("AR")]
    [InlineData("AAR")]
    [InlineData("ARAR")]
    [InlineData("RRAARRA")]
    [InlineData("AARRAARRAA")]
    public void AnyCallSequence_KeepsAtMostOneOutstandingRequest(string sequence)
    {
        int outstanding = 0;
        var block = new ModalInputBlock(blocked => outstanding += blocked ? 1 : -1);

        foreach (char operation in sequence)
        {
            if (operation == 'A')
            {
                block.Acquire();
            }
            else
            {
                block.Release();
            }

            Assert.InRange(outstanding, 0, 1);
            Assert.Equal(block.Owned ? 1 : 0, outstanding);
        }
    }

    [Fact]
    public void ThrowingBackend_StillTransitionsOwnership_SoRetriesStayBalanced()
    {
        int trueCalls = 0;
        var block = new ModalInputBlock(blocked =>
        {
            if (blocked && ++trueCalls == 1)
            {
                throw new InvalidOperationException("backend failure");
            }
        });

        Assert.Throws<InvalidOperationException>(block.Acquire);
        Assert.True(block.Owned);

        // A retry after the throw must not issue a second request.
        block.Acquire();
        Assert.Equal(1, trueCalls);

        block.Release();
        Assert.False(block.Owned);
    }
}
