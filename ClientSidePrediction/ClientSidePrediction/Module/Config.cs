using BepInEx;
using BepInEx.Configuration;

namespace ClientSidePrediction.BepInEx {
    public static partial class ConfigManager {
        public static ConfigFile configFile;

        static ConfigManager() {
            string text = Path.Combine(Paths.ConfigPath, $"{Module.Name}.cfg");
            configFile = new ConfigFile(text, true);

            debug = configFile.Bind(
                "Debug",
                "enable",
                false,
                "Enables debug messages when true.");

            tonguePredictThreshold = configFile.Bind(
                "Settings",
                "TonguePredictThreshold",
                150,
                "When ping (in ms) exceeds this value enemy tongue windup animations are predicted.");
        }

        public static bool Debug {
            get { return debug.Value; }
            set { debug.Value = value; }
        }
        private static ConfigEntry<bool> debug;

        public static int TonguePredictThreshold {
            get { return tonguePredictThreshold.Value; }
            set { tonguePredictThreshold.Value = value; }
        }
        private static ConfigEntry<int> tonguePredictThreshold;
    }
}