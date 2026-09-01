using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.Event.Query;
using SlimeRancher2AP.Archipelago;
using SlimeRancher2AP.Data;
using SlimeRancher2AP.Patches.LocationPatches;

namespace SlimeRancher2AP.Patches.PlayerPatches;

/// <summary>
/// Keeps <c>ItemHandler._upgradeLevels</c> in sync by intercepting every upgrade level change,
/// including save-data restoration on game load.
/// <c>OnUpgradeChanged</c> is non-virtual (private in original C#), so it is safe to patch.
/// </summary>
[HarmonyPatch(typeof(ActorUpgradeHandler), "OnUpgradeChanged")]
internal static class UpgradeLevelTrackingPatch
{
    /// <summary>
    /// True while the handler is responding to an upgrade change — i.e. recomputing the stat
    /// modifiers for the new level.
    /// </summary>
    /// <remarks>
    /// <see cref="UpgradeModelGetLevelPatch"/> must return the REAL level during this window.
    /// The recompute reads levels back out of the model to decide which modifiers to apply; if
    /// it sees the Fabricator checked count instead (-1 for anything the player has not crafted)
    /// it applies nothing, and an upgrade the player owns silently stops working. Observed as a
    /// jetpack that stopped functioning the moment an unrelated Energy Tank was granted.
    ///
    /// Latent until the mod started writing UpgradeModel: nothing used to trigger a recompute.
    /// </remarks>
    internal static bool IsApplyingModifiers => _modifierDepth > 0;

    /// <summary>Nesting depth — SetModifiersFromModel may raise per-upgrade changes inside it.</summary>
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
            try { ItemHandler.TrackUpgradeLevel(definition.name, toLevel); } catch { /* guard against partially-initialised UpgradeDefinition during scene load */ }
        }
        finally { EndModifierRecompute(); }
    }
}

/// <summary>
/// Widens the modifier-recompute suppression window to cover
/// <c>ActorUpgradeHandler.SetModifiersFromModel</c>.
/// </summary>
/// <remarks>
/// Guarding <c>OnUpgradeChanged</c> alone was not enough. Save restore goes through
/// <c>UpgradeModel.Push</c>, which does NOT raise a per-upgrade change event — confirmed in a
/// log where UpgradeLevelTrackingPatch first fired on a later item grant, well after the model
/// had been restored. The handler instead rebuilds every modifier from the model in one pass
/// here, reading each level back through <c>GetUpgradeLevel</c>. With the Fabricator override
/// live that reads -1 for anything uncrafted, so a correctly-restored model produced no
/// modifiers at all and owned upgrades did nothing.
///
/// CallerCount(1), so it is a safe patch target.
/// </remarks>
[HarmonyPatch(typeof(ActorUpgradeHandler), nameof(ActorUpgradeHandler.SetModifiersFromModel))]
internal static class SetModifiersFromModelPatch
{
    private static void Prefix()  => UpgradeLevelTrackingPatch.BeginModifierRecompute();
    private static void Postfix() => UpgradeLevelTrackingPatch.EndModifierRecompute();
}

/// <summary>
/// Blocks <c>UpgradeModel.IncrementUpgradeLevel</c> in AP mode unless the call
/// originates from <c>ItemHandler.ApplyUpgrade</c> (the AP item pipeline).
/// </summary>
/// <remarks>
/// The Fabricator calls <c>IncrementUpgradeLevel</c> directly — possibly deferred into a
/// coroutine after <c>FabricateAndSpendCost</c> returns, so a time-limited flag on the
/// Fabricator method is not reliable. Instead we invert the guard: all increments are
/// blocked while AP mode is active <em>except</em> those explicitly wrapped by
/// <c>ItemHandler.IsApplyingItem = true</c>. This is timing-independent.
/// Save-data restoration is safe because <c>HasActiveSession</c> is false during game load.
/// <c>IncrementUpgradeLevel</c> is non-virtual, so Harmony patches it safely.
/// </summary>
[HarmonyPatch(typeof(UpgradeModel), nameof(UpgradeModel.IncrementUpgradeLevel))]
internal static class FabricatorUpgradeBlockPatch
{
    /// <summary>
    /// Set to <c>true</c> the first time <c>IncrementUpgradeLevel</c> is blocked during an
    /// active Fabricator craft.  This marks the transition from the "pre-increment cost-check
    /// phase" to the "post-increment display-refresh phase" inside <c>FabricateAndSpendCost</c>,
    /// so <see cref="UpgradeModelGetLevelPatch"/> knows it is safe to add the optimistic +1.
    /// Reset to <c>false</c> at the start of each new craft in
    /// <see cref="FabricatorPatch.Prefix"/>.
    /// </summary>
    internal static bool WasCraftBlocked { get; set; }

