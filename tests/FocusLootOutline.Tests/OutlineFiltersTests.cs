using FocusLootOutline;
using Xunit;

namespace FocusLootOutline.Tests;

// Tests for the game-free outline filters. They cover the name matching (which decides what is junk,
// what is decorative foliage, and what is a fire) and the two bits of state math (the flat-plane test
// and the dispenser depletion). Whether a Harmony patch resolves, the outline attaches, or the game
// data reads correctly stays an in-game check.
public class OutlineFiltersTests
{
    // --- IsExcludedProp ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Mobile_lighting_tower", true)]
    [InlineData("Mobile_lighting_tower (2)", true)]     // numbered scene copy
    [InlineData("mobile_lighting_tower", true)]         // case-insensitive
    [InlineData("Deco-Industrial-Trash-1", true)]        // camp decor pile, exact variant
    [InlineData("Deco-Industrial-Trash-1(Clone)", true)] // instanced copy still matches
    [InlineData("Deco-Industrial-Trash-2", true)]        // camp decor pile, exact variant
    [InlineData("Deco-Industrial-Trash-2(Clone)", true)]
    // Other variants of the same family are real lootable dumpsters and MUST stay. The digit boundary
    // keeps "-1"/"-2" from catching "-10"/"-20".
    [InlineData("Deco-Industrial-Trash-10", false)]
    [InlineData("Deco-Industrial-Trash-20", false)]
    // Real deco loot stashes/caches must stay: they share the "deco-" prefix but are lootable.
    [InlineData("deco-ammo-stash-1(Clone)", false)]
    [InlineData("deco-cache-medical(Clone)", false)]
    [InlineData("Search", false)]
    [InlineData("CacheContainer", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsExcludedProp_matches_decor_false_positives_only(string name, bool expected)
        => Assert.Equal(expected, OutlineFilters.IsExcludedProp(name));

    // --- IsExcludedMesh ---------------------------------------------------------------------------

    [Theory]
    // Decorative foliage: prefix match on "Bush"/"Ivy".
    [InlineData("Bush-Con-Green-1_LOD0", true)]
    [InlineData("Ivy2_something", true)]
    // Named junk sub-meshes: fragment match anywhere.
    [InlineData("Wire Span Mesh", true)]
    [InlineData("ShadowCaster", true)]
    [InlineData("some_VegStudio_grass", true)]
    // Harvestable plants must stay outlined: "Plant_"-prefixed, so a bare "Bush" fragment must NOT
    // catch them. This is the regression guard behind the prefix (not fragment) match for foliage.
    [InlineData("Plant_Bush_B", false)]
    [InlineData("Plant_FlowersRedBush", false)]
    // A real fire mesh is not junk.
    [InlineData("fire-firebarrel_0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsExcludedMesh_skips_foliage_and_junk_but_keeps_plants(string name, bool expected)
        => Assert.Equal(expected, OutlineFilters.IsExcludedMesh(name));

    // --- NameMarksFire ----------------------------------------------------------------------------

    [Theory]
    [InlineData("deco-fire-interactable-barrel(Clone)", true)] // the unlit fire's ObjectRoot prop
    [InlineData("fire-firebarrel_0", true)]
    [InlineData("CraftingInteractable-Fire-Camp", true)]       // the lit fire's node
    // A lamp / light switch and the light rig must NOT be treated as a fire.
    [InlineData("Interactable-Light", false)]
    [InlineData("Mobile_lighting_tower", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NameMarksFire_matches_fire_props_only(string name, bool expected)
        => Assert.Equal(expected, OutlineFilters.NameMarksFire(name));

    // --- IsBigFlatPlane ---------------------------------------------------------------------------

    [Fact]
    public void IsBigFlatPlane_true_for_zero_bounds_mesh()
        => Assert.True(OutlineFilters.IsBigFlatPlane(0.01f, 0.0f, 0.01f));

    [Fact]
    public void IsBigFlatPlane_true_for_ground_decal_thin_and_wide()
        => Assert.True(OutlineFilters.IsBigFlatPlane(0.03f, 3.33f, 3.33f)); // survivor-drop quad

    [Fact]
    public void IsBigFlatPlane_true_for_parachute_sheet()
        => Assert.True(OutlineFilters.IsBigFlatPlane(0.2f, 5f, 5f));

    [Theory]
    [InlineData(0.68f, 0.91f, 0.68f)] // fire barrel body (a box)
    [InlineData(0.62f, 0.03f, 0.61f)] // fire top: thin but only ~0.6 m wide, not a decal
    [InlineData(0.02f, 0.4f, 0.2f)]   // a thin tool (wrench/plate): kept, width is small
    public void IsBigFlatPlane_false_for_real_meshes(float x, float y, float z)
        => Assert.False(OutlineFilters.IsBigFlatPlane(x, y, z));

    // --- IsRefillDepleted -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1, false)]  // fresh single-use dispenser
    [InlineData(1, 1, true)]   // single-use, taken
    [InlineData(1, 0, true)]   // override inactive (raw 0) -> fall back to single-use, taken
    [InlineData(0, 0, false)]  // override inactive, not yet taken
    [InlineData(2, 3, false)]  // multi-use, uses left
    [InlineData(3, 3, true)]   // multi-use, exhausted
    [InlineData(5, 2, true)]   // over max
    public void IsRefillDepleted_compares_use_count_to_max(int used, int max, bool expected)
        => Assert.Equal(expected, OutlineFilters.IsRefillDepleted(used, max));
}
