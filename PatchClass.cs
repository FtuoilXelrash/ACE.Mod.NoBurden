
namespace NoBurden;

[HarmonyPatch]
public class PatchClass(BasicMod mod, string settingsName = "Settings.json") : BasicPatch<Settings>(mod, settingsName)
{
    /// <summary>
    /// Thread-local storage for capturing player level before UpdateXpAndLevel executes.
    /// This allows Prefix and Postfix to communicate without a persistent cache.
    /// </summary>
    [ThreadStatic]
    private static long? CapturedLevelBeforeUpdate = null;

    public override Task OnStartSuccess()
    {
        // Assign Settings from SettingsContainer to make it available throughout the class
        Settings = SettingsContainer.Settings;

        ModManager.Log($"NoBurden started successfully!");
        ModManager.Log($"Burden disabled for characters below level {Settings.IgnoreBurdenBelowCharacterLevel}");

        return Task.CompletedTask;
    }

    public override void Stop()
    {
        base.Stop();
        ModManager.Log($"NoBurden stopped!");
    }

    // ===== PATCH 1: Override GetEncumbranceCapacity =====
    /// <summary>
    /// Patch Player.GetEncumbranceCapacity() to return unlimited capacity for low-level players.
    /// This is the main mechanism that prevents burden from applying.
    /// Formula normally: (150 * strength) + (AugmentationIncreasedCarryingCapacity * 30 * strength)
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.GetEncumbranceCapacity))]
    [HarmonyPostfix]
    public static void Patch_GetEncumbranceCapacity(Player __instance, ref int __result)
    {
        var threshold = PatchClass.Settings.IgnoreBurdenBelowCharacterLevel;

        if (__instance.Level < threshold)
        {
            __result = 10000000;  // Return effectively unlimited capacity
        }
    }

    // ===== PATCH 2: Override EncumbranceVal Setter =====
    /// <summary>
    /// Patch WorldObject.EncumbranceVal setter to zero out burden for low-level players.
    /// This ensures the encumbrance value itself (item weight burden) is set to 0.
    /// </summary>
    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.EncumbranceVal), MethodType.Setter)]
    [HarmonyPrefix]
    public static void Patch_EncumbranceVal_Setter(WorldObject __instance, ref int? value)
    {
        if (value.HasValue && __instance is Player player)
        {
            var threshold = PatchClass.Settings.IgnoreBurdenBelowCharacterLevel;

            if (player.Level < threshold)
            {
                value = 0;  // Zero out burden for low-level players
            }
        }
    }

    // ===== PATCH 3: Detect Level-Up and Send Warning =====
    /// <summary>
    /// Patch Player.PlayerEnterWorld() to initialize level cache and detect level changes.
    /// We use PlayerEnterWorld as a hook to track when players log in.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.PlayerEnterWorld))]
    [HarmonyPostfix]
    public static void Patch_PlayerEnterWorld(Player __instance)
    {
        var threshold = PatchClass.Settings.IgnoreBurdenBelowCharacterLevel;
        var currentLevel = __instance.Level ?? 1;

        // Check if player already crossed threshold before this login
        if (PlayerLevelCache.ContainsKey(__instance.Guid))
        {
            var cachedLevel = PlayerLevelCache[__instance.Guid];
            if (cachedLevel < threshold && currentLevel >= threshold)
            {
                __instance.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                    $"WARNING: Your level has reached {currentLevel} you now suffer from the effects of burden!\n" +
                    $"(This effect may not be applied until the next time you log in.)",
                    ChatMessageType.CombatEnemy
                ));
            }
            // Remove from cache if now at or above threshold (no longer need to track them)
            if (currentLevel >= threshold)
                PlayerLevelCache.Remove(__instance.Guid);
        }
        else if (currentLevel < threshold)
        {
            // Only cache players below threshold
            PlayerLevelCache[__instance.Guid] = currentLevel;
        }
    }

    // ===== PATCH 4: Level Property Setter Detection =====
    // ===== PATCH 4: UpdateXpAndLevel Postfix - Detect Level-Up =====
    /// <summary>
    /// Patch the private UpdateXpAndLevel() method as a Postfix.
    /// After CheckForLevelup() runs inside UpdateXpAndLevel, check if level changed.
    /// This is where we can detect level-ups and send the burden warning immediately.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateXpAndLevel")]
    [HarmonyPostfix]
    public static void Patch_UpdateXpAndLevel(Player __instance)
    {
        var threshold = PatchClass.Settings.IgnoreBurdenBelowCharacterLevel;
        var currentLevel = __instance.Level ?? 1;

        // Only track players below threshold
        if (currentLevel >= threshold)
        {
            // Remove from cache if leveled past threshold
            PlayerLevelCache.Remove(__instance.Guid);
            return;
        }

        // Get cached level (level before UpdateXpAndLevel was called)
        if (!PlayerLevelCache.ContainsKey(__instance.Guid))
        {
            // First time seeing this below-threshold player - just cache them
            PlayerLevelCache[__instance.Guid] = currentLevel;
            return;
        }

        var startingLevel = PlayerLevelCache[__instance.Guid];

        // Check if we crossed the burden threshold (exact same logic as custom server code)
        if (startingLevel < threshold && currentLevel >= threshold)
        {
            // Send warning message immediately (just like in custom server code)
            __instance.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"WARNING: Your level has reached {currentLevel} you now suffer from the effects of burden!\n" +
                $"(This effect may not be applied until the next time you log in.)",
                ChatMessageType.CombatEnemy
            ));
            // Remove from cache since they're now at/above threshold
            PlayerLevelCache.Remove(__instance.Guid);
        }
        else
        {
            // Update cache to current level for next call (only if still below threshold)
            PlayerLevelCache[__instance.Guid] = currentLevel;
        }
    }

    // ===== PATCH 5: Cleanup player cache on logout =====
    /// <summary>
    /// Patch Player.LogOut() to clean up the level cache for this player.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.LogOut))]
    [HarmonyPrefix]
    public static void Patch_LogOut(Player __instance)
    {
        // Remove this player from the level cache to prevent memory leaks
        if (PlayerLevelCache.ContainsKey(__instance.Guid))
        {
            PlayerLevelCache.Remove(__instance.Guid);
        }
    }

}

/// <summary>
/// Static helper class for extension methods and utilities
/// </summary>
public static class NoBurdenHelpers
{
    /// <summary>
    /// Extension method to check if a player should ignore burden.
    /// Allows other mods or code to easily check if a player is below the threshold.
    /// </summary>
    public static bool IsBurdenIgnored(this Player player)
    {
        var threshold = PatchClass.Settings?.IgnoreBurdenBelowCharacterLevel ?? 50;
        return player.Level < threshold;
    }
}

public class Settings
{
    /// <summary>
    /// The character level at which burden will start to apply.
    /// Players below this level ignore encumbrance mechanics.
    /// Default: 50 (burden applies at level 50 and above)
    /// Set to 0 for retail behavior (burden applies at all levels)
    /// </summary>
    public long IgnoreBurdenBelowCharacterLevel { get; set; } = 50;
}