    private static bool Prefix()
    {
        if (!FabricatorPatch.IsEnabled)
            return true;              // no AP session — vanilla behaviour, allow all
        bool allow = ItemHandler.IsApplyingItem; // true = AP pipeline grant → allow
        if (!allow && FabricatorPatch.IsCrafting)
            WasCraftBlocked = true;   // transition: cost-check phase → display-refresh phase
        return allow;
    }
}

/// <summary>
/// Caches the player's ActorUpgradeHandler by hooking CheckUpgradePropertiesAreAvailable.
/// </summary>
/// <remarks>
/// <para>
/// Both the constructor and virtual methods (InitModel, SetModel) are unsafe patch targets on
/// IL2CPP non-MonoBehaviour types:
/// - Constructors: the Harmony fallback path does not fire for them in practice.
/// - Virtual methods: Harmony patches the vtable slot at the interface level, so the Postfix
///   fires with whatever type is at that vtable position — causing an AccessViolationException
///   when Il2CppObjectPool tries to cast the wrong pointer to ActorUpgradeHandler.
/// </para>
/// <para>
/// <c>CheckUpgradePropertiesAreAvailable()</c> is non-virtual (no vtable dispatch) and has
/// CallerCount=1 (called exactly once, from within or immediately after the constructor).
/// Patching it is safe: the trampoline is installed only in ActorUpgradeHandler's own method
/// table, so __instance is always a valid ActorUpgradeHandler pointer.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(ActorUpgradeHandler), nameof(ActorUpgradeHandler.CheckUpgradePropertiesAreAvailable))]
internal static class ActorUpgradeHandlerCachePatch
{
    private static void Postfix(ActorUpgradeHandler __instance)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.All("ActorUpgradeHandlerCachePatch.Postfix — entry");
#endif
        if (ItemHandler.UpgradeHandler == __instance) return; // same instance, nothing to do

        bool isReplacement = ItemHandler.UpgradeHandler != null;
        ItemHandler.UpgradeHandler = __instance;
        Logger.Info($"[AP] ActorUpgradeHandler {(isReplacement ? "re-cached (new instance after scene reload)" : "cached")} via CheckUpgradePropertiesAreAvailable Postfix");

        // If this is a replacement (scene reload while connected), the old validation flag
        // was already consumed for the previous handler.  Schedule a fresh validation so
        // AP-applied upgrades are re-applied against the new model.
        if (isReplacement && Plugin.Instance.ApClient.IsConnected)
        {
            Plugin.Instance.ApClient.ScheduleUpgradeValidation();
            Logger.Info("[AP] Scheduled upgrade re-validation for replacement ActorUpgradeHandler");
        }
    }
}

