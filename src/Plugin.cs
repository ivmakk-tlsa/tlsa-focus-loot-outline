using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Game.Actors;
using Game.Actors.Helpers;
using Game.Logic.Controllers;
using Game.Logic.Interaction;
using Game.Maps.Markup;
using Game.Props;
using Game.Rendering;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace FocusLootOutline;

// Highlights searchable containers and lootable interactables (loot containers, sector stashes,
// supply caches, gated containers, pickups, crafting/utility stations, objectives) with a colored
// outline while focus mode is active, so the player can spot them faster. The outline is the game's
// own OutlineRenderer/OutlineCategory pipeline (the same one RedAimOutline drives on zombies). Focus
// enter/exit come from FocusController.StartFocus / EndFocus.
//
// Highlighting is event-driven (container spawn, focus enter/exit); there is no per-frame work. The
// one-time outline attach after focus starts is spread a few containers per frame by the ticker, so
// the first focus press does not stutter. Set Verbose in the config to trace registration and
// attachment when diagnosing a container type that does not highlight.
//
// The mesh to outline is found by FindRenderRoot: the Interactable's ObjectRoot when it has meshes,
// else the nearest ancestor that does (for a loot box whose mesh is a sibling on a shared prop, like
// a box on a vehicle). Two meshes are then filtered out: a big flat plane (a ground quad or a
// parachute sheet that would draw as a bright square), and a few named false-positive props (a
// scripted lighting tower that registers as searchable but never prompts).
[BepInPlugin(PluginGuid, "Focus Loot Outline", "1.0.1")]
public class Plugin : BasePlugin
{
    public const string PluginGuid = "com.ivmakk.tlsa.focuslootoutline";
    internal static new ManualLogSource Log;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> Verbose;

    // Outline color channels and glow strength. Read when the shared category is (re)built, so a
    // config edit takes effect on the next focus activation without a restart.
    internal static ConfigEntry<float> Red;
    internal static ConfigEntry<float> Green;
    internal static ConfigEntry<float> Blue;
    internal static ConfigEntry<float> Alpha;
    internal static ConfigEntry<float> Strength;

    // The fuel-can outline color, its own category so a fuel can outlines apart from the shared color.
    // Default is red, to match the game's own red x-ray highlight on the explosive can.
    internal static ConfigEntry<float> FuelRed;
    internal static ConfigEntry<float> FuelGreen;
    internal static ConfigEntry<float> FuelBlue;
    internal static ConfigEntry<float> FuelAlpha;

    // When false, the outline draws over everything (x-ray), which is easiest to spot. When true,
    // walls occlude it.
    internal static ConfigEntry<bool> DepthTest;

    // Skip containers already searched/depleted.
    internal static ConfigEntry<bool> OnlyUnsearched;

    // Per-kind toggles.
    internal static ConfigEntry<bool> IncludeStashes;
    internal static ConfigEntry<bool> IncludeCaches;

    // Battery/item-gated interactables: antidote dispensers and containers that need an item (a
    // battery) to unlock.
    internal static ConfigEntry<bool> IncludeGated;

    // Tool-gated interactables (need a tool to unlock).
    internal static ConfigEntry<bool> IncludeToolGated;

    // Loose loot and pickups.
    internal static ConfigEntry<bool> IncludePickups;

    // Carryable fuel cans, outlined in the Fuel color (red by default).
    internal static ConfigEntry<bool> IncludeFuel;

    // Crafting and utility stations (workbench, merchant, supply store, upgrades, shrine).
    internal static ConfigEntry<bool> IncludeStations;

    // Objectives and misc (power generator, books, XP interactions).
    internal static ConfigEntry<bool> IncludeObjectives;

    // How many containers attach their outlines per frame after focus starts. Spreads the one-time
    // attach work over several frames so the first focus press does not stutter.
    internal static ConfigEntry<int> AttachPerFrame;

    // Dev overlay: draw each highlighted object's render-root name and kind on screen while focus is
    // active, to name a wrongly highlighted prop for a filter. Off by default.
    internal static ConfigEntry<bool> DevLabels;

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    internal static Color OutlineColor =>
        new Color(Clamp01(Red.Value), Clamp01(Green.Value), Clamp01(Blue.Value), Clamp01(Alpha.Value));

    internal static Color FuelColor =>
        new Color(Clamp01(FuelRed.Value), Clamp01(FuelGreen.Value), Clamp01(FuelBlue.Value), Clamp01(FuelAlpha.Value));

    // Every tracked container, keyed by native pointer.
    internal static readonly Dictionary<IntPtr, Tracked> Registry = new Dictionary<IntPtr, Tracked>();

    // Containers waiting to attach their outlines, drained a few per frame by the ticker.
    internal static readonly Queue<Tracked> PendingAttach = new Queue<Tracked>();

    // Shared outline category all container outlines reference. Built lazily on first use.
    internal static OutlineCategory Category;

    // The fuel-can outline category (red by default), so a fuel can outlines apart from the shared
    // color. Built alongside Category.
    internal static OutlineCategory FuelCategory;

    // True while focus mode is active, so a container registered mid-focus can be lit immediately.
    internal static bool FocusActive;

