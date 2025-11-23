
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

    /// <summary>
    /// Cached threshold to avoid repeated Settings access in hot paths.
    /// Updated whenever settings are reloaded.
    /// </summary>
    public static long CachedThreshold = 50;

    /// <summary>
    /// Static reference to the instance so static command handlers can access SettingsContainer
    /// </summary>
    private static PatchClass Instance = null;

    public override Task OnStartSuccess()
    {
        // Store instance reference for static command handlers
        Instance = this;

        // Assign Settings from SettingsContainer to make it available throughout the class
        Settings = SettingsContainer.Settings;

        // Cache the threshold for fast access in patches
        CachedThreshold = Settings.IgnoreBurdenBelowCharacterLevel;

        ModManager.Log($"NoBurden started successfully!");
        ModManager.Log($"Burden disabled for characters below level {CachedThreshold}");

        return Task.CompletedTask;
    }

    public override void Stop()
    {
        base.Stop();
        ModManager.Log($"NoBurden stopped!");
    }

    /// <summary>
    /// Instance method that performs the actual settings reload.
    /// This has access to SettingsContainer as an instance member.
    /// </summary>
    public void ReloadSettings()
    {
        // Reload settings from SettingsContainer (handles file I/O internally)
        SettingsContainer.LoadOrCreateAsync().GetAwaiter().GetResult();
        Settings = SettingsContainer.Settings;
        CachedThreshold = Settings.IgnoreBurdenBelowCharacterLevel;
    }

    /// <summary>
    /// Admin command to reload settings
    /// Usage: /nbreload (in-game) or nbreload (console - no prefix)
    /// </summary>
    [CommandHandler("nbreload", AccessLevel.Admin, CommandHandlerFlag.None, 0, "Reload NoBurden settings", "")]
    public static void HandleReloadNoBurdenSettings(Session session, params string[] parameters)
    {
        if (Instance == null)
        {
            if (session?.Player != null)
                ChatPacket.SendServerMessage(session, "NoBurden mod not properly initialized.", ChatMessageType.Broadcast);
            else
                Console.WriteLine("NoBurden mod not properly initialized.");
            return;
        }

        var oldThreshold = PatchClass.CachedThreshold;

        // Reload settings from disk via instance method
        Instance.ReloadSettings();

        // Provide feedback
        var feedback = $"NoBurden settings reloaded. Burden threshold: {CachedThreshold}";
        if (oldThreshold != CachedThreshold)
            feedback += $" (was {oldThreshold})";

        // Send feedback to appropriate channel (in-game vs console)
        if (session?.Player != null)
        {
            ChatPacket.SendServerMessage(session, feedback, ChatMessageType.CombatEnemy);
        }
        else
        {
            Console.WriteLine(feedback);
        }
        ModManager.Log(feedback);
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
        if (__instance.Level < CachedThreshold)
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
            if (player.Level < CachedThreshold)
            {
                value = 0;  // Zero out burden for low-level players
            }
        }
    }

    // ===== PATCH 3: UpdateXpAndLevel Prefix - Capture Level Before =====
    /// <summary>
    /// Prefix for UpdateXpAndLevel() captures player level BEFORE the method runs.
    /// Stores in thread-local variable for use in Postfix.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateXpAndLevel")]
    [HarmonyPrefix]
    public static void Patch_UpdateXpAndLevel_Prefix(Player __instance)
    {
        // Capture the level before UpdateXpAndLevel runs
        CapturedLevelBeforeUpdate = __instance.Level ?? 1;
    }

    // ===== PATCH 4: UpdateXpAndLevel Postfix - Detect Level-Up =====
    /// <summary>
    /// Postfix for UpdateXpAndLevel() detects if level changed and warns if threshold crossed.
    /// Uses captured level from Prefix - no persistent cache needed.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateXpAndLevel")]
    [HarmonyPostfix]
    public static void Patch_UpdateXpAndLevel_Postfix(Player __instance)
    {
        var startingLevel = CapturedLevelBeforeUpdate ?? (__instance.Level ?? 1);
        var currentLevel = __instance.Level ?? 1;

        // Clear the thread-local after use
        CapturedLevelBeforeUpdate = null;

        // Check if we crossed the burden threshold (exact same logic as custom server code)
        if (startingLevel < CachedThreshold && currentLevel >= CachedThreshold)
        {
            // Send warning message immediately (just like in custom server code)
            __instance.Session?.Network.EnqueueSend(new GameMessageSystemChat(
                $"WARNING: Your level has reached {currentLevel} you now suffer from the effects of burden!\n" +
                $"(This effect may not be applied until the next time you log in.)",
                ChatMessageType.CombatEnemy
            ));
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
        return player.Level < PatchClass.CachedThreshold;
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
