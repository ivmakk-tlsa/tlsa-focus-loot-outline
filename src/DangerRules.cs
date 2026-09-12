using System;

namespace FocusLootOutline;

// The lit/dark decisions and ring geometry for hazard markers (ground AoE puddles, mines, traps,
// gas), with no game types. The game adapter reads a hazard's state and the blast radius into these
// calls and builds the ring mesh from the returned points/triangles. Keep this file free of BepInEx,
// Il2Cpp and UnityEngine references; the test project links it directly.
internal static class DangerRules
{
    // An AreaOfEffect that lives this long or less is a burst (a one-shot explosion visual), not a
    // lingering puddle, so it never gets a ground highlight.
    public const float MinLastingDuration = 0.5f;

    // Metres, band width of the mine blast ring: wide enough to read at a glance without covering
    // the ground inside the blast.
    public const float RingWidth = 0.15f;

    // Used when the blast radius cannot be read from the game data (a missing/zeroed field).
    public const float MineRingFallbackRadius = 3f;

    public const int RingSegments = 48;

    // Default smallest ring drawn, metres. A fire spot's AreaOfEffect collider is only 0.25 m, far
    // smaller than its flames, so a ring at that radius would sit inside the fire and read as nothing.
    public const float DefaultMinRingRadius = 0.75f;

    // The ring radius for a hazard with no mesh of its own: its collider radius, floored so a small
    // collider still draws a ring that frames the visible hazard.
    public static float RingRadius(float hazardRadius, float minRadius) => Math.Max(hazardRadius, minRadius);

    // A lingering ground hazard lights only while it is live, lasting, and can hit the player. A
    // single-frame effect or a durable-but-harmless zone (already resolved, or never targets the
    // player) is not worth the highlight. A duration of zero means no expiry (the map's permanent
    // fire spots read 0 in-game), so it is lasting; only a short positive duration is a burst.
    public static bool ShouldLightArea(bool active, bool singleFrame, float duration, bool hitsPlayer)
        => active && !singleFrame && (duration <= 0f || duration > MinLastingDuration) && hitsPlayer;

    // Plain label for the dev overlay / log from the status-effect model name the adapter resolved.
    public static string EffectLabel(string effectName)
    {
        switch (effectName)
        {
            case "Acid": return "acid";
            case "Infection": return "infection";
            case "Burning": return "fire";
            case "BurningSmall": return "fire";
            case "RadialDamage": return "blast";
            default: return "hazard";
        }
    }

    // A trap lights only while it is still armed; a tripped trap has already fired.
    public static bool ShouldLightTrap(bool tripped) => !tripped;

    // A gas hazard lights only while its canister has not yet gone off.
    public static bool ShouldLightGas(bool exploded) => !exploded;

    // Points on a circle in the XZ plane, counter-clockwise, first point at (radius, 0). The caller
    // builds a ring mesh from two such rings (outer and inner radius).
    public static (float x, float z)[] RingPoints(float radius, int segments)
    {
        var points = new (float x, float z)[segments];
        for (int i = 0; i < segments; i++)
        {
            double angle = 2.0 * Math.PI * i / segments;
            points[i] = ((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
        }
        return points;
    }

    // Triangle indices for a flat band, closed into a loop. Vertex layout the caller builds:
    // RingPoints(radius, segments) followed by RingPoints(radius - RingWidth, segments), so outer i
    // is index i and inner i is index segments + i. Wound clockwise as seen from +Y so the face
    // points up in Unity's left-handed convention.
    public static int[] RingTriangles(int segments)
    {
        var indices = new int[6 * segments];
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int outerI = i;
            int outerNext = next;
            int innerI = segments + i;
            int innerNext = segments + next;

            int b = i * 6;
            indices[b + 0] = outerI;
            indices[b + 1] = outerNext;
            indices[b + 2] = innerI;

            indices[b + 3] = outerNext;
            indices[b + 4] = innerNext;
            indices[b + 5] = innerI;
        }
        return indices;
    }
}