    public override void Load()
    {
        Log = base.Log;

        Enabled = Config.Bind("General", "Enabled", true, "Master switch for the focus highlight.");
        Verbose = Config.Bind("General", "Verbose", false, "Verbose diagnostic logging: container registration and outline attachment. Turn on to diagnose a container that does not highlight.");

        Red = Config.Bind("Color", "Red", 1f, new ConfigDescription("Outline red channel.", new AcceptableValueRange<float>(0f, 1f)));
        Green = Config.Bind("Color", "Green", 0.85f, new ConfigDescription("Outline green channel.", new AcceptableValueRange<float>(0f, 1f)));
        Blue = Config.Bind("Color", "Blue", 0.1f, new ConfigDescription("Outline blue channel.", new AcceptableValueRange<float>(0f, 1f)));
        Alpha = Config.Bind("Color", "Alpha", 1f, new ConfigDescription("Outline alpha.", new AcceptableValueRange<float>(0f, 1f)));
        Strength = Config.Bind("Color", "Strength", 1f, "Outline fresnel strength.");

        FuelRed = Config.Bind("Color", "FuelRed", 1f, new ConfigDescription("Fuel-can outline red channel. Default red, to match the game's own explosive-can highlight.", new AcceptableValueRange<float>(0f, 1f)));
        FuelGreen = Config.Bind("Color", "FuelGreen", 0f, new ConfigDescription("Fuel-can outline green channel.", new AcceptableValueRange<float>(0f, 1f)));
        FuelBlue = Config.Bind("Color", "FuelBlue", 0f, new ConfigDescription("Fuel-can outline blue channel.", new AcceptableValueRange<float>(0f, 1f)));
        FuelAlpha = Config.Bind("Color", "FuelAlpha", 1f, new ConfigDescription("Fuel-can outline alpha.", new AcceptableValueRange<float>(0f, 1f)));

        DepthTest = Config.Bind("Visibility", "DepthTest", false, "false draws the outline over walls (x-ray); true lets walls occlude it.");
        OnlyUnsearched = Config.Bind("Filter", "OnlyUnsearched", true, "Highlight only containers that are not yet searched or depleted.");
        IncludeStashes = Config.Bind("Filter", "IncludeStashes", true, "Highlight sector stashes.");
        IncludeCaches = Config.Bind("Filter", "IncludeCaches", true, "Highlight supply caches.");
        IncludeGated = Config.Bind("Filter", "IncludeGated", true, "Highlight battery/item-gated interactables (antidote dispensers, containers that need a battery).");
        IncludeToolGated = Config.Bind("Filter", "IncludeToolGated", true, "Highlight tool-gated interactables (need a tool to unlock).");
        IncludePickups = Config.Bind("Filter", "IncludePickups", true, "Highlight loose loot and pickups (ground items, survivor drops, tool rewards).");
        IncludeFuel = Config.Bind("Filter", "IncludeFuel", true, "Highlight carryable fuel cans, in the Fuel color (red by default, matching the game's own explosive-can highlight).");
        IncludeStations = Config.Bind("Filter", "IncludeStations", true, "Highlight crafting and utility stations (workbench, merchant, supply store, upgrades, shrine).");
        IncludeObjectives = Config.Bind("Filter", "IncludeObjectives", true, "Highlight objectives and misc (power generator, books, XP interactions).");
        AttachPerFrame = Config.Bind("Performance", "AttachPerFrame", 4, new ConfigDescription("How many containers attach outlines per frame after focus starts. Lower is smoother but takes longer to fully light.", new AcceptableValueRange<int>(1, 32)));
        DevLabels = Config.Bind("Diagnostics", "DevLabels", false, "Dev overlay: while focus is active, draw each highlighted object's render-root name and kind on screen, so a wrongly highlighted prop can be named. Local diagnostic; keep off in normal play.");

        new Harmony(PluginGuid).PatchAll();

        ClassInjector.RegisterTypeInIl2Cpp<Ticker>();
        var go = new GameObject("FocusLootOutlineTicker");
        go.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<Ticker>();

        Log.LogInfo("Focus Loot Outline loaded.");
    }

    internal enum Kind { Loot, Stash, Cache, Gated, ToolGated, Pickup, Station, Objective, Fuel }

    // A registered container. Holds the typed reference for its state checks, the GameObject to
    // outline, and the outline renderers once attached.
    internal sealed class Tracked
    {
        public Kind Kind;
        public LootContainer Loot;
        public StashContainer Stash;
        public CacheContainer Cache;
        // The use-count state source for a Gated antidote dispenser, so it can be un-highlighted once
        // spent the way Loot/Cache use their searched state. Null for other gated kinds.
        public AntiViralRefillInteraction Refill;
        public GameObject GameObject;
        public List<OutlineRenderer> Outlines;

        // The render root chosen by FindRenderRoot: its name for the dev label (this is the name a
        // filter matches on), and its transform as the label's world anchor. Set at attach time.
        public string RootName;
        public Transform Anchor;

        public bool AttachTried;
        public bool Lit;
        public bool Queued;

        // A corpse drives the game's own OutlineController (skinned body included) instead of the
        // manual per-mesh outlines. When UsesController is set, Controller holds it and Outlines is
        // empty.
        public OutlineController Controller;
        public bool UsesController;
    }

