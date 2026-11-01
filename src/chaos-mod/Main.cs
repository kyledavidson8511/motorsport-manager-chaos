using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace ChaosMod
{
    public static class Main
    {
        public static bool Enabled;
        public static Settings Settings;
        public static UnityModManager.ModEntry.ModLogger Logger;
        private static Harmony sHarmony;

        // Entry point named in Info.json.
        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            sHarmony = new Harmony(modEntry.Info.Id);
            sHarmony.PatchAll(Assembly.GetExecutingAssembly());
            Enabled = true;
            Logger.Log("Chaos mod loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            // Patches stay applied; every patch checks Enabled and does nothing when off.
            Enabled = value;
            return true;
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }

        public static void Log(string message)
        {
            if (Settings != null && Settings.LogEvents && Logger != null)
            {
                Logger.Log(message);
            }
        }

        // ------------------------------------------------------------------
        // Season curve
        // ------------------------------------------------------------------

        /// <summary>0 at the first round of the season, 1 at the last.</summary>
        public static float SeasonProgress()
        {
            try
            {
                Championship championship = Game.instance.sessionManager.championship;
                int count = championship.eventCount;
                if (count <= 1)
                {
                    return 0f;
                }
                return Mathf.Clamp01((float)championship.eventNumber / (count - 1));
            }
            catch (Exception)
            {
                return 0.5f;
            }
        }

        /// <summary>
        /// Blend from the round-1 value to the settled value. Smoothstep, so the
        /// early rounds stay chaotic and it tapers off rather than dropping in a line.
        /// </summary>
        public static float SeasonValue(float round1, float settled)
        {
            return SeasonValue(round1, settled, SeasonProgress(), Settings.RampEndsAt);
        }

        public static float SeasonValue(float round1, float settled, float progress, float rampEndsAt)
        {
            float t = rampEndsAt <= 0.001f ? 1f : Mathf.Clamp01(progress / rampEndsAt);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(round1, settled, t);
        }

        // ------------------------------------------------------------------
        // Settings panel (UMM: Ctrl+F10 in game)
        // ------------------------------------------------------------------

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            Settings s = Settings;
            float progress = SeasonProgress();
            bool inGame = Game.instance != null && Game.instance.sessionManager != null && Game.instance.sessionManager.championship != null;

            GUILayout.Label("<b>Season curve</b> — chaos is highest at round 1 and eases to the settled values.");
            s.RampEndsAt = Slider("Settles by (fraction of season)", s.RampEndsAt, 0.05f, 1f, "0.00");

            GUILayout.Space(8);
            GUILayout.Label("<b>Crashes</b> (base game averages ~0.9 per race; half of races have none)");
            s.CrashesPerRaceRound1 = Slider("Average crashes per race — round 1", s.CrashesPerRaceRound1, 0f, 6f, "0.0");
            s.CrashesPerRaceSettled = Slider("Average crashes per race — settled", s.CrashesPerRaceSettled, 0f, 6f, "0.0");
            s.MaxCrashesPerRace = Mathf.RoundToInt(Slider("Max crashes in one race", s.MaxCrashesPerRace, 1f, 12f, "0"));
            s.NeverBelowBaseGame = GUILayout.Toggle(s.NeverBelowBaseGame, " Never fewer crashes than the base game would have rolled");

            GUILayout.Space(8);
            GUILayout.Label("<b>Random part failures</b> (base game: none — parts only fail when worn to zero)");
            s.RandomFailureChanceRound1 = Slider("Chance per car per race — round 1", s.RandomFailureChanceRound1, 0f, 0.5f, "0%");
            s.RandomFailureChanceSettled = Slider("Chance per car per race — settled", s.RandomFailureChanceSettled, 0f, 0.5f, "0%");
            s.UnreliabilityWeight = Slider("Extra risk for unreliable parts", s.UnreliabilityWeight, 0f, 5f, "0.0");

            GUILayout.Space(8);
            GUILayout.Label("<b>Wear & retirements</b>");
            s.WearMultiplierRound1 = Slider("Part wear multiplier — round 1", s.WearMultiplierRound1, 0.5f, 3f, "0.00x");
            s.WearMultiplierSettled = Slider("Part wear multiplier — settled", s.WearMultiplierSettled, 0.5f, 3f, "0.00x");
            s.ExtraRetirementChance = Slider("Extra chance a failure ends the race", s.ExtraRetirementChance, 0f, 1f, "0%");

            GUILayout.Space(8);
            s.LogEvents = GUILayout.Toggle(s.LogEvents, " Log crash budgets and random failures to the UMM log");

            GUILayout.Space(8);
            if (inGame)
            {
                GUILayout.Label(string.Format(
                    "Right now ({0:0%} through the season): {1:0.0} crashes/race, {2:0.0%} random-failure chance per car, {3:0.00}x wear.",
                    progress,
                    SeasonValue(s.CrashesPerRaceRound1, s.CrashesPerRaceSettled),
                    SeasonValue(s.RandomFailureChanceRound1, s.RandomFailureChanceSettled),
                    SeasonValue(s.WearMultiplierRound1, s.WearMultiplierSettled)));
            }
            if (GUILayout.Button("Reset to defaults", GUILayout.Width(160)))
            {
                Settings fresh = new Settings();
                s.RampEndsAt = fresh.RampEndsAt;
                s.CrashesPerRaceRound1 = fresh.CrashesPerRaceRound1;
                s.CrashesPerRaceSettled = fresh.CrashesPerRaceSettled;
                s.MaxCrashesPerRace = fresh.MaxCrashesPerRace;
                s.NeverBelowBaseGame = fresh.NeverBelowBaseGame;
                s.RandomFailureChanceRound1 = fresh.RandomFailureChanceRound1;
                s.RandomFailureChanceSettled = fresh.RandomFailureChanceSettled;
                s.UnreliabilityWeight = fresh.UnreliabilityWeight;
                s.WearMultiplierRound1 = fresh.WearMultiplierRound1;
                s.WearMultiplierSettled = fresh.WearMultiplierSettled;
                s.ExtraRetirementChance = fresh.ExtraRetirementChance;
                s.LogEvents = fresh.LogEvents;
            }
        }

        private static float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(330));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220));
            GUILayout.Label(value.ToString(format), GUILayout.Width(70));
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
