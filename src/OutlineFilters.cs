using System;

namespace FocusLootOutline;

// The mesh/prop/name filters and the small state math, with no game types. Everything here works on
// plain names and numbers, so it runs in a unit test with no running game. The game adapter
// (Plugin.cs) reads a Renderer's bounds and an interactable's names/state into these calls. Keep this
// file free of BepInEx, Il2Cpp and UnityEngine references; the test project links it directly.
internal static class OutlineFilters
{
    // A big flat plane has one near-zero axis and one wide axis. A ground quad or a parachute sheet
    // fits this; a loot mesh (box, corpse, bench part) never does.
    public const float FlatPlaneMinThickness = 0.3f;
    public const float FlatPlaneMinSpan = 4f;

    // A zero-bounds mesh (a merged/degenerate renderer) has no real silhouette and draws as garbage.
    public const float FlatDegenerateMax = 0.02f;

    // A ground decal/quad is paper-thin AND wide. The width bound is what tells it from a small flat
    // tool (a wrench, a plate, a lid), which is thin but only a few dozen cm across, so a tool keeps
    // its outline while a survivor-drop ground quad (about 3.3 m) is dropped.
    public const float FlatDecalThickness = 0.05f;
    public const float FlatDecalSpan = 1.5f;

    // The industrial-trash prop the game reuses under one name both as reachable loot and as camp decor
    // at a military tent ("Deco-Industrial-Trash-1"/"-2"). The name alone cannot tell the two apart, so
    // the game adapter skips a match only when a tent stands next to it. Each name is matched whole: a
    // digit right after the token means a different variant ("-1" must never catch "-10"), while a
    // "(Clone)" or " (2)" instance suffix still matches. (The lighting tower's decor copy needs no name
    // rule: its search collider is disabled, which the game adapter catches for every prop.)
    private static readonly string[] TentDecorProps = { "Deco-Industrial-Trash-1", "Deco-Industrial-Trash-2" };

    // The tent decor's collider objects are named "Tent1", "Tent2", ... plus an instance suffix.
    private const string TentPrefix = "Tent";

    // Props that stay highlighted even when their tracked interaction collider is disabled. The player's
    // mission vehicle keeps its objective collider off between mission steps, but the vehicle itself is
    // always interactable (refuel, stash, leave), so it must not go dark.
    private static readonly string[] KeepWhenUndetectable = { "PlayerMissionVehicle" };

    // Decorative foliage (ivy, bushes) baked into a container's prefab root - a survivor drop or a
    // wall dispenser sits in a bush - outlines as a jagged spiky cluster. It is matched by name
    // PREFIX, because harvestable plant loot has "Plant_"-prefixed meshes ("Plant_Bush_B",
    // "Plant_FlowersRedBush") that would be caught by a bare "Bush" fragment. Every decorative asset
    // starts with "Bush" or "Ivy"; no real plant does.
    private static readonly string[] ExcludedMeshPrefixes = { "Bush", "Ivy" };

    // Mesh-name fragments (case-insensitive) whose renderer must never be outlined. A "Wire Span Mesh"
    // cable reports near-zero bounds and draws as spikes; a "ShadowCaster" duplicates the silhouette;
    // "VegStudio" is a catch-all for other decorative vegetation-studio assets.
    private static readonly string[] ExcludedMeshes = { "Wire Span Mesh", "ShadowCaster", "VegStudio" };

    // Fire-barrel / campfire prop-name fragments. The unlit fire has no interaction handler component
    // (its "light" action is a UnityEvent), so it is classified by the prop it drives. These are
    // fire-specific, so a lamp or a light switch is not caught.
    private static readonly string[] FireMarkers = { "firebarrel", "fire-interactable", "Fire-Camp" };

    // True when the name is the trash prop the game reuses as both reachable loot and tent decor. The
    // game adapter then skips this copy only when a tent stands next to it; the name alone decides
    // nothing. Matched whole: a digit right after the token means a different variant (so "-1" does
    // not catch "-10"), while a "(Clone)" or " (2)" suffix still matches.
    public static bool IsTentDecorProp(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < TentDecorProps.Length; i++)
        {
            var v = TentDecorProps[i];
            // Walk every occurrence of the token, not only the first. A first hit that is
            // followed by a digit is a different variant ("-10"), but a later occurrence may
            // still be a valid whole match, so keep scanning past a digit-suffixed hit.
            int from = 0;
            while (true)
            {
                int idx = name.IndexOf(v, from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                int after = idx + v.Length;
                if (after >= name.Length || !char.IsDigit(name[after])) return true;
                from = idx + 1;
            }
        }
        return false;
    }

    // True when a collider object's name marks a tent ("Tent1(Clone)"). A tent-decor trash prop with
    // one of these next to it is the unreachable copy.
    public static bool IsTentMarker(string name)
        => !string.IsNullOrEmpty(name) && name.StartsWith(TentPrefix, StringComparison.OrdinalIgnoreCase);

    // True when the render-root name is a prop the undetectable-collider rule must leave alone.
    public static bool KeepsHighlightWhenUndetectable(string name) => ContainsAny(name, KeepWhenUndetectable);

    // Decorative foliage (prefix match) and named junk sub-meshes (fragment match) are never outlined.
    public static bool IsExcludedMesh(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < ExcludedMeshPrefixes.Length; i++)
            if (name.StartsWith(ExcludedMeshPrefixes[i], StringComparison.OrdinalIgnoreCase)) return true;
        return ContainsAny(name, ExcludedMeshes);
    }

    // True when a prop name marks a fire barrel / campfire, so an unlit fire is classified by it.
    public static bool NameMarksFire(string name) => ContainsAny(name, FireMarkers);

    // The mesh bounds size on each axis. True for a big flat plane (a ground quad, a decal, or a
    // parachute sheet) that would draw as a bright square with x-ray on. A loot mesh never fits.
    public static bool IsBigFlatPlane(float sizeX, float sizeY, float sizeZ)
    {
        float min = Min3(sizeX, sizeY, sizeZ);
        float max = Max3(sizeX, sizeY, sizeZ);
        if (max <= FlatDegenerateMax) return true;                          // zero-bounds mesh
        if (min <= FlatDecalThickness && max >= FlatDecalSpan) return true; // ground decal/quad
        return min <= FlatPlaneMinThickness && max >= FlatPlaneMinSpan;     // thin, wide sheet
    }

    // An antidote dispenser is used up when its use count reaches its max uses. A single-use dispenser
    // is the common case, so fall back to 1 when the raw max reads non-positive (override inactive).
    public static bool IsRefillDepleted(int useCount, int maxUses)
    {
        int max = maxUses > 0 ? maxUses : 1;
        return useCount >= max;
    }

    private static bool ContainsAny(string name, string[] fragments)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < fragments.Length; i++)
            if (name.IndexOf(fragments[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static float Min3(float a, float b, float c) => Math.Min(a, Math.Min(b, c));
    private static float Max3(float a, float b, float c) => Math.Max(a, Math.Max(b, c));
}