    // Build or refresh the outline categories from config so a live color edit is honored. The shared
    // category colors every highlight; the fuel category colors carryable fuel cans apart from it.
    internal static void RefreshCategory()
    {
        Category = BuildCategory(Category, OutlineColor, "shared");
        FuelCategory = BuildCategory(FuelCategory, FuelColor, "fuel");
    }

    // Create the category on first use, then set its active/inactive state from config. DepthTest and
    // Strength are shared across both categories; only the color differs.
    private static OutlineCategory BuildCategory(OutlineCategory cat, Color color, string label)
    {
        if (cat == null)
        {
            cat = ScriptableObject.CreateInstance<OutlineCategory>();
            cat.m_Active = new OutlineCategory.State();
            cat.m_Inactive = new OutlineCategory.State();
            if (Verbose.Value) Log.LogDebug($"[cat] created {label} OutlineCategory.");
        }

        var active = cat.m_Active;
        active.m_Enabled = true;
        active.m_DepthTest = DepthTest.Value;
        active.m_Color = color;
        active.m_ColorblindColor = color;
        active.m_FresnelStrength = Strength.Value;

        var inactive = cat.m_Inactive;
        inactive.m_Enabled = false;
        inactive.m_DepthTest = DepthTest.Value;
        inactive.m_Color = color;
        inactive.m_ColorblindColor = color;
        inactive.m_FresnelStrength = 0f;
        return cat;
    }

    internal static bool ShouldHighlight(Tracked t)
    {
        switch (t.Kind)
        {
            case Kind.Loot:
                if (t.Loot == null) return false;
                // Highlight while the search prompt still shows (not yet searched), even if the
                // container turns out empty. A searched container no longer prompts, so skip it.
                if (OnlyUnsearched.Value && t.Loot.IsSearched) return false;
                return true;
            case Kind.Stash:
                return IncludeStashes.Value && t.Stash != null;
            case Kind.Cache:
                if (!IncludeCaches.Value || t.Cache == null) return false;
                if (OnlyUnsearched.Value && t.Cache.SearchCount > 0) return false;
                return true;
            case Kind.Gated:
                if (!IncludeGated.Value) return false;
                // A use-count dispenser stops prompting once its uses run out. Skip it then, the same
                // as a searched loot box or a depleted cache. A battery-gated box has no reliable
                // used-up signal (see ticket 0004), so it stays highlighted while it can be interacted.
                if (OnlyUnsearched.Value && t.Refill != null && IsRefillDepleted(t.Refill)) return false;
                return true;
            case Kind.ToolGated:
                return IncludeToolGated.Value;
            case Kind.Pickup:
                return IncludePickups.Value;
            case Kind.Fuel:
                return IncludeFuel.Value;
            case Kind.Station:
                return IncludeStations.Value;
            case Kind.Objective:
                return IncludeObjectives.Value;
        }
        return false;
    }

    // An antidote dispenser tracks m_UseCount against m_MaxUses. When the count reaches the max it is
    // used up and no longer prompts, so treat it as depleted. m_MaxUses is an override value; a
    // single-use dispenser is the common case, so fall back to 1 when the override reads non-positive.
    internal static bool IsRefillDepleted(AntiViralRefillInteraction r)
    {
        try
        {
            int used = r.m_UseCount;
            int rawMax = 0;
            var mo = r.m_MaxUses;
            if (mo != null) rawMax = mo.Value;
            if (Verbose.Value) Log.LogDebug($"[gated] refill useCount={used} maxUses(raw)={rawMax} active={(mo != null && mo.Active)}.");
            return OutlineFilters.IsRefillDepleted(used, rawMax);
        }
        catch (Exception e)
        {
            if (Verbose.Value) Log.LogWarning($"[gated] refill state read failed: {e.Message}");
            return false;
        }
    }

    // Add an OutlineRenderer to each mesh under the container and wire it to the shared category.
    // Runs once per container. The renderer wiring is set defensively because the interop metadata
    // does not reveal what OutlineRenderer.Awake does.
    internal static void EnsureOutlines(Tracked t)
    {
        if (t.AttachTried) return;
        t.AttachTried = true;
        t.Outlines = new List<OutlineRenderer>();

        var go = t.GameObject;
        if (go == null)
        {
            if (Verbose.Value) Log.LogWarning("[attach] container GameObject is null.");
            return;
        }

        RefreshCategory();

        var root = FindRenderRoot(go);
        t.RootName = root.name;
        t.Anchor = root.transform;

        // Skip props that register as searchable but never prompt the player (a scripted light rig).
        // They are false positives, so leave them unlit.
        if (OutlineFilters.IsExcludedProp(root.name))
        {
            if (Verbose.Value) Log.LogDebug($"[skip-prop] '{go.name}' root '{root.name}' is excluded.");
            // Null the outline list so SetGlow hits its null-guard and Lit stays false: an excluded
            // prop draws nothing, so it must not appear as lit in the Verbose focus snapshot.
            t.Outlines = null;
            return;
        }

        // A corpse's body is skinned, and the manual OutlineRenderer below does not light skinned
        // meshes (only the static accessories glow). The dead ZombieActor keeps the game's own
        // OutlineController, which lights the whole body - the same one RedAimOutline drives on live
        // zombies. Use it when present; otherwise fall through to the manual outlines.
        if (TrySetupCorpseController(t, root, go)) return;

        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Renderer> renderers;
        try
        {
            renderers = root.GetComponentsInChildren<Renderer>(true);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[attach] GetComponentsInChildren failed on '{root.name}': {e.Message}");
            return;
        }

        int made = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;

            // Skip non-mesh renderers (particles, trails, sprites) - the outline shader expects a mesh.
            var smr = r.TryCast<SkinnedMeshRenderer>();
            var mr = r.TryCast<MeshRenderer>();
            if (smr == null && mr == null)
            {
                if (Verbose.Value) Log.LogDebug($"[attach] skip non-mesh renderer '{r.gameObject.name}' ({r.GetType().Name}).");
                continue;
            }

            // Per-renderer diagnostic (bounds, position, flatness) for tuning the mesh filters. Verbose only.
            if (Verbose.Value)
            {
                Vector3 bs, bc; try { var b = r.bounds; bs = b.size; bc = b.center; } catch { bs = Vector3.zero; bc = Vector3.zero; }
                Vector3 gp = go.transform.position;
                Log.LogDebug($"[mesh] '{go.name}' kind={t.Kind} '{r.gameObject.name}' type={(smr != null ? "skinned" : "mesh")} size=({bs.x:F2}/{bs.y:F2}/{bs.z:F2}) center=({bc.x:F1}/{bc.y:F1}/{bc.z:F1}) gpos=({gp.x:F1}/{gp.y:F1}/{gp.z:F1}) flat={IsBigFlatPlane(r)} enabled={r.enabled} active={r.gameObject.activeInHierarchy}.");
            }

            // Skip a big flat plane (a ground quad, a decal, a parachute sheet). Some containers
            // carry one, and with x-ray on it draws as a bright square that swamps the loot. A loot
            // mesh is never both this thin in one axis and this wide in another.
            if (IsBigFlatPlane(r))
            {
                if (Verbose.Value) Log.LogDebug($"[skip-flat] '{go.name}' mesh '{r.gameObject.name}'.");
                continue;
            }

            // Skip junk sub-meshes that outline as garbage: a procedural "Wire Span Mesh" cable
            // reports near-zero bounds, so the shader draws it as spikes radiating from a point
            // (seen on the HERC supply cache beacon); a "ShadowCaster" is a shadow-only proxy that
            // just duplicates the real silhouette; and decorative foliage (ivy/bush) baked into a
            // container root outlines as a jagged cluster. See ExcludedMeshes.
            if (OutlineFilters.IsExcludedMesh(r.gameObject.name))
            {
                if (Verbose.Value) Log.LogDebug($"[skip-mesh] '{go.name}' mesh '{r.gameObject.name}' is excluded.");
                continue;
            }

            try
            {
                var existing = r.gameObject.GetComponent<OutlineRenderer>();
                var or = existing != null ? existing : r.gameObject.AddComponent<OutlineRenderer>();
                or.Category = t.Kind == Kind.Fuel ? FuelCategory : Category;
                or.m_Renderer = r;
                if (smr != null)
                {
                    or.m_SkinnedMeshRenderer = smr;
                }
                else
                {
                    var mf = r.gameObject.GetComponent<MeshFilter>();
                    if (mf != null) or.m_MeshFilter = mf;
                }
                or.m_AllowRender = true;
                or.SetState(OutlineState.Inactive, true);
                try { or.UpdateRenderFunction(); } catch { }
                t.Outlines.Add(or);
                made++;
            }
            catch (Exception e)
            {
                Log.LogWarning($"[attach] AddComponent<OutlineRenderer> failed on '{r.gameObject.name}': {e.Message}");
            }
        }

