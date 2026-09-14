using System;
using System.IO;
using BepInEx;
using HarmonyLib;

namespace Hoard
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInIncompatibility("randyknapp.mods.equipmentandquickslots")]
    [BepInIncompatibility("goldenrevolver.quick_stack_store")]
    [BepInIncompatibility("Azumatt.AzuCraftyBoxes")]
    [BepInIncompatibility("org.bepinex.plugins.valheimstorage")]
    [BepInIncompatibility("aedenthorn.BuildingRepair")]
    [BepInIncompatibility("aedenthorn.InstantMonsterDrop")]
    [BepInIncompatibility("DarkmoonBlade.ValheimAchievementsEnabler")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.ryan.hoard";
        public const string Name = "Hoard";
        public const string Version = "0.7.1";

        private Harmony _harmony;
        private FileSystemWatcher _watcher;
        private DateTime _lastConfigWrite;

        private void Awake()
        {
            Log.Source = Logger;
            HoardConfig.Bind(Config);
            // All or nothing: a patch target that vanished in a game update must not leave
            // half the patches applied (the state that silently corrupts inventories).
            _harmony = new Harmony(Guid);
            try
            {
                _harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception e)
            {
                Log.Error($"{Name} {Version} failed to patch the game and has been fully disabled. Rebuild against this game version (valheim/Hoard/build.sh) and check the report of tools/PatchCheck.\n{e}");
                _harmony.UnpatchSelf();
                return;
            }
            WatchConfigFile();
            Log.Info($"{Name} {Version} loaded");
        }

        // Edits to the .cfg on disk (any editor) apply live.
        private void WatchConfigFile()
        {
            try
            {
                Config.SaveOnConfigSet = true;
                string dir = Path.GetDirectoryName(Config.ConfigFilePath);
                _watcher = new FileSystemWatcher(dir, Path.GetFileName(Config.ConfigFilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += (_, __) => _lastConfigWrite = DateTime.UtcNow;
            }
            catch (Exception e) { Log.Warn($"Config file watcher unavailable: {e.Message}"); }
        }

        private DateTime _reloadAt = DateTime.MaxValue;

        private void Update()
        {
            // Debounced reload off the main thread's watcher event.
            if (_lastConfigWrite != default)
            {
                _reloadAt = _lastConfigWrite.AddMilliseconds(400);
                _lastConfigWrite = default;
            }
            if (DateTime.UtcNow >= _reloadAt)
            {
                _reloadAt = DateTime.MaxValue;
                try
                {
                    Config.SaveOnConfigSet = false;
                    Config.Reload();
                    Log.Info("Config reloaded from disk");
                }
                catch (Exception e) { Log.Warn($"Config reload failed: {e.Message}"); }
                finally { Config.SaveOnConfigSet = true; }
            }

            ConfigWindow.PollToggle();
            if (!ConfigWindow.Visible)
            {
                Slots.HandleHotkeys();
                if (Player.m_localPlayer && !GameState.IgnoreKeysInPlaceMode()) RowPlanting.HandleInput(Player.m_localPlayer);
            }
        }

        private void LateUpdate() => SlotValidation.Run();

        private void OnGUI() => ConfigWindow.OnGUI();
    }
}
