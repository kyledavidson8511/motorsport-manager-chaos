using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ChaosMod
{
    /// <summary>
    /// 1.1.0: crashes and mechanical failures in practice and qualifying.
    ///
    /// The base game has neither: CrashDirector only allows crashes in a race, and part
    /// condition only wears in a race. Rather than switching those race systems on (which
    /// would put lasting damage on the race car and send out a safety car in practice),
    /// each practice/qualifying session gets its own small schedule:
    ///
    ///   • Crash: the car is flagged to crash (the game's own crash-lane path, so it looks
    ///     like any race crash), then sits out a repair window in the garage. A qualifying
    ///     crash ends that car's session. Part condition is put back as it was, so the race
    ///     car isn't left at the game's 0-20% crash damage. No safety car outside races.
    ///   • Failure: a part (weighted toward unreliable ones) gives up. The car smokes,
    ///     returns to the garage, and can't go out again until the repair window is over.
    ///
    /// Everything is undone at session end (and at the next session start, in case a
    /// session ended in a way that skipped the hook), so nothing carries into the race.
    /// </summary>
    internal static class SessionChaos
    {
        private class Pending
        {
            public RacingVehicle Car;
            public bool Crash;
            public float AtTime;      // normalised session time to fire at
            public bool Armed;        // crash: isSetToCrash set, waiting for the crash lane
            public float ArmedAt;
            public int Attempts;
        }

        internal class Lock
        {
            public float Until;       // normalised session time; > 1 = rest of the session
            public bool Crashed;
            public string Why;
            public bool SmokeOn;
            public bool BlockLogged;
        }

        private static readonly List<Pending> sPending = new List<Pending>();
        internal static readonly Dictionary<RacingVehicle, Lock> Locks = new Dictionary<RacingVehicle, Lock>();
        private static readonly Dictionary<CarPartCondition, float> sSavedCondition = new Dictionary<CarPartCondition, float>();
        private static bool sActive;
        private static string sSessionName = "";
        private static int sCrashes, sFailures, sLinedUpCrashes, sLinedUpFailures;

        public static bool Active => sActive;
        public static string SessionName => sSessionName;

        private static bool IsPracticeOrQuali(SessionDetails.SessionType t)
        {
            return t == SessionDetails.SessionType.Practice || t == SessionDetails.SessionType.Qualifying;
        }

        // ------------------------------------------------------------------
        // Session start: roll the schedule.
        // ------------------------------------------------------------------
        public static void OnSessionStart()
        {
            Reset(true);
            if (!Main.Enabled || Main.Settings == null || !Main.Settings.PracticeQualiChaos)
            {
                return;
            }
            SessionManager session = Game.instance.sessionManager;
            SessionDetails.SessionType type = session.sessionType;
            if (!IsPracticeOrQuali(type))
            {
                return;
            }
            Settings s = Main.Settings;
            bool quali = type == SessionDetails.SessionType.Qualifying;
            sSessionName = quali ? "QUALIFYING" : "PRACTICE";
            float crashChance = quali ? Main.SeasonValue(s.QualiCrashChanceRound1, s.QualiCrashChanceSettled)
                                      : Main.SeasonValue(s.PracticeCrashChanceRound1, s.PracticeCrashChanceSettled);
            float failChance = quali ? Main.SeasonValue(s.QualiFailureChanceRound1, s.QualiFailureChanceSettled)
                                     : Main.SeasonValue(s.PracticeFailureChanceRound1, s.PracticeFailureChanceSettled);
            bool wet = false;
            try
            {
                wet = session.currentSessionWeather.GetAverageWeather().rainType >= Weather.RainType.Medium;
            }
            catch (Exception)
            {
            }
            if (wet)
            {
                crashChance = Mathf.Min(0.95f, crashChance * 1.5f);
            }

            VehicleManager vm = Game.instance.vehicleManager;
            for (int i = 0; i < vm.vehicleCount; i++)
            {
                RacingVehicle car = vm.GetVehicle(i) as RacingVehicle;
                if (car == null)
                {
                    continue;
                }
                if (Rng.Value01() < crashChance)
                {
                    sPending.Add(new Pending { Car = car, Crash = true, AtTime = Mathf.Lerp(0.08f, 0.85f, Rng.Value01()) });
                    sLinedUpCrashes++;
                }
                if (Rng.Value01() < failChance)
                {
                    sPending.Add(new Pending { Car = car, Crash = false, AtTime = Mathf.Lerp(0.05f, 0.85f, Rng.Value01()) });
                    sLinedUpFailures++;
                }
            }
            sActive = true;
            Main.Log(string.Format("{0} START: {1} crash(es) and {2} failure(s) lined up across the field; season {3:0%}, crash {4:0.0%} and failure {5:0.0%} per car{6}.",
                sSessionName, sLinedUpCrashes, sLinedUpFailures, Main.SeasonProgress(), crashChance, failChance, wet ? " (wet: crash risk ×1.5)" : ""));
        }

        // ------------------------------------------------------------------
        // Every simulation tick.
        // ------------------------------------------------------------------
        public static void Tick()
        {
            if (!sActive)
            {
                return;
            }
            SessionManager session = Game.instance.sessionManager;
            if (!IsPracticeOrQuali(session.sessionType))
            {
                return;
            }
            float now = session.GetNormalizedSessionTime();

            for (int i = sPending.Count - 1; i >= 0; i--)
            {
                Pending p = sPending[i];
                RacingVehicle car = p.Car;
                if (car == null || car.behaviourManager == null)
                {
                    sPending.RemoveAt(i);
                    continue;
                }
                if (Locks.ContainsKey(car) || car.behaviourManager.isOutOfRace && !car.behaviourManager.isCrashed)
                {
                    // Already sidelined (or retired): this incident never happens.
                    if (p.Armed)
                    {
                        SetToCrash(car, false);
                    }
                    sPending.RemoveAt(i);
                    continue;
                }
                if (p.Crash && p.Armed)
                {
                    if (car.behaviourManager.isCrashed)
                    {
                        sPending.RemoveAt(i); // the crash is under way; OnCrashed takes it from here
                        continue;
                    }
                    if (now - p.ArmedAt > 0.04f || !OnFlyingLap(car))
                    {
                        // No crash lane came up in time, or the car pitted: stand down and try again later.
                        SetToCrash(car, false);
                        p.Armed = false;
                        p.Attempts++;
                        p.AtTime = now + 0.03f;
                        if (p.Attempts > 6 || p.AtTime > 0.95f)
                        {
                            sPending.RemoveAt(i);
                        }
                    }
                    continue;
                }
                if (now < p.AtTime || !OnFlyingLap(car))
                {
                    if (now > 0.96f)
                    {
                        sPending.RemoveAt(i); // never got the chance
                    }
                    continue;
                }
                if (p.Crash)
                {
                    SetToCrash(car, true);
                    p.Armed = true;
                    p.ArmedAt = now;
                }
                else
                {
                    Fail(car, now);
                    sPending.RemoveAt(i);
                }
            }

            if (Locks.Count == 0)
            {
                return;
            }
            List<RacingVehicle> done = null;
            foreach (KeyValuePair<RacingVehicle, Lock> kv in Locks)
            {
                RacingVehicle car = kv.Key;
                Lock l = kv.Value;
                if (l.Crashed && car.behaviourManager != null && car.behaviourManager.isCrashed
                    && car.behaviourManager.currentBehaviour.behaviourType == AIBehaviourStateManager.Behaviour.Crashed)
                {
                    // The crash has played out and the game has parked the car in its garage. Turn it
                    // back into an ordinary parked car straight away, so the game's "crashed" state
                    // never reaches session results; the repair hold (this lock) keeps it in.
                    Revive(car);
                }
                if (l.SmokeOn && car.pathState != null && car.pathState.IsInPitlaneArea()
                    && car.pathState.currentState != null && car.pathState.currentState.stateType == PathStateManager.StateType.Garage)
                {
                    SetSmoke(car, false);
                    l.SmokeOn = false;
                }
                if (now >= l.Until)
                {
                    (done = done ?? new List<RacingVehicle>()).Add(car);
                }
            }
            if (done != null)
            {
                for (int i = 0; i < done.Count; i++)
                {
                    Release(done[i], true);
                }
            }
        }

        private static bool OnFlyingLap(RacingVehicle car)
        {
            try
            {
                return car.movementEnabled
                    && car.pathState != null && !car.pathState.IsInPitlaneArea()
                    && car.pathController.currentPathType == PathController.PathType.Track
                    && car.behaviourManager.currentBehaviour != null
                    && car.behaviourManager.currentBehaviour.behaviourType == AIBehaviourStateManager.Behaviour.Racing
                    && !car.timer.hasSeenChequeredFlag;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void SetToCrash(RacingVehicle car, bool on)
        {
            try
            {
                AIRacingBehaviour racing = car.behaviourManager.GetBehaviour<AIRacingBehaviour>();
                if (racing != null)
                {
                    racing.isSetToCrash = on;
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos: could not set the crash flag. " + e.Message);
            }
        }

        // ------------------------------------------------------------------
        // Failure.
        // ------------------------------------------------------------------
        private static void Fail(RacingVehicle car, float now)
        {
            Settings s = Main.Settings;
            bool quali = Game.instance.sessionManager.sessionType == SessionDetails.SessionType.Qualifying;
            string part = PickPart(car);
            float repair = s.FailureRepairShare * Mathf.Lerp(0.6f, 1.4f, Rng.Value01());
            Lock l = new Lock { Until = now + repair, Crashed = false, Why = part + " failure", SmokeOn = true };
            Locks[car] = l;
            SetSmoke(car, true);
            try
            {
                car.strategy.ReturnToGarage();
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos: return to garage failed. " + e.Message);
            }
            try
            {
                CommentaryManager.SendComment(car, Comment.CommentType.MechanicalIssue, car.driver);
            }
            catch (Exception)
            {
            }
            sFailures++;
            Main.Log(string.Format("{0} FAILURE: {1}, {2} — {3}; back to the garage, {4}.",
                sSessionName, RaceLog.DriverName(car), SessionClock(), part, RepairText(l.Until, quali)));
        }

        private static string PickPart(RacingVehicle car)
        {
            try
            {
                CarPart[] parts = car.car.seriesCurrentParts;
                if (parts == null || parts.Length == 0)
                {
                    return "Mechanical";
                }
                float total = 0f;
                float[] w = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    float rel = parts[i] != null ? Mathf.Clamp01(parts[i].reliability) : 1f;
                    w[i] = 1f + Main.Settings.UnreliabilityWeight * (1f - rel);
                    total += w[i];
                }
                float r = Rng.Value01() * total;
                for (int i = 0; i < parts.Length; i++)
                {
                    r -= w[i];
                    if (r <= 0f && parts[i] != null)
                    {
                        return parts[i].GetPartType().ToString();
                    }
                }
                return parts[parts.Length - 1].GetPartType().ToString();
            }
            catch (Exception)
            {
                return "Mechanical";
            }
        }

        private static void SetSmoke(RacingVehicle car, bool on)
        {
            try
            {
                if (car.unityVehicle != null)
                {
                    car.unityVehicle.SetPartConditionEffects(on ? CarPartCondition.PartState.Failure : CarPartCondition.PartState.Optimal);
                }
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------------
        // Crash: called around AICrashedBehaviour.OnEnter.
        // ------------------------------------------------------------------
        public static void BeforeCrashed(RacingVehicle car)
        {
            sSavedCondition.Clear();
            if (!sActive || car == null)
            {
                return;
            }
            try
            {
                CarPart[] parts = car.car.seriesCurrentParts;
                for (int i = 0; parts != null && i < parts.Length; i++)
                {
                    if (parts[i] != null)
                    {
                        sSavedCondition[parts[i].partCondition] = parts[i].partCondition.condition;
                    }
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos: could not record part condition. " + e.Message);
            }
        }

        public static void AfterCrashed(RacingVehicle car)
        {
            if (!sActive || car == null)
            {
                return;
            }
            try
            {
                SetToCrash(car, false);
                if (!Main.Settings.CrashDamageCarriesOver)
                {
                    foreach (KeyValuePair<CarPartCondition, float> kv in sSavedCondition)
                    {
                        kv.Key.SetCondition(kv.Value);
                    }
                }
                sSavedCondition.Clear();
                bool quali = Game.instance.sessionManager.sessionType == SessionDetails.SessionType.Qualifying;
                float now = Game.instance.sessionManager.GetNormalizedSessionTime();
                float until = quali && Main.Settings.QualiCrashEndsSession
                    ? 2f
                    : now + Main.Settings.CrashRepairShare * Mathf.Lerp(0.7f, 1.3f, Rng.Value01());
                Locks[car] = new Lock { Until = until, Crashed = true, Why = "crash damage" };
                sCrashes++;
                Main.Log(string.Format("{0} CRASH: {1}, {2} — {3}{4}.",
                    sSessionName, RaceLog.DriverName(car), SessionClock(), RepairText(until, quali),
                    Main.Settings.CrashDamageCarriesOver ? "; the damage carries into the race" : "; parts restored afterwards, no race damage"));
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos: crash handling failed. " + e);
            }
        }

        // ------------------------------------------------------------------
        // Releasing a car: repairs done, or the session is over.
        // ------------------------------------------------------------------
        private static void Release(RacingVehicle car, bool logIt)
        {
            Lock l;
            Locks.TryGetValue(car, out l);
            Locks.Remove(car);
            if (l != null && l.SmokeOn)
            {
                SetSmoke(car, false);
            }
            if (car.behaviourManager != null && car.behaviourManager.isCrashed)
            {
                Revive(car);
            }
            if (logIt && l != null)
            {
                Main.Log(string.Format("{0} REPAIRED: {1}, {2} — {3} fixed; the car can go out again.",
                    sSessionName, RaceLog.DriverName(car), SessionClock(), l.Why));
            }
        }

        /// <summary>Puts a crashed car back to a normal car parked in its garage.</summary>
        internal static void Revive(RacingVehicle car)
        {
            try
            {
                SetToCrash(car, false);
                car.behaviourManager.ChangeBehaviour(AIBehaviourStateManager.Behaviour.Racing);
                try
                {
                    car.steeringManager.GetBehaviour<TargetPointSteeringBehaviour>().ClearTarget();
                }
                catch (Exception)
                {
                }
                car.strategy.SetToNoActionRequired();
                if (car.unityVehicle != null)
                {
                    car.unityVehicle.DisableAllEffects();
                    car.unityVehicle.SetPartConditionEffects(CarPartCondition.PartState.Optimal);
                    car.unityVehicle.SetRotationTargetType(UnityVehicle.RotationTarget.ForwardVector);
                }
                car.pathState.ChangeState(PathStateManager.StateType.Garage);
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos: could not reset a crashed car. " + e);
            }
        }

        // ------------------------------------------------------------------
        // Send-out gate, session end, reset.
        // ------------------------------------------------------------------
        public static bool BlockSendOut(RacingVehicle car)
        {
            Lock l;
            if (!sActive || car == null || !Locks.TryGetValue(car, out l))
            {
                return false;
            }
            if (!l.BlockLogged)
            {
                l.BlockLogged = true;
                bool quali = Game.instance.sessionManager.sessionType == SessionDetails.SessionType.Qualifying;
                Main.Log(string.Format("{0} GARAGE: {1} — still being repaired ({2}); {3}.",
                    sSessionName, RaceLog.DriverName(car), l.Why, RepairText(l.Until, quali)));
            }
            return true;
        }

        public static void OnSessionEnd()
        {
            if (!sActive)
            {
                return;
            }
            Main.Log(string.Format("{0} SUMMARY: {1} crash(es) and {2} failure(s) happened (of {3} and {4} lined up).",
                sSessionName, sCrashes, sFailures, sLinedUpCrashes, sLinedUpFailures));
            Reset(false);
        }

        /// <summary>Undo everything this module did. Also run at every session start, in case a
        /// session ended (skip, quit, load) without the end hook.</summary>
        private static void Reset(bool atSessionStart)
        {
            foreach (Pending p in sPending)
            {
                if (p.Armed && p.Car != null)
                {
                    SetToCrash(p.Car, false);
                }
            }
            sPending.Clear();
            foreach (RacingVehicle car in new List<RacingVehicle>(Locks.Keys))
            {
                Release(car, false);
            }
            Locks.Clear();
            sSavedCondition.Clear();
            if (atSessionStart && Game.instance.sessionManager.GetNormalizedSessionTime() < 0.001f)
            {
                // A crashed car left over from a practice/qualifying session that ended without
                // our hook (e.g. a save loaded mid-session) would otherwise be out of the next one.
                try
                {
                    VehicleManager vm = Game.instance.vehicleManager;
                    for (int i = 0; i < vm.vehicleCount; i++)
                    {
                        RacingVehicle car = vm.GetVehicle(i) as RacingVehicle;
                        if (car != null && car.behaviourManager != null && car.behaviourManager.isCrashed)
                        {
                            Revive(car);
                            Main.Log("SESSION START: " + RaceLog.DriverName(car) + " was still marked crashed from an earlier session; reset.");
                        }
                    }
                }
                catch (Exception e)
                {
                    Main.Logger.Error("Session chaos: start-of-session reset failed. " + e.Message);
                }
            }
            sActive = false;
            sCrashes = sFailures = sLinedUpCrashes = sLinedUpFailures = 0;
        }

        // ------------------------------------------------------------------
        // Text helpers.
        // ------------------------------------------------------------------
        private static string SessionClock()
        {
            try
            {
                float left = Game.instance.sessionManager.time;
                return string.Format("{0}:{1:00} left", (int)(left / 60f), (int)(left % 60f));
            }
            catch (Exception)
            {
                return "?";
            }
        }

        internal static string RepairText(float until, bool quali)
        {
            if (until > 1f)
            {
                return quali ? "out for the rest of qualifying" : "out for the rest of the session";
            }
            try
            {
                SessionManager s = Game.instance.sessionManager;
                float seconds = (until - s.GetNormalizedSessionTime()) * s.GetSessionDuration();
                if (seconds >= s.time)
                {
                    return quali ? "out for the rest of qualifying" : "out for the rest of the session";
                }
                return string.Format("about {0}:{1:00} of repairs", (int)(seconds / 60f), (int)(seconds % 60f));
            }
            catch (Exception)
            {
                return "under repair";
            }
        }
    }

    // ----------------------------------------------------------------------
    // Hooks
    // ----------------------------------------------------------------------

    [HarmonyPatch(typeof(CrashDirector), "OnSessionStarting")]
    internal static class SessionChaosStartPatch
    {
        private static void Postfix()
        {
            try
            {
                SessionChaos.OnSessionStart();
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos start failed. " + e);
            }
        }
    }

    [HarmonyPatch(typeof(CrashDirector), "SimulationUpdate")]
    internal static class SessionChaosTickPatch
    {
        private static void Postfix()
        {
            try
            {
                SessionChaos.Tick();
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos tick failed. " + e);
            }
        }
    }

    [HarmonyPatch(typeof(CrashDirector), "OnSessionEnd")]
    internal static class SessionChaosEndPatch
    {
        private static void Prefix()
        {
            try
            {
                SessionChaos.OnSessionEnd();
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos end failed. " + e);
            }
        }
    }

    /// <summary>No safety car, VSC or yellow outside a race: the base game only crashes cars in
    /// races, and its flag logic (laps to go, safety car readiness) assumes one.</summary>
    [HarmonyPatch(typeof(CrashDirector), "OnCrashIncident")]
    internal static class SessionChaosNoFlagPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            try
            {
                return Game.instance.sessionManager.sessionType == SessionDetails.SessionType.Race;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(AICrashedBehaviour), "OnEnter")]
    internal static class SessionChaosCrashedPatch
    {
        private static void Prefix(AICrashedBehaviour __instance)
        {
            try
            {
                SessionChaos.BeforeCrashed(Traverse.Create(__instance).Field("mRacingVehicle").GetValue<RacingVehicle>());
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos (crash, before) failed. " + e.Message);
            }
        }

        private static void Postfix(AICrashedBehaviour __instance)
        {
            try
            {
                if (Game.instance.sessionManager.sessionType != SessionDetails.SessionType.Race)
                {
                    SessionChaos.AfterCrashed(Traverse.Create(__instance).Field("mRacingVehicle").GetValue<RacingVehicle>());
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error("Session chaos (crash, after) failed. " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(SessionStrategy), "SendOutOnTrack")]
    internal static class SessionChaosSendOutPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SessionStrategy __instance)
        {
            try
            {
                RacingVehicle car = Traverse.Create(__instance).Field("mVehicle").GetValue<RacingVehicle>();
                return !SessionChaos.BlockSendOut(car);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    /// <summary>While a car is under repair, its garage doesn't try to send it out. Without this the
    /// garage logic re-runs its run prep (tyres, fuel, trim — and Strategy Director's run planning)
    /// every tick until the send-out succeeds.</summary>
    [HarmonyPatch(typeof(GaragePathState), "SimulationUpdate")]
    internal static class SessionChaosGaragePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(GaragePathState __instance)
        {
            try
            {
                RacingVehicle car = __instance.racingVehicle;
                return car == null || !SessionChaos.Active || !SessionChaos.Locks.ContainsKey(car);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    /// <summary>A small banner while one of your cars is sidelined.</summary>
    internal class SessionChaosOverlay : MonoBehaviour
    {
        private GUIStyle mStyle;

        private void OnGUI()
        {
            try
            {
                if (!Main.Enabled || !SessionChaos.Active || SessionChaos.Locks.Count == 0)
                {
                    return;
                }
                bool quali = Game.instance.sessionManager.sessionType == SessionDetails.SessionType.Qualifying;
                string text = null;
                foreach (KeyValuePair<RacingVehicle, SessionChaos.Lock> kv in SessionChaos.Locks)
                {
                    if (kv.Key == null || !kv.Key.isPlayerDriver)
                    {
                        continue;
                    }
                    string line = string.Format("{0}: {1} — {2}", RaceLog.DriverName(kv.Key), kv.Value.Why, SessionChaos.RepairText(kv.Value.Until, quali));
                    text = text == null ? line : text + "\n" + line;
                }
                if (text == null)
                {
                    return;
                }
                if (mStyle == null)
                {
                    mStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                    mStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
                }
                float w = 520f;
                GUI.Box(new Rect((Screen.width - w) / 2f, 70f, w, 30f + 20f * (text.Split('\n').Length - 1)), "CHAOS  " + text, mStyle);
            }
            catch (Exception)
            {
            }
        }
    }
}