        if (Verbose.Value)
            Log.LogDebug($"[attach] {Now()} '{go.name}' kind={t.Kind}: {made} outline(s) from {renderers.Length} renderer(s) under '{root.name}'.");
    }

    // Read the renderer's world bounds and hand the flat-plane test to the game-free OutlineFilters
    // (see that file and its unit tests). Returns false when the bounds cannot be read.
    private static bool IsBigFlatPlane(Renderer r)
    {
        Vector3 s;
        try { s = r.bounds.size; }
        catch { return false; }
        return OutlineFilters.IsBigFlatPlane(s.x, s.y, s.z);
    }

    // A prop's mesh count. An ancestor whose subtree holds more renderers than this is a scene
    // grouping node, not a single prop, so the ancestor walk stops before lighting a map section.
    private const int MaxAncestorRenderers = 80;

    // Pick the GameObject to gather outline meshes from. Many containers put the LootContainer on a
    // bare "Interactable" node with no mesh. The visual usually sits on the Interactable's ObjectRoot.
    // Some containers (a loot box on a vehicle) have ObjectRoot pointing back at the bare node, so the
    // mesh is a sibling under a shared prop parent instead. For those, walk up to the nearest ancestor
    // that has meshes, capped so a scene grouping node is never lit.
    private static GameObject FindRenderRoot(GameObject go)
    {
        try
        {
            var interactable = go.GetComponent<Interactable>();
            if (interactable == null) interactable = go.GetComponentInParent<Interactable>();
            if (interactable != null && interactable.ObjectRoot != null)
            {
                var objRoot = interactable.ObjectRoot.gameObject;
                int c = objRoot.GetComponentsInChildren<Renderer>(true).Length;
                if (Verbose.Value) Log.LogDebug($"[root] '{go.name}': Interactable.ObjectRoot='{objRoot.name}' renderers={c}.");
                if (c > 0) return objRoot;
            }
            else if (Verbose.Value)
            {
                Log.LogDebug($"[root] '{go.name}': interactable={interactable != null} objectRoot=null.");
            }

            // ObjectRoot held no mesh. Climb parents and take the lowest ancestor whose subtree has
            // meshes, unless that count is prop-unlike (a grouping node) - then leave it unlit.
            var parent = go.transform.parent;
            for (int depth = 0; parent != null && depth < 5; depth++, parent = parent.parent)
            {
                var pgo = parent.gameObject;
                int pc = pgo.GetComponentsInChildren<Renderer>(true).Length;
                if (pc <= 0) continue;
                if (pc > MaxAncestorRenderers)
                {
                    if (Verbose.Value) Log.LogDebug($"[root] '{go.name}': ancestor '{pgo.name}' has {pc} renderers (> {MaxAncestorRenderers}), too broad; leaving unlit.");
                    break;
                }
                if (Verbose.Value) Log.LogDebug($"[root] '{go.name}': using ancestor '{pgo.name}' (depth {depth}) renderers={pc}.");
                return pgo;
            }
        }
        catch (Exception e) { if (Verbose.Value) Log.LogWarning($"[root] lookup failed on '{go.name}': {e.Message}"); }

        return go;
    }

    // Names of unrecognized interactables already logged this session, so each logs once.
    private static readonly HashSet<string> LoggedUnclassified = new HashSet<string>();

    // Log an interactable that Classify did not place, with its component list. Once per name.
    internal static void LogUnclassified(GameObject go)
    {
        string name = go.name;
        if (!LoggedUnclassified.Add(name)) return;
        string parts = ComponentNames(go);

        // Also log the interactable's localized-name path and ObjectRoot. Some interactables (the
        // unlit campfire) carry no handler component - their action is a UnityEvent - so the name
        // path and the driven prop are the only signals to classify them by.
        string loc = "?", objRoot = "?";
        try
        {
            var it = go.GetComponent<Interactable>();
            if (it != null)
            {
                try { loc = it.m_LocalizedNamePath; } catch { }
                try { objRoot = it.ObjectRoot != null ? it.ObjectRoot.name : "<null>"; } catch { }
            }
        }
        catch { }
        Log.LogDebug($"[unclassified] '{name}' locPath='{loc}' objRoot='{objRoot}' components: {parts}.");
    }

    // Wall-clock time for a log line. BepInEx does not timestamp its disk log, so the mod stamps its
    // own diagnostic lines, making the focus snapshot (see the ENTER line in HighlightAll) correlatable
    // to an external moment.
    internal static string Now() => DateTime.Now.ToString("HH:mm:ss.fff");

    // The real il2cpp class names of a GameObject's components, comma-joined. Used to classify an
    // interactable that no known handler matched, and to trace a Gated box whose used-up state lives
    // on a varying component.
    internal static string ComponentNames(GameObject go)
    {
        try
        {
            var comps = go.GetComponents<Component>();
            var names = new List<string>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                names.Add(Il2CppTypeName(c));
            }
            return string.Join(", ", names);
        }
        catch (Exception e) { return $"<components unavailable: {e.Message}>"; }
    }

    // Real il2cpp class name of a component. The C# proxy's GetType() returns the wrapper base
    // ("Component"/"Object"), so read the native class name straight from the runtime instead.
    private static string Il2CppTypeName(Component c)
    {
        try
        {
            var klass = IL2CPP.il2cpp_object_get_class(c.Pointer);
            var namePtr = IL2CPP.il2cpp_class_get_name(klass);
            string n = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(namePtr);
            return string.IsNullOrEmpty(n) ? "?" : n;
        }
        catch { return "?"; }
    }

    // Wire a corpse to the game's own OutlineController when its dead ZombieActor still has one with
    // renderers. Returns true when the controller is used (caller skips the manual outlines).
    private static bool TrySetupCorpseController(Tracked t, GameObject root, GameObject go)
    {
        try
        {
            var za = root.GetComponent<ZombieActor>();
            if (za == null) za = root.GetComponentInChildren<ZombieActor>(true);
            if (za == null) return false;

            var view = za.View;
            var oc = view != null ? view.OutlineController : null;
            if (oc == null)
            {
                if (Verbose.Value) Log.LogDebug($"[corpse] '{go.name}' has ZombieActor but no OutlineController; using manual outlines.");
                return false;
            }

            oc.SetOutlinesEnabled(true, true);
            oc.RefreshOutlineRenderers();
            int ocr = oc.m_OutlineRenderers != null ? oc.m_OutlineRenderers.Count : 0;
            if (ocr <= 0)
            {
                if (Verbose.Value) Log.LogDebug($"[corpse] '{go.name}' controller has 0 renderers; using manual outlines.");
                return false;
            }

            // Point the corpse controller at the mod's shared category so the body glows in the
            // mod color, matching the containers. The controller is this dead actor's own, so this
            // does not recolor live zombies (each has its own controller). Without this the corpse
            // keeps the game's default outline color (the red aim/zombie outline).
            try { oc.SetOutlinesCategory(Category); }
            catch (Exception e) { if (Verbose.Value) Log.LogWarning($"[corpse] SetOutlinesCategory failed on '{go.name}': {e.Message}"); }

            oc.SetOutlinesActive(false);
            t.Controller = oc;
            t.UsesController = true;
            if (Verbose.Value) Log.LogDebug($"[corpse] '{go.name}' uses game OutlineController with {ocr} renderers.");
            return true;
        }
        catch (Exception e)
        {
            if (Verbose.Value) Log.LogWarning($"[corpse] controller setup failed on '{go.name}': {e.Message}");
            return false;
        }
    }

    internal static void SetGlow(Tracked t, bool on)
    {
        if (t.Lit == on) return;

        if (t.UsesController)
        {
            if (t.Controller != null)
            {
                try { t.Controller.SetOutlinesActive(on); }
                catch (Exception e) { if (Verbose.Value) Log.LogWarning($"[glow] controller SetOutlinesActive failed: {e.Message}"); }
            }
            t.Lit = on;
            return;
        }

        if (t.Outlines == null) return;
        var state = on ? OutlineState.Active : OutlineState.Inactive;
        for (int i = 0; i < t.Outlines.Count; i++)
        {
            var or = t.Outlines[i];
            if (or == null) continue;
            try { or.SetState(state, true); }
            catch (Exception e) { if (Verbose.Value) Log.LogWarning($"[glow] SetState failed: {e.Message}"); }
        }
        t.Lit = on;
    }

    internal static void HighlightAll(bool on)
    {
        FocusActive = on;
        if (!Enabled.Value) return;

        if (!on)
        {
            // Focus ended: cancel pending attach work and turn off any lit outline.
            while (PendingAttach.Count > 0) PendingAttach.Dequeue().Queued = false;
            foreach (var pair in Registry)
            {
                if (pair.Value.Lit) SetGlow(pair.Value, false);
            }
            if (Verbose.Value) Log.LogDebug($"[focus] EXIT: registry={Registry.Count}.");
            return;
        }

        // Light already-attached containers now (cheap); queue the rest for the ticker to attach a
        // few per frame, so the first focus press does not stutter.
        RefreshCategory();
        int litNow = 0, queued = 0;
        var dead = new List<IntPtr>();
        foreach (var pair in Registry)
        {
            var t = pair.Value;
            if (t.GameObject == null) { dead.Add(pair.Key); continue; }
            if (ShouldHighlight(t))
            {
                if (t.AttachTried) { SetGlow(t, true); litNow++; }
                else if (!t.Queued) { t.Queued = true; PendingAttach.Enqueue(t); queued++; }
            }
            else if (t.Lit)
            {
                SetGlow(t, false);
            }
        }
        for (int i = 0; i < dead.Count; i++) Registry.Remove(dead[i]);

        if (Verbose.Value)
        {
            // A timestamped snapshot of what is lit right now, so this line names every highlighted
            // object at a known moment. Queued (not-yet-attached) objects have no name yet and appear
            // on the next press.
            var lit = new List<string>();
            foreach (var pair in Registry)
            {
                var tt = pair.Value;
                if (tt.Lit && tt.RootName != null) lit.Add($"{tt.RootName}[{tt.Kind}]");
            }
            Log.LogDebug($"[focus] {Now()} ENTER registry={Registry.Count} litNow={litNow} queued={queued} lit=[{string.Join(", ", lit)}].");
        }
    }

    // Attach outlines for a few queued containers per frame, then light them. Called by the ticker.
    internal static void DrainAttachQueue()
    {
        if (!FocusActive || PendingAttach.Count == 0) return;

        int budget = AttachPerFrame.Value;
        if (budget < 1) budget = 1;
        for (int i = 0; i < budget && PendingAttach.Count > 0; i++)
        {
            var t = PendingAttach.Dequeue();
            t.Queued = false;
            if (t.GameObject == null) continue;
            EnsureOutlines(t);
            if (FocusActive && ShouldHighlight(t)) SetGlow(t, true);
        }
    }

    // Dev overlay: label every lit object with its render-root name and kind, so a wrongly
    // highlighted prop can be named for a filter. Drawn from the ticker's OnGUI, gated on DevLabels.
    internal static void DrawDevLabels()
    {
        if (!FocusActive || DevLabels == null || !DevLabels.Value) return;

        var cam = Camera.main;
        if (cam == null)
        {
            var all = Camera.allCameras;
            if (all != null && all.Length > 0) cam = all[0];
        }
        if (cam == null) return;

        var prev = GUI.color;
        GUI.color = Color.yellow;
        foreach (var pair in Registry)
        {
            var t = pair.Value;
            if (t == null || !t.Lit || t.Anchor == null) continue;
            // Label only what actually draws an outline. An excluded prop is marked lit but has no
            // outlines, so skipping the empty ones keeps a label from floating on an un-outlined prop.
            bool hasOutline = t.UsesController || (t.Outlines != null && t.Outlines.Count > 0);
            if (!hasOutline) continue;
            Vector3 sp;
            try { sp = cam.WorldToScreenPoint(t.Anchor.position); }
            catch { continue; }
            if (sp.z <= 0f) continue; // behind the camera
            GUI.Label(new Rect(sp.x - 4f, Screen.height - sp.y, 460f, 22f), $"{t.RootName} [{t.Kind}]");
        }
        GUI.color = prev;
    }
}

