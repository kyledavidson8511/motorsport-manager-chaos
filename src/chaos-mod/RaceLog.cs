using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ChaosMod
{
    /// <summary>
    /// Per-race event log: one budget line, a line per crash and per failure as
    /// they happen, and a summary when the race ends. Logging only — nothing
    /// here changes gameplay.
    /// </summary>
    internal static class RaceLog
    {
        // Budget bookkeeping (see CrashBudgetPatch for why a race can roll twice).
        public static bool BudgetRolledThisSession;
        public static int[] BudgetChunkCounts;
        public static int Budget;
        public static int BaseGameRoll;

        // Tallies for the end-of-race summary.
        public static int Crashes;
        public static int SafetyCars;
        public static int VirtualSafetyCars;
        public static int RandomFailures;
        public static int WearFailures;
        public static int Retirements;

        private static readonly HashSet<string> sLoggedCrashes = new HashSet<string>();

        public static void ResetSession()
        {
            BudgetRolledThisSession = false;
            BudgetChunkCounts = null;
            Budget = 0;
            BaseGameRoll = 0;
            Crashes = SafetyCars = VirtualSafetyCars = 0;
            RandomFailures = WearFailures = Retirements = 0;
            sLoggedCrashes.Clear();
        }

        public static bool IsRace()
        {
            try
            {
                return Game.instance.sessionManager.sessionType == SessionDetails.SessionType.Race;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string LapText()
        {
            try
            {
                SessionManager s = Game.instance.sessionManager;
                return string.Format("lap {0}/{1}", s.lap, s.lapCount);
            }
            catch (Exception)
            {
                return "lap ?";
            }
        }

        public static string DriverName(RacingVehicle vehicle)
        {
            try
            {
                return vehicle != null && vehicle.driver != null ? vehicle.driver.name : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>The game can report one crash from two places; count it once.</summary>
        public static bool FirstReportOfCrash(RacingVehicle vehicle)
        {
            string key = DriverName(vehicle) + "|" + LapText();
            return sLoggedCrashes.Add(key);
        }
    }

    // ----------------------------------------------------------------------
    // Crash incidents and what they brought out.
    // ----------------------------------------------------------------------
    [HarmonyPatch(typeof(CrashDirector), "OnCrashIncident")]
    internal static class CrashIncidentLogPatch
    {
        internal static string Outcome;

        private static void Prefix(RacingVehicle inVehicle, out bool __state)
        {
            __state = false;
            if (!Main.Enabled || !RaceLog.IsRace() || !RaceLog.FirstReportOfCrash(inVehicle))
            {
                return;
            }
            __state = true;
            bool green = Game.instance.sessionManager.flag == SessionManager.Flag.Green;
            Outcome = green ? "yellow flag" : "already under caution";
        }

        private static void Postfix(RacingVehicle inVehicle, bool __state)
        {
            if (!__state)
            {
                return;
            }
            RaceLog.Crashes++;
            Main.Log(string.Format("CRASH: {0}, {1} -> {2}. (crash {3} of budget {4})",
                RaceLog.DriverName(inVehicle), RaceLog.LapText(), Outcome, RaceLog.Crashes, RaceLog.Budget));
        }
    }

    [HarmonyPatch(typeof(CrashDirector), "SetSafetyCarFlag")]
    internal static class SafetyCarLogPatch
    {
        private static void Postfix()
        {
            if (!Main.Enabled || !RaceLog.IsRace())
            {
                return;
            }
            RaceLog.SafetyCars++;
            CrashIncidentLogPatch.Outcome = "SAFETY CAR";
        }
    }

    [HarmonyPatch(typeof(CrashDirector), "SetVirtualSafetyCarFlag")]
    internal static class VirtualSafetyCarLogPatch
    {
        private static void Postfix()
        {
            if (!Main.Enabled || !RaceLog.IsRace())
            {
                return;
            }
            RaceLog.VirtualSafetyCars++;
            CrashIncidentLogPatch.Outcome = "VIRTUAL SAFETY CAR";
        }
    }

    // ----------------------------------------------------------------------
    // End-of-race summary. CrashDirector.OnSessionEnd runs for every session;
    // we summarise races only, then reset for the next session.
    // ----------------------------------------------------------------------
    [HarmonyPatch(typeof(CrashDirector), "OnSessionEnd")]
    internal static class SessionEndLogPatch
    {
        private static void Prefix()
        {
            if (Main.Enabled && RaceLog.IsRace() && RaceLog.BudgetRolledThisSession)
            {
                Main.Log(string.Format(
                    "RACE SUMMARY: {0} crash(es) from a budget of {1} (base game rolled {2}); {3} safety car(s), {4} VSC(s); " +
                    "{5} random failure(s), {6} worn-out failure(s), {7} retirement(s) from failures.",
                    RaceLog.Crashes, RaceLog.Budget, RaceLog.BaseGameRoll, RaceLog.SafetyCars, RaceLog.VirtualSafetyCars,
                    RaceLog.RandomFailures, RaceLog.WearFailures, RaceLog.Retirements));
            }
            RaceLog.ResetSession();
        }
    }
}