/// <summary>
/// Overrides <c>UpgradeModel.GetUpgradeLevel</c> in AP mode so the Fabricator's right-side
/// detail panel (crafting cost, recipe) reflects the AP checks-sent level rather than the
/// actual persisted model level.
/// </summary>
/// <remarks>
/// <para>
/// <c>PurchaseCost</c> and <c>Recipe</c> are virtual getters whose native IL2CPP
/// implementations read <c>UpgradeModel.GetUpgradeLevel</c> directly — patching
/// <c>get_CurrentUpgradeLevel</c> or <c>get_NextUpgradeLevelDefinition</c> has no effect
/// there.  A native detour on <c>GetUpgradeLevel</c> intercepts all 20 call sites,
/// including native IL2CPP code.
/// </para>
/// <para>
/// Two call sites must NOT receive the overridden level:
/// <list type="number">
///   <item><b>Fabricator cost-check phase</b> — inside <c>FabricateAndSpendCost</c>, before
///         <c>IncrementUpgradeLevel</c> is called.  The game uses this to decide which
///         tier's materials to spend.  Returning the AP-tracked level here would make it
///         demand the NEXT tier's materials and fail the craft.  Guard:
///         <c>IsCrafting &amp;&amp; !WasCraftBlocked</c> → return vanilla.</item>
///   <item><b>AP item pipeline</b> — <c>ItemHandler.ApplyUpgrade</c> reads the real model
///         level to compute <c>targetLevel</c>.  Guard: <c>IsApplyingItem</c> → return
///         vanilla.  <c>IsApplyingItem</c> is set true immediately before this read and
///         cleared immediately after.</item>
/// </list>
/// Everywhere else — including the post-craft <c>ItemCrafted</c> display refresh that fires
/// after <c>FabricateAndSpendCost</c> returns — the override is active and returns the
/// correct AP checks-sent level.
/// </para>
/// </remarks>
[HarmonyPatch(typeof(UpgradeModel), nameof(UpgradeModel.GetUpgradeLevel))]
internal static class UpgradeModelGetLevelPatch
{
    private static bool Prefix(UpgradeDefinition definition, ref int __result)
    {
        // Block during the cost-check phase of a Fabricator craft: IsCrafting=true but
        // IncrementUpgradeLevel hasn't been blocked yet, meaning the native code is still
        // computing which materials to spend.  Returning AP-tracked level here would make
        // the game try to spend the wrong tier's materials and fail the craft.
        if (FabricatorPatch.IsCrafting && !FabricatorUpgradeBlockPatch.WasCraftBlocked)
            return true;

        // Block during AP item application: ApplyUpgrade needs the real model level to
        // compute the correct targetLevel.
        if (ItemHandler.IsApplyingItem)
            return true;

        // Block while the handler is recomputing stat modifiers in response to an upgrade
        // change. That recompute reads levels back out of the model; handing it the Fabricator
        // checked count makes it apply no modifier at all, so an owned upgrade stops working.
        if (UpgradeLevelTrackingPatch.IsApplyingModifiers)
            return true;

        if (!FabricatorPatch.IsEnabled)
            return true;

        var upgradeName = definition?.name;
        if (string.IsNullOrEmpty(upgradeName)) return true;

        var crafts = LocationTable.GetFabricatorCrafts(upgradeName);
        if (crafts.Count == 0) return true; // upgrade not tracked in AP — vanilla behaviour

        int checkedCount = crafts.Count(l => Plugin.Instance.SaveManager.IsChecked(l.Id));
        // Add 1 for the craft that just happened (MarkChecked hasn't run yet in the Postfix).
        if (FabricatorPatch.CraftingUpgradeName == upgradeName)
            checkedCount++;

        // Return pure checked count, independent of the AP-granted model level.
        // PlayerUpgradeObtainedQueryComponent.IsSatisfied (Prismacore fight prereq) is patched
        // separately to use ItemHandler.GetTrackedLevel so it isn't affected by this change.
        __result = checkedCount - 1;
        return false;
    }
}

/// <summary>
/// Ensures <c>PlayerUpgradeObtainedQueryComponent.IsSatisfied</c> uses the actual AP-granted
/// model level rather than the Fabricator checked count.
/// </summary>
/// <remarks>
/// In AP mode <see cref="UpgradeModelGetLevelPatch"/> returns
/// <c>checkedCount - 1</c> from <c>UpgradeModel.GetUpgradeLevel</c>.  For an AP-granted upgrade
/// that hasn't been crafted at the Fabricator (checkedCount = 0), that returns −1, which would
/// cause prerequisite checks like the Prismacore fight to consider the upgrade absent even though
/// the player physically has it.  This patch bypasses the override for that specific query by
/// reading <c>ItemHandler.GetTrackedLevel</c> — the actual in-model level — directly.
/// </remarks>
[HarmonyPatch(typeof(PlayerUpgradeObtainedQueryComponent), "IsSatisfied")]
internal static class UpgradeObtainedQueryPatch
{
    private static bool Prefix(PlayerUpgradeObtainedQueryComponent __instance, ref bool __result)
    {
        if (!FabricatorPatch.IsEnabled) return true;
        var def = __instance._upgradeDefinition;
        if (def == null || string.IsNullOrEmpty(def.name)) return true;

        // Read the REAL model level, not just the tracked cache.
        //
        // The cache is cleared on disconnect and only repopulated from AP items inside the
        // watermark, so an upgrade the SR2 save genuinely holds can be missing from it — and
        // this query would then deny an upgrade the player is carrying. Reported in the wild as
        // "the hologram keeps saying I need a water tank even though I have one".
        //
        // GetRealModelLevel suppresses UpgradeModelGetLevelPatch for the read, which would
        // otherwise return the Fabricator checked count instead of the real level.
        int level = System.Math.Max(
            ItemHandler.GetRealModelLevel(def),
            ItemHandler.GetTrackedLevel(def.name));
        __result  = level >= __instance._requiredLevel;
        return false;
    }
}