// --- Container registration --------------------------------------------------------------------

[HarmonyPatch(typeof(LootContainer), "Awake")]
public static class LootAwakePatch
{
    [HarmonyPostfix]
    public static void Postfix(LootContainer __instance)
    {
        var t = new Plugin.Tracked { Kind = Plugin.Kind.Loot, Loot = __instance, GameObject = __instance.gameObject };
        Plugin.Registry[__instance.Pointer] = t;
        if (Plugin.Verbose.Value)
        {
            var p = __instance.transform.position;
            Plugin.Log.LogDebug($"[reg] LootContainer '{__instance.gameObject.name}' at ({p.x:F1},{p.y:F1},{p.z:F1}) searched={__instance.IsSearched} depleted={__instance.IsDepleted}.");
        }
        if (Plugin.FocusActive) Plugin.HighlightAll(true);
    }
}

[HarmonyPatch(typeof(StashContainer), "Awake")]
public static class StashAwakePatch
{
    [HarmonyPostfix]
    public static void Postfix(StashContainer __instance)
    {
        Plugin.Registry[__instance.Pointer] = new Plugin.Tracked { Kind = Plugin.Kind.Stash, Stash = __instance, GameObject = __instance.gameObject };
        if (Plugin.Verbose.Value)
            Plugin.Log.LogDebug($"[reg] StashContainer '{__instance.gameObject.name}'.");
        if (Plugin.FocusActive) Plugin.HighlightAll(true);
    }
}

