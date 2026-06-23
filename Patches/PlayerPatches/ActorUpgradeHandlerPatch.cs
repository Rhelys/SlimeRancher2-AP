using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.DataModel;
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
    private static void Postfix(UpgradeDefinition definition, int fromLevel, int toLevel)
    {
#if DEBUG
        SlimeRancher2AP.Utils.DebugTrace.Once("UpgradeLevelTrackingPatch.Postfix — first entry");
#endif
        if (!Plugin.Instance.ModEnabled) return;
        try { ItemHandler.TrackUpgradeLevel(definition.name, toLevel); } catch { /* guard against partially-initialised UpgradeDefinition during scene load */ }
    }
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
            return true;              // fabricator not randomized — vanilla behaviour, allow all
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

        // Floor at the actual model level so AP-granted upgrades (received without crafting at
        // the Fabricator) are still visible to game logic such as
        // PlayerUpgradeObtainedQueryComponent.IsSatisfied (which fires for the Prismacore fight).
        // Without this, checkedCount=0 for an AP-granted Water Tank returns level=-1, causing
        // Gigi to block the final fight even though the player has the upgrade.
        int modelLevel = ItemHandler.GetTrackedLevel(upgradeName);
        __result = System.Math.Max(checkedCount - 1, modelLevel);
        return false;
    }
}
