using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Event.Query;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Patches.LocationPatches;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Tracks every per-upgrade level change into <c>ItemHandler._upgradeLevels</c>, and marks the
/// modifier recompute it triggers. Non-virtual, so safe to patch.
/// </summary>
[HarmonyPatch(typeof(ActorUpgradeHandler), "OnUpgradeChanged")]
internal static class UpgradeLevelTrackingPatch
{
    /// <summary>
    /// True while stat modifiers are recomputed. <see cref="UpgradeModelGetLevelPatch"/> must
    /// return the real level then, or owned upgrades get no modifiers and stop working.
    /// </summary>
    internal static bool IsApplyingModifiers => _modifierDepth > 0;

    /// <summary>A depth, not a bool: SetModifiersFromModel raises per-upgrade changes inside itself.</summary>
    private static int _modifierDepth;

    internal static void BeginModifierRecompute() => _modifierDepth++;

    internal static void EndModifierRecompute()
    {
        if (_modifierDepth > 0) _modifierDepth--;
    }

    private static void Prefix() => BeginModifierRecompute();

    private static void Postfix(UpgradeDefinition definition, int fromLevel, int toLevel)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("UpgradeLevelTrackingPatch.Postfix — first entry");
#endif
        try
        {
            if (!Plugin.Instance.ModEnabled) return;
            try { ItemHandler.TrackUpgradeLevel(definition.name, toLevel); } catch { /* half-initialised definition during scene load */ }
        }
        finally { EndModifierRecompute(); }
    }
}

/// <summary>
/// Extends the recompute window over <c>SetModifiersFromModel</c> — save restore
/// (<c>UpgradeModel.Push</c>) rebuilds modifiers there without raising per-upgrade changes.
/// CallerCount(1), safe.
/// </summary>
[HarmonyPatch(typeof(ActorUpgradeHandler), nameof(ActorUpgradeHandler.SetModifiersFromModel))]
internal static class SetModifiersFromModelPatch
{
    private static void Prefix()  => UpgradeLevelTrackingPatch.BeginModifierRecompute();
    private static void Postfix() => UpgradeLevelTrackingPatch.EndModifierRecompute();
}

/// <summary>
/// Blocks every <c>IncrementUpgradeLevel</c> in AP mode except AP grants
/// (<c>ItemHandler.IsApplyingItem</c>) — the Fabricator's vanilla grant may run deferred, so
/// an allow-list is the timing-proof guard. Save restore uses <c>Push</c>, not this.
/// </summary>
[HarmonyPatch(typeof(UpgradeModel), nameof(UpgradeModel.IncrementUpgradeLevel))]
internal static class FabricatorUpgradeBlockPatch
{
    /// <summary>
    /// Set when a craft's increment is blocked: the cost-check phase is over, so
    /// <see cref="UpgradeModelGetLevelPatch"/> may add the optimistic +1. Reset per craft.
    /// </summary>
    internal static bool WasCraftBlocked { get; set; }

    private static bool Prefix()
    {
        if (!FabricatorPatch.IsEnabled)
            return true;
        bool allow = ItemHandler.IsApplyingItem;
        if (!allow && FabricatorPatch.IsCrafting)
            WasCraftBlocked = true;
        return allow;
    }
}

/// <summary>
/// Caches the player's ActorUpgradeHandler via <c>CheckUpgradePropertiesAreAvailable</c>
/// (non-virtual, called once at construction).
/// </summary>
/// <remarks>
/// Not the constructor (the hook never fires) nor InitModel/SetModel: patching a virtual
/// slot fires for other types and crashes on the cast.
/// </remarks>
[HarmonyPatch(typeof(ActorUpgradeHandler), nameof(ActorUpgradeHandler.CheckUpgradePropertiesAreAvailable))]
internal static class ActorUpgradeHandlerCachePatch
{
    private static void Postfix(ActorUpgradeHandler __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("ActorUpgradeHandlerCachePatch.Postfix — entry");
#endif
        if (ItemHandler.UpgradeHandler == __instance) return;

        bool isReplacement = ItemHandler.UpgradeHandler != null;
        ItemHandler.UpgradeHandler = __instance;
        Logger.Info($"[AP] ActorUpgradeHandler {(isReplacement ? "re-cached (new instance after scene reload)" : "cached")} via CheckUpgradePropertiesAreAvailable Postfix");

        // A new handler (scene reload) needs its own validation pass.
        if (isReplacement && Plugin.Instance.ApClient.IsConnected)
        {
            Plugin.Instance.ApClient.ScheduleUpgradeValidation();
            Logger.Info("[AP] Scheduled upgrade re-validation for replacement ActorUpgradeHandler");
        }
    }
}

/// <summary>
/// Makes <c>UpgradeModel.GetUpgradeLevel</c> return the Fabricator checked count, so the
/// Fabricator shows the next uncrafted tier.
/// </summary>
/// <remarks>
/// A global native detour — cost and recipe getters read the level natively, past any managed
/// patch. It lies to the game, so every read that makes a real decision is exempted below. If
/// a fourth exemption is ever needed, invert this to apply only to the Fabricator UI instead.
/// </remarks>
[HarmonyPatch(typeof(UpgradeModel), nameof(UpgradeModel.GetUpgradeLevel))]
internal static class UpgradeModelGetLevelPatch
{
    private static bool Prefix(UpgradeDefinition definition, ref int __result)
    {
        // Exempt: the AP item pipeline.
        if (ItemHandler.IsApplyingItem)
            return true;

        // Exempt: modifier recompute.
        if (UpgradeLevelTrackingPatch.IsApplyingModifiers)
            return true;

        if (!FabricatorPatch.IsEnabled)
            return true;

        var upgradeName = definition?.name;
        if (string.IsNullOrEmpty(upgradeName)) return true;

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0) return true; // not an AP upgrade

        int checkedCount = crafts.Count(l => Plugin.Instance.SaveManager.IsChecked(l.Id));

        // The optimistic +1 counts the craft in flight, but not in the cost-check phase (the
        // craft hasn't happened yet). That phase must see the checked count too, never the real
        // level — an owned upgrade would read as maxed and the craft be refused.
        bool costCheckPhase = FabricatorPatch.IsCrafting
                              && !FabricatorUpgradeBlockPatch.WasCraftBlocked;
        if (!costCheckPhase && FabricatorPatch.CraftingUpgradeName == upgradeName)
            checkedCount++;

        __result = checkedCount - 1;
        return false;
    }
}

/// <summary>
/// Makes <c>PlayerUpgradeObtainedQueryComponent</c> (e.g. Gigi's Water Tank check) see the real
/// level, not <see cref="UpgradeModelGetLevelPatch"/>'s checked count.
/// </summary>
[HarmonyPatch(typeof(PlayerUpgradeObtainedQueryComponent), "IsSatisfied")]
internal static class UpgradeObtainedQueryPatch
{
    private static bool Prefix(PlayerUpgradeObtainedQueryComponent __instance, ref bool __result)
    {
        if (!FabricatorPatch.IsEnabled) return true;
        var def = __instance._upgradeDefinition;
        if (def == null || string.IsNullOrEmpty(def.name)) return true;

        // The model level too, not just the cache — the cache can miss what the save holds.
        int level = System.Math.Max(
            ItemHandler.GetRealModelLevel(def),
            ItemHandler.GetTrackedLevel(def.name));
        __result  = level >= __instance._requiredLevel;
        return false;
    }
}
