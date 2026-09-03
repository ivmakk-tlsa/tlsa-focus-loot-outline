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
[BepInPlugin(PluginGuid, "Focus Loot Outline", "1.0.0")]
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

    // Crafting and utility stations (workbench, merchant, supply store, upgrades, shrine).
    internal static ConfigEntry<bool> IncludeStations;

    // Objectives and misc (power generator, books, XP interactions).
    internal static ConfigEntry<bool> IncludeObjectives;

    // How many containers attach their outlines per frame after focus starts. Spreads the one-time
    // attach work over several frames so the first focus press does not stutter.
    internal static ConfigEntry<int> AttachPerFrame;

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    internal static Color OutlineColor =>
        new Color(Clamp01(Red.Value), Clamp01(Green.Value), Clamp01(Blue.Value), Clamp01(Alpha.Value));

    // Every tracked container, keyed by native pointer.
    internal static readonly Dictionary<IntPtr, Tracked> Registry = new Dictionary<IntPtr, Tracked>();

    // Containers waiting to attach their outlines, drained a few per frame by the ticker.
    internal static readonly Queue<Tracked> PendingAttach = new Queue<Tracked>();

    // Shared outline category all container outlines reference. Built lazily on first use.
    internal static OutlineCategory Category;

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

        DepthTest = Config.Bind("Visibility", "DepthTest", false, "false draws the outline over walls (x-ray); true lets walls occlude it.");
        OnlyUnsearched = Config.Bind("Filter", "OnlyUnsearched", true, "Highlight only containers that are not yet searched or depleted.");
        IncludeStashes = Config.Bind("Filter", "IncludeStashes", true, "Highlight sector stashes.");
        IncludeCaches = Config.Bind("Filter", "IncludeCaches", true, "Highlight supply caches.");
        IncludeGated = Config.Bind("Filter", "IncludeGated", true, "Highlight battery/item-gated interactables (antidote dispensers, containers that need a battery).");
        IncludeToolGated = Config.Bind("Filter", "IncludeToolGated", true, "Highlight tool-gated interactables (need a tool to unlock).");
        IncludePickups = Config.Bind("Filter", "IncludePickups", true, "Highlight loose loot and pickups (ground items, survivor drops, tool rewards).");
        IncludeStations = Config.Bind("Filter", "IncludeStations", true, "Highlight crafting and utility stations (workbench, merchant, supply store, upgrades, shrine).");
        IncludeObjectives = Config.Bind("Filter", "IncludeObjectives", true, "Highlight objectives and misc (power generator, books, XP interactions).");
        AttachPerFrame = Config.Bind("Performance", "AttachPerFrame", 4, new ConfigDescription("How many containers attach outlines per frame after focus starts. Lower is smoother but takes longer to fully light.", new AcceptableValueRange<int>(1, 32)));

        new Harmony(PluginGuid).PatchAll();

        ClassInjector.RegisterTypeInIl2Cpp<Ticker>();
        var go = new GameObject("FocusLootOutlineTicker");
        go.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<Ticker>();

        Log.LogInfo("Focus Loot Outline loaded.");
    }

    internal enum Kind { Loot, Stash, Cache, Gated, ToolGated, Pickup, Station, Objective }

    // A registered container. Holds the typed reference for its state checks, the GameObject to
    // outline, and the outline renderers once attached.
    internal sealed class Tracked
    {
        public Kind Kind;
        public LootContainer Loot;
        public StashContainer Stash;
        public CacheContainer Cache;
        public GameObject GameObject;
        public List<OutlineRenderer> Outlines;
        public bool AttachTried;
        public bool Lit;
        public bool Queued;

        // A corpse drives the game's own OutlineController (skinned body included) instead of the
        // manual per-mesh outlines. When UsesController is set, Controller holds it and Outlines is
        // empty.
        public OutlineController Controller;
        public bool UsesController;
    }

    // Build or refresh the shared category from config so a live color edit is honored.
    internal static void RefreshCategory()
    {
        Color color = OutlineColor;
        if (Category == null)
        {
            Category = ScriptableObject.CreateInstance<OutlineCategory>();
            Category.m_Active = new OutlineCategory.State();
            Category.m_Inactive = new OutlineCategory.State();
            if (Verbose.Value) Log.LogDebug("[cat] created shared OutlineCategory.");
        }

        var active = Category.m_Active;
        active.m_Enabled = true;
        active.m_DepthTest = DepthTest.Value;
        active.m_Color = color;
        active.m_ColorblindColor = color;
        active.m_FresnelStrength = Strength.Value;

        var inactive = Category.m_Inactive;
        inactive.m_Enabled = false;
        inactive.m_DepthTest = DepthTest.Value;
        inactive.m_Color = color;
        inactive.m_ColorblindColor = color;
        inactive.m_FresnelStrength = 0f;
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
                return IncludeGated.Value;
            case Kind.ToolGated:
                return IncludeToolGated.Value;
            case Kind.Pickup:
                return IncludePickups.Value;
            case Kind.Station:
                return IncludeStations.Value;
            case Kind.Objective:
                return IncludeObjectives.Value;
        }
        return false;
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

        // Skip props that register as searchable but never prompt the player (a scripted light rig).
        // They are false positives, so leave them unlit.
        if (IsExcludedProp(root.name))
        {
            if (Verbose.Value) Log.LogDebug($"[skip-prop] '{go.name}' root '{root.name}' is excluded.");
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

            try
            {
                var existing = r.gameObject.GetComponent<OutlineRenderer>();
                var or = existing != null ? existing : r.gameObject.AddComponent<OutlineRenderer>();
                or.Category = Category;
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
            Log.LogDebug($"[attach] '{go.name}' kind={t.Kind}: {made} outline(s) from {renderers.Length} renderer(s) under '{root.name}'.");
    }

    // A big flat plane has one near-zero axis and one wide axis. A ground quad or a parachute sheet
    // fits this; a loot mesh (box, corpse, bench part) never does.
    private const float FlatPlaneMinThickness = 0.3f;
    private const float FlatPlaneMinSpan = 4f;

    // Prop-name fragments (case-insensitive) that mark a false-positive highlight. A mobile lighting
    // tower registers as searchable but never prompts, so it is excluded.
    private static readonly string[] ExcludedProps = { "Mobile_lighting_tower" };

    private static bool IsExcludedProp(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < ExcludedProps.Length; i++)
        {
            if (name.IndexOf(ExcludedProps[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static bool IsBigFlatPlane(Renderer r)
    {
        Vector3 s;
        try { s = r.bounds.size; }
        catch { return false; }
        float min = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
        float max = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        return min <= FlatPlaneMinThickness && max >= FlatPlaneMinSpan;
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
        string parts;
        try
        {
            var comps = go.GetComponents<Component>();
            var names = new List<string>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                // GetType() on the proxy returns "Component"; the real runtime type comes from the
                // Il2CppSystem.Object view.
                var obj = c.TryCast<Il2CppSystem.Object>();
                names.Add(obj != null ? obj.GetType().Name : c.GetType().Name);
            }
            parts = string.Join(", ", names);
        }
        catch (Exception e) { parts = $"<components unavailable: {e.Message}>"; }
        Log.LogDebug($"[unclassified] '{name}' components: {parts}.");
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
            Log.LogDebug($"[focus] ENTER: registry={Registry.Count} litNow={litNow} queued={queued}.");
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
        Plugin.Registry[__instance.Pointer] = new Plugin.Tracked { Kind = kind, GameObject = go };
        if (Plugin.Verbose.Value)
        {
            string extra = layers.Count > 1 ? $" layers=[{string.Join(", ", layers)}]" : "";
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
        return layers;
    }
}

// --- Focus toggle ------------------------------------------------------------------------------

[HarmonyPatch(typeof(FocusController), "StartFocus")]
public static class FocusStartPatch
{
    [HarmonyPostfix]
    public static void Postfix(FocusController __instance)
    {
        if (Plugin.Verbose.Value) Plugin.Log.LogDebug($"[focus] StartFocus fired (IsFocusActive={__instance.IsFocusActive}).");
        Plugin.HighlightAll(true);
    }
}

[HarmonyPatch(typeof(FocusController), "EndFocus")]
public static class FocusEndPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (Plugin.Verbose.Value) Plugin.Log.LogDebug("[focus] EndFocus fired.");
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
}
