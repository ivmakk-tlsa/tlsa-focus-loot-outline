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

    // A placed ground trap (a "trap-infection-ground" MapTile) springs a hazard cloud when the player
    // steps close. The cloud (a PoisonExplosion prefab with an AreaOfEffect) exists only after it fires,
    // so the pre-detonation device is the persistent tile itself, matched by name because the tile
    // carries no hazard component of its own. Disc radius when the tile has no readable radius.
    public const float TrapTileRingRadius = 3f;

    public static bool IsHazardTrapTile(string name) => TrapTileLabel(name) != null;

    // The hazard label for a placed ground trap tile, or null when the name is not a hazard trap.
    public static string TrapTileLabel(string name)
    {
        if (name == null) return null;
        string n = name.ToLowerInvariant();
        if (!n.StartsWith("trap-")) return null;
        if (n.Contains("infection") || n.Contains("poison")) return "infection";
        if (n.Contains("acid")) return "acid";
        if (n.Contains("fire") || n.Contains("burn")) return "fire";
        if (n.Contains("gas")) return "gas";
        // A proximity land mine (trap-proximity-mine) explodes when the player steps near. It reads as
        // "mine" so it also gets a blast ring, the same as a placed box mine.
        if (n.Contains("mine") || n.Contains("proximity")) return "mine";
        return null;
    }

    // A gas hazard lights only while its canister has not yet gone off.
    public static bool ShouldLightGas(bool exploded) => !exploded;

    // Points on a circle in the XZ plane, counter-clockwise, first point at (radius, 0). The caller
    // builds the danger disc as a fan from a centre vertex to these rim points.
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
}
