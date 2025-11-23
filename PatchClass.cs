
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
    public static long CachedThreshold = 10;

    /// <summary>
    /// Static reference to the instance so static command handlers can access SettingsContainer
    /// </summary>
    private static PatchClass? Instance = null;

    public override Task OnStartSuccess()
    {
        // Store instance reference for static command handlers
        Instance = this;

        // Assign Settings from SettingsContainer to make it available throughout the class
        Settings = SettingsContainer.Settings;

        // Cache the threshold for fast access in patches
        CachedThreshold = Settings.BurdenThresholdLevel;

        ModManager.Log($"NoBurden started successfully!");
        ModManager.Log($"Burden disabled for characters level {CachedThreshold} and below");

        return Task.CompletedTask;
    }

    public override void Stop()
    {
        base.Stop();
        ModManager.Log($"NoBurden stopped!");
    }

    /// <summary>
    /// Instance method that reloads settings from disk.
    /// The framework handles file watching, but this allows manual reload via command.
    /// </summary>
    public void ReloadSettings()
    {
        try
        {
            // Use framework's standard settings path
            var settingsPath = Path.Combine(Mod.Instance.ModPath, "Settings.json");

            if (!File.Exists(settingsPath))
            {
                ModManager.Log($"Settings.json not found at: {settingsPath}");
                return;
            }

            // Read the file with proper sharing
            using (var fileStream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var newSettings = JsonSerializer.DeserializeAsync<Settings>(fileStream).Result;
                if (newSettings != null)
                {
                    Settings = newSettings;
                    CachedThreshold = Settings.BurdenThresholdLevel;
                }
            }
        }
        catch (Exception ex)
        {
            ModManager.Log($"Error reloading NoBurden settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Admin command for NoBurden mod management
    /// Usage: /noburden [status|reload|limit|default] [args]
    /// Examples:
    ///   /noburden status - Show current threshold
    ///   /noburden reload - Reload from Settings.json
    ///   /noburden limit 15 - Set threshold to 15
    ///   /noburden default - Reset to default (10)
    /// </summary>
    [CommandHandler("noburden", AccessLevel.Admin, CommandHandlerFlag.None, -1, "Manage NoBurden settings", "status|reload|limit <level>|default")]
    public static void HandleNoBurdenCommand(Session session, params string[] parameters)
    {
        if (Instance == null)
        {
            SendMessage(session, "NoBurden mod not properly initialized.", ChatMessageType.Broadcast);
            return;
        }

        // Show help and status if no parameters
        if (parameters.Length == 0)
        {
            HandleHelpCommand(session);
            HandleStatusCommand(session);
            return;
        }

        var subcommand = parameters[0].ToLower();

        switch (subcommand)
        {
            case "status":
                HandleStatusCommand(session);
                break;

            case "reload":
                HandleReloadCommand(session);
                break;

            case "limit":
                HandleLimitCommand(session, parameters);
                break;

            case "default":
                HandleDefaultCommand(session);
                break;

            default:
                SendMessage(session, $"Unknown command: {subcommand}. Use: status, reload, limit <level>, or default", ChatMessageType.Broadcast);
                break;
        }
    }

    private static void HandleHelpCommand(Session session)
    {
        SendMessage(session, "=== NoBurden Commands ===", ChatMessageType.CombatEnemy);
        SendMessage(session, "/noburden status - Show current threshold", ChatMessageType.Broadcast);
        SendMessage(session, "/noburden reload - Reload from Settings.json", ChatMessageType.Broadcast);
        SendMessage(session, "/noburden limit <level> - Set threshold level", ChatMessageType.Broadcast);
        SendMessage(session, "/noburden default - Reset to default (10)", ChatMessageType.Broadcast);
    }

    private static void HandleStatusCommand(Session session)
    {
        var message = $"NoBurden - Current burden threshold: {CachedThreshold}";
        SendMessage(session, message, ChatMessageType.CombatEnemy);
        ModManager.Log(message);
    }

    private static void HandleReloadCommand(Session session)
    {
        var oldThreshold = CachedThreshold;
        Instance?.ReloadSettings();

        var feedback = $"NoBurden settings reloaded. Burden threshold: {CachedThreshold}";
        if (oldThreshold != CachedThreshold)
            feedback += $" (was {oldThreshold})";

        SendMessage(session, feedback, ChatMessageType.CombatEnemy);
        ModManager.Log(feedback);
    }

    private static void HandleLimitCommand(Session session, params string[] parameters)
    {
        if (parameters.Length < 2)
        {
            SendMessage(session, "Usage: noburden limit <level>", ChatMessageType.Broadcast);
            return;
        }

        if (!long.TryParse(parameters[1], out var newLevel))
        {
            SendMessage(session, $"Invalid level: {parameters[1]}. Must be a number.", ChatMessageType.Broadcast);
            return;
        }

        if (newLevel < 0)
        {
            SendMessage(session, "Level cannot be negative.", ChatMessageType.Broadcast);
            return;
        }

        var oldThreshold = CachedThreshold;

        // Update settings object and cache
        PatchClass.Settings.BurdenThresholdLevel = newLevel;
        CachedThreshold = newLevel;

        // Save to Settings.json
        try
        {
            var settingsPath = Path.Combine(Mod.Instance.ModPath, "Settings.json");
            using (var fileStream = new FileStream(settingsPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                JsonSerializer.SerializeAsync(fileStream, PatchClass.Settings, new JsonSerializerOptions { WriteIndented = true }).Wait();
            }

            var feedback = $"NoBurden burden threshold updated: {newLevel}";
            if (oldThreshold != newLevel)
                feedback += $" (was {oldThreshold})";

            SendMessage(session, feedback, ChatMessageType.CombatEnemy);
            ModManager.Log(feedback);
        }
        catch (Exception ex)
        {
            PatchClass.Settings.BurdenThresholdLevel = oldThreshold;
            CachedThreshold = oldThreshold;
            SendMessage(session, $"Error saving settings: {ex.Message}", ChatMessageType.Broadcast);
            ModManager.Log($"Error saving NoBurden settings: {ex.Message}");
        }
    }

    private static void HandleDefaultCommand(Session session)
    {
        var oldThreshold = CachedThreshold;
        const long defaultLevel = 10;

        PatchClass.Settings.BurdenThresholdLevel = defaultLevel;
        CachedThreshold = defaultLevel;

        // Save to Settings.json
        try
        {
            var settingsPath = Path.Combine(Mod.Instance.ModPath, "Settings.json");
            using (var fileStream = new FileStream(settingsPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                JsonSerializer.SerializeAsync(fileStream, PatchClass.Settings, new JsonSerializerOptions { WriteIndented = true }).Wait();
            }

            var feedback = $"NoBurden threshold reset to default: {defaultLevel}";
            if (oldThreshold != defaultLevel)
                feedback += $" (was {oldThreshold})";

            SendMessage(session, feedback, ChatMessageType.CombatEnemy);
            ModManager.Log(feedback);
        }
        catch (Exception ex)
        {
            PatchClass.Settings.BurdenThresholdLevel = oldThreshold;
            CachedThreshold = oldThreshold;
            SendMessage(session, $"Error saving settings: {ex.Message}", ChatMessageType.Broadcast);
            ModManager.Log($"Error resetting NoBurden settings: {ex.Message}");
        }
    }

    private static void SendMessage(Session session, string message, ChatMessageType messageType)
    {
        if (session?.Player != null)
            ChatPacket.SendServerMessage(session, message, messageType);
        else
            Console.WriteLine(message);
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
    /// Players at or below this level ignore encumbrance mechanics.
    /// Default: 10 (burden applies at level 11 and above)
    /// Set to 0 for retail behavior (burden applies at all levels)
    /// </summary>
    public long BurdenThresholdLevel { get; set; } = 10;
}
