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

    // Prop-name fragments (case-insensitive) that mark a false-positive highlight. A mobile lighting
    // tower registers as searchable but never prompts, so it is excluded.
    private static readonly string[] ExcludedProps = { "Mobile_lighting_tower" };

    // Specific industrial-trash variants seen only as unreachable camp decor (loose garbage piles),
    // not the family. The same "Deco-Industrial-Trash" family also has real lootable dumpsters (e.g.
    // -10), so each decor variant is matched whole: a following digit means a different variant, and
    // "-1"/"-2" must never match "-10"/"-20". Add a variant here only after confirming it is decor-only.
    private static readonly string[] ExcludedTrashVariants = { "Deco-Industrial-Trash-1", "Deco-Industrial-Trash-2" };

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

    // A scripted-light rig, and a confirmed decor-only trash variant, are never outlined.
    public static bool IsExcludedProp(string name)
        => ContainsAny(name, ExcludedProps) || IsExcludedTrashVariant(name);

    // True when the name is one of the decor-only trash variants, matched whole: a digit right after
    // the variant token means a different variant (so "-2" does not catch "-10"/"-20"), while a
    // "(Clone)" or " (1)" suffix still matches.
    private static bool IsExcludedTrashVariant(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < ExcludedTrashVariants.Length; i++)
        {
            var v = ExcludedTrashVariants[i];
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
