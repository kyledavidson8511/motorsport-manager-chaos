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
            try
            {
                GameObject overlay = new GameObject("ChaosSessionOverlay");
                UnityEngine.Object.DontDestroyOnLoad(overlay);
                overlay.AddComponent<SessionChaosOverlay>();
            }
            catch (Exception e)
            {
                Logger.Error("Overlay failed to start: " + e.Message);
            }
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
            GUILayout.Label("<b>Practice & qualifying</b> (base game: no crashes or failures outside races; chances are per car per session)");
            s.PracticeQualiChaos = GUILayout.Toggle(s.PracticeQualiChaos, " Crashes and failures in practice and qualifying");
            if (s.PracticeQualiChaos)
            {
                s.PracticeCrashChanceRound1 = Slider("Practice crash chance — round 1", s.PracticeCrashChanceRound1, 0f, 0.3f, "0%");
                s.PracticeCrashChanceSettled = Slider("Practice crash chance — settled", s.PracticeCrashChanceSettled, 0f, 0.3f, "0%");
                s.PracticeFailureChanceRound1 = Slider("Practice failure chance — round 1", s.PracticeFailureChanceRound1, 0f, 0.3f, "0%");
                s.PracticeFailureChanceSettled = Slider("Practice failure chance — settled", s.PracticeFailureChanceSettled, 0f, 0.3f, "0%");
                s.QualiCrashChanceRound1 = Slider("Qualifying crash chance — round 1", s.QualiCrashChanceRound1, 0f, 0.3f, "0%");
                s.QualiCrashChanceSettled = Slider("Qualifying crash chance — settled", s.QualiCrashChanceSettled, 0f, 0.3f, "0%");
                s.QualiFailureChanceRound1 = Slider("Qualifying failure chance — round 1", s.QualiFailureChanceRound1, 0f, 0.3f, "0%");
                s.QualiFailureChanceSettled = Slider("Qualifying failure chance — settled", s.QualiFailureChanceSettled, 0f, 0.3f, "0%");
                s.CrashRepairShare = Slider("Crash repairs (share of the session)", s.CrashRepairShare, 0.05f, 1f, "0%");
                s.FailureRepairShare = Slider("Failure repairs (share of the session)", s.FailureRepairShare, 0.05f, 1f, "0%");
                s.QualiCrashEndsSession = GUILayout.Toggle(s.QualiCrashEndsSession, " A qualifying crash ends that car's session");
                s.CrashDamageCarriesOver = GUILayout.Toggle(s.CrashDamageCarriesOver, " Crash damage carries into the race (off: parts are put back after repairs)");
            }

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
                s.PracticeQualiChaos = fresh.PracticeQualiChaos;
                s.PracticeCrashChanceRound1 = fresh.PracticeCrashChanceRound1;
                s.PracticeCrashChanceSettled = fresh.PracticeCrashChanceSettled;
                s.PracticeFailureChanceRound1 = fresh.PracticeFailureChanceRound1;
                s.PracticeFailureChanceSettled = fresh.PracticeFailureChanceSettled;
                s.QualiCrashChanceRound1 = fresh.QualiCrashChanceRound1;
                s.QualiCrashChanceSettled = fresh.QualiCrashChanceSettled;
                s.QualiFailureChanceRound1 = fresh.QualiFailureChanceRound1;
                s.QualiFailureChanceSettled = fresh.QualiFailureChanceSettled;
                s.CrashRepairShare = fresh.CrashRepairShare;
                s.FailureRepairShare = fresh.FailureRepairShare;
                s.QualiCrashEndsSession = fresh.QualiCrashEndsSession;
                s.CrashDamageCarriesOver = fresh.CrashDamageCarriesOver;
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