[HarmonyPatch(typeof(CacheContainer), "Awake")]
public static class CacheAwakePatch
{
    [HarmonyPostfix]
    public static void Postfix(CacheContainer __instance)
    {
        Plugin.Registry[__instance.Pointer] = new Plugin.Tracked { Kind = Plugin.Kind.Cache, Cache = __instance, GameObject = __instance.gameObject };
        if (Plugin.Verbose.Value)
            Plugin.Log.LogDebug($"[reg] CacheContainer '{__instance.gameObject.name}' searchCount={__instance.SearchCount}.");
        if (Plugin.FocusActive) Plugin.HighlightAll(true);
    }
}

[HarmonyPatch(typeof(LootContainer), "OnDestroy")]
public static class LootDestroyPatch
{
    [HarmonyPostfix]
    public static void Postfix(LootContainer __instance) => Plugin.Registry.Remove(__instance.Pointer);
}

// Catch interactables that have no lifecycle hook of their own (gated containers, pickups, crafting
// stations, objectives). Interactable.Awake is the common entry for every interactable, so classify
// here by the handler components on the GameObject. Containers that carry their own
// LootContainer/Stash/Cache are already tracked, so skip those.
[HarmonyPatch(typeof(Interactable), "Awake")]
public static class InteractableAwakePatch
{
    [HarmonyPostfix]
    public static void Postfix(Interactable __instance)
    {
        var go = __instance.gameObject;
        if (go.GetComponent<LootContainer>() != null || go.GetComponent<StashContainer>() != null || go.GetComponent<CacheContainer>() != null)
            return;

        var layers = ClassifyLayers(go);
        if (layers.Count == 0)
        {
            // An interactable we do not recognize is otherwise invisible. Log it once per name with
            // its components, so a lootable that never highlights leaves past data to classify it by.
            if (Plugin.Verbose.Value) Plugin.LogUnclassified(go);
            return;
        }

        // First match is the tracked group. The rest are logged so a multi-layer item is visible.
        var kind = layers[0];
        var t = new Plugin.Tracked { Kind = kind, GameObject = go };
        // Capture a use-count dispenser's state source so it can gate the highlight (see
        // ShouldHighlight). Absent for other gated kinds.
        if (kind == Plugin.Kind.Gated)
            t.Refill = go.GetComponent<AntiViralRefillInteraction>();
        Plugin.Registry[__instance.Pointer] = t;
        if (Plugin.Verbose.Value)
        {
            string extra = layers.Count > 1 ? $" layers=[{string.Join(", ", layers)}]" : "";
            // A Gated box's used-up state lives on a component that varies by dispenser type. Log the
            // component list and interaction-enabled flag so a variant that still stays lit is classifiable.
            if (kind == Plugin.Kind.Gated)
            {
                bool enabled = true; try { enabled = __instance.IsInteractionEnabled; } catch { }
                extra += $" enabled={enabled} comps=[{Plugin.ComponentNames(go)}]";
            }
            Plugin.Log.LogDebug($"[reg] {kind} interactable '{go.name}'.{extra}");
        }
        if (Plugin.FocusActive) Plugin.HighlightAll(true);
    }

    // List the highlight groups an interactable matches, in priority order. The caller tracks it as
    // the first; the full list is logged so a multi-layer item shows up in telemetry.
    private static List<Plugin.Kind> ClassifyLayers(GameObject go)
    {
        var layers = new List<Plugin.Kind>();
        if (go.GetComponent<AntiViralRefillInteraction>() != null
            || go.GetComponent<ItemRequirementInteraction>() != null
            || go.GetComponent<AntiviralDispenser>() != null)
            layers.Add(Plugin.Kind.Gated);
        if (go.GetComponent<ToolRequirementInteraction>() != null)
            layers.Add(Plugin.Kind.ToolGated);
        // A carryable fuel can (CarryInteraction) is its own kind, outlined red to match the game's
        // own red x-ray highlight on the explosive can. Checked before Pickup so a can routes to the
        // Fuel (red) category, not the shared Pickup color. This assumes CarryInteraction marks the
        // fuel can; if another carryable prop shares the component, it would also outline red.
        if (go.GetComponent<CarryInteraction>() != null)
            layers.Add(Plugin.Kind.Fuel);
        if (go.GetComponent<PickupItem>() != null
            || go.GetComponent<SurvivorDrop>() != null
            || go.GetComponent<ToolRewardInteraction>() != null)
            layers.Add(Plugin.Kind.Pickup);
        if (go.GetComponent<CraftingInteraction>() != null
            || go.GetComponent<MerchantInteraction>() != null
            || go.GetComponent<SupplyStoreInteraction>() != null
            || go.GetComponent<UpgradesDialogInteractable>() != null
            || go.GetComponent<ShrineInteraction>() != null)
            layers.Add(Plugin.Kind.Station);
        if (go.GetComponent<PowerGenerator>() != null
            || go.GetComponent<BookInteraction>() != null
            || go.GetComponent<AwardXpInteraction>() != null)
            layers.Add(Plugin.Kind.Objective);
        // An unlit campfire has no interaction handler component (its "light" action is a UnityEvent),
        // so no component above matches it. Classify it by the fire-barrel prop it drives. Once lit it
        // gains a CraftingInteraction and is a Station on its own; matching the prop keeps it a Station
        // while unlit too. Kept last so a real handler component always wins first.
        if (layers.Count == 0 && IsFireInteractable(go))
            layers.Add(Plugin.Kind.Station);
        return layers;
    }

    // True when this interactable drives a fire barrel / campfire prop. Matched on the prop name
    // (the GameObject or its ObjectRoot), which is fire-specific, so a lamp or light switch is not
    // caught. "deco-fire-interactable-barrel" and "fire-firebarrel" are the fire prop names seen.
    private static bool IsFireInteractable(GameObject go)
    {
        if (OutlineFilters.NameMarksFire(go.name)) return true;
        try
        {
            var it = go.GetComponent<Interactable>();
            var root = it != null ? it.ObjectRoot : null;
            if (root != null && OutlineFilters.NameMarksFire(root.name)) return true;
        }
        catch { }
        return false;
    }
}

// When the antidote is dispensed, the wall box's use count advances. If that empties it, drop its
// glow at once instead of waiting for the next focus press, so a used dispenser stops standing out
// the moment it is taken. ShouldHighlight keeps it dark on later focus presses.
[HarmonyPatch(typeof(AntiViralRefillInteraction), "OnInteractionCompleted")]
public static class RefillCompletedPatch
{
    [HarmonyPostfix]
    public static void Postfix(AntiViralRefillInteraction __instance)
    {
        try
        {
            if (!Plugin.OnlyUnsearched.Value) return;
            var interactable = __instance.GetComponent<Interactable>();
            if (interactable == null) return;
            if (Plugin.Registry.TryGetValue(interactable.Pointer, out var t)
                && t.Lit && Plugin.IsRefillDepleted(__instance))
            {
                Plugin.SetGlow(t, false);
                if (Plugin.Verbose.Value) Plugin.Log.LogDebug($"[gated] '{t.GameObject?.name}' depleted; glow off.");
            }
        }
        catch (Exception e)
        {
            if (Plugin.Verbose.Value) Plugin.Log.LogWarning($"[gated] completion hook failed: {e.Message}");
        }
    }
}

// --- Focus toggle ------------------------------------------------------------------------------

[HarmonyPatch(typeof(FocusController), "StartFocus")]
public static class FocusStartPatch
{
    [HarmonyPostfix]
    public static void Postfix(FocusController __instance)
    {
        if (Plugin.Verbose.Value) Plugin.Log.LogDebug($"[focus] {Plugin.Now()} StartFocus fired (IsFocusActive={__instance.IsFocusActive}).");
        Plugin.HighlightAll(true);
    }
}

[HarmonyPatch(typeof(FocusController), "EndFocus")]
public static class FocusEndPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (Plugin.Verbose.Value) Plugin.Log.LogDebug($"[focus] {Plugin.Now()} EndFocus fired.");
        Plugin.HighlightAll(false);
    }
}

// --- Attach ticker -----------------------------------------------------------------------------

// Drains the pending-attach queue a few containers per frame, so the one-time outline attach after
// focus starts does not land in a single frame. Idle (early return) when nothing is queued.
public class Ticker : MonoBehaviour
{
    public Ticker(IntPtr ptr) : base(ptr) { }

    public void LateUpdate() => Plugin.DrainAttachQueue();

    public void OnGUI()
    {
        try { Plugin.DrawDevLabels(); }
        catch (Exception e) { if (Plugin.Verbose.Value) Plugin.Log.LogWarning($"[devlabels] {e.Message}"); }
    }
}
