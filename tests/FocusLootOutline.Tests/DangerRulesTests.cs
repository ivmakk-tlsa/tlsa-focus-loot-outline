using System;
using FocusLootOutline;
using Xunit;

namespace FocusLootOutline.Tests;

// Tests for the game-free hazard-marker rules: the lit/dark decisions for ground AoEs, traps and
// gas, the effect-name label map, and the ring geometry the adapter builds the mine-blast mesh from.
// Whether a Harmony patch resolves or the ring actually renders in-game stays an in-game check.
public class DangerRulesTests
{
    // --- ShouldLightArea --------------------------------------------------------------------------

    [Fact]
    public void ShouldLightArea_dark_when_inactive()
        => Assert.False(DangerRules.ShouldLightArea(false, false, 5f, true));

    [Fact]
    public void ShouldLightArea_dark_when_single_frame()
        => Assert.False(DangerRules.ShouldLightArea(true, true, 5f, true));

    [Theory]
    [InlineData(0.5f)]  // exactly the threshold: still a burst
    [InlineData(0.1f)]
    public void ShouldLightArea_dark_when_duration_at_or_below_minimum(float duration)
        => Assert.False(DangerRules.ShouldLightArea(true, false, duration, true));

    // A zero duration is "never expires" (the map's permanent fire spots), not a zero-length burst.
    [Fact]
    public void ShouldLightArea_lit_when_duration_is_zero_meaning_permanent()
        => Assert.True(DangerRules.ShouldLightArea(true, false, 0f, true));

    [Fact]
    public void ShouldLightArea_dark_when_it_cannot_hit_the_player()
        => Assert.False(DangerRules.ShouldLightArea(true, false, 5f, false));

    [Fact]
    public void ShouldLightArea_lit_when_active_lasting_and_hits_player()
        => Assert.True(DangerRules.ShouldLightArea(true, false, 5f, true));

    // --- EffectLabel ------------------------------------------------------------------------------

    [Theory]
    [InlineData("Acid", "acid")]
    [InlineData("Infection", "infection")]
    [InlineData("Burning", "fire")]
    [InlineData("BurningSmall", "fire")]
    [InlineData("RadialDamage", "blast")]
    [InlineData("SomethingElse", "hazard")]
    [InlineData("", "hazard")]
    [InlineData(null, "hazard")]
    [InlineData("acid", "hazard")]  // case-sensitive: lowercase does not match "Acid"
    public void EffectLabel_maps_known_names_and_falls_back_to_hazard(string effectName, string expected)
        => Assert.Equal(expected, DangerRules.EffectLabel(effectName));

    // --- ShouldLightTrap / ShouldLightGas ----------------------------------------------------------

    [Theory]
    [InlineData(false, true)]  // armed
    [InlineData(true, false)]  // already tripped
    public void ShouldLightTrap_lights_only_while_armed(bool tripped, bool expected)
        => Assert.Equal(expected, DangerRules.ShouldLightTrap(tripped));

    [Theory]
    [InlineData(false, true)]  // not yet gone off
    [InlineData(true, false)]  // exploded
    public void ShouldLightGas_lights_only_before_it_explodes(bool exploded, bool expected)
        => Assert.Equal(expected, DangerRules.ShouldLightGas(exploded));

    // --- RingRadius ---------------------------------------------------------------------------------

    [Fact]
    public void RingRadius_floors_a_small_collider_to_the_minimum()
        => Assert.Equal(0.75f, DangerRules.RingRadius(0.25f, 0.75f));

    [Fact]
    public void RingRadius_keeps_a_large_collider_radius()
        => Assert.Equal(3f, DangerRules.RingRadius(3f, 0.75f));

    // --- RingPoints ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(3f, 48)]
    [InlineData(2.85f, 16)]
    [InlineData(1f, 3)]
    public void RingPoints_returns_segment_count_points_on_the_circle(float radius, int segments)
    {
        var points = DangerRules.RingPoints(radius, segments);
        Assert.Equal(segments, points.Length);

        Assert.Equal(radius, points[0].x, 4);
        Assert.Equal(0f, points[0].z, 4);

        for (int i = 0; i < points.Length; i++)
        {
            double dist = Math.Sqrt(points[i].x * points[i].x + points[i].z * points[i].z);
            Assert.Equal(radius, (float)dist, 4);
        }
    }

    [Fact]
    public void RingPoints_points_are_distinct()
    {
        var points = DangerRules.RingPoints(3f, DangerRules.RingSegments);
        for (int i = 0; i < points.Length; i++)
            for (int j = i + 1; j < points.Length; j++)
                Assert.False(
                    Math.Abs(points[i].x - points[j].x) < 1e-4 && Math.Abs(points[i].z - points[j].z) < 1e-4,
                    $"points {i} and {j} coincide");
    }

    // --- RingTriangles --------------------------------------------------------------------------------

    [Theory]
    [InlineData(48)]
    [InlineData(16)]
    [InlineData(3)]
    public void RingTriangles_returns_two_triangles_per_segment_within_the_vertex_range(int segments)
    {
        var indices = DangerRules.RingTriangles(segments);
        Assert.Equal(6 * segments, indices.Length);
        foreach (var idx in indices)
        {
            Assert.InRange(idx, 0, 2 * segments - 1);
        }
    }

    [Fact]
    public void RingTriangles_closing_quad_references_index_0_and_segments()
    {
        int segments = DangerRules.RingSegments;
        var indices = DangerRules.RingTriangles(segments);
        // The last quad (segment index `segments - 1`) joins back to the first outer/inner vertices.
        var lastQuad = new int[6];
        Array.Copy(indices, 6 * (segments - 1), lastQuad, 0, 6);
        Assert.Contains(0, lastQuad);
        Assert.Contains(segments, lastQuad);
    }

    [Fact]
    public void RingTriangles_all_triangles_share_the_same_winding_sign()
    {
        int segments = DangerRules.RingSegments;
        var outer = DangerRules.RingPoints(1f, segments);
        var inner = DangerRules.RingPoints(0.85f, segments);
        var vertices = new (float x, float z)[2 * segments];
        Array.Copy(outer, vertices, segments);
        Array.Copy(inner, 0, vertices, segments, segments);

        var indices = DangerRules.RingTriangles(segments);
        int? sign = null;
        for (int t = 0; t < indices.Length / 3; t++)
        {
            var a = vertices[indices[t * 3 + 0]];
            var b = vertices[indices[t * 3 + 1]];
            var c = vertices[indices[t * 3 + 2]];

            // Twice the signed area of triangle a-b-c in the XZ plane.
            float signedArea = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
            Assert.NotEqual(0f, signedArea);
            int triSign = Math.Sign(signedArea);

            if (sign == null) sign = triSign;
            else Assert.Equal(sign.Value, triSign);
        }
    }
}
