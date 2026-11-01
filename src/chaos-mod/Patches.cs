using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace ChaosMod
{
    internal static class Rng
    {
        private static readonly System.Random sRandom = new System.Random();

        public static float Value01()
        {
            return (float)sRandom.NextDouble();
        }

        public static int Range(int minInclusive, int maxExclusive)
        {
            return sRandom.Next(minInclusive, maxExclusive);
        }

        /// <summary>Knuth's Poisson sampler; fine for the small means used here.</summary>
        public static int Poisson(float mean)
        {
            if (mean <= 0f)
            {
                return 0;
            }
            double limit = Math.Exp(-mean);
            double product = sRandom.NextDouble();
            int count = 0;
            while (product > limit)
            {
                count++;
                product *= sRandom.NextDouble();
            }
            return count;
        }
    }

    // ----------------------------------------------------------------------
    // 1. Crashes: replace the per-race crash budget.
    //
    // Base game (CrashDirector.OnSessionStarting) rolls 0-5 crashes with a 50%
    // chance of zero (mean ~0.87), +0-1 in heavy rain, and drops each crash into
    // one of 10 time chunks. We keep the chunk mechanism and the rain bonus and
    // replace only the count, drawn from a Poisson around the season-curve mean.
    // Every other gate in the game still applies (green flag, lap > 1, 3+ laps to
    // go, safety car ready, the car's own crash-readiness), so the real number of
    // crashes can come in under the budget.
    // ----------------------------------------------------------------------
    [HarmonyPatch(typeof(CrashDirector), "OnSessionStarting")]
    internal static class CrashBudgetPatch
    {
        private static void Postfix(CrashDirector __instance)
        {
            if (!Main.Enabled)
            {
                return;
            }
            try
            {
                SessionManager session = Game.instance.sessionManager;
                Settings s = Main.Settings;

                float mean = Main.SeasonValue(s.CrashesPerRaceRound1, s.CrashesPerRaceSettled);
                int crashes = Rng.Poisson(mean);
                if (session.currentSessionWeather.GetAverageWeather().rainType >= Weather.RainType.Heavy)
                {
                    crashes += Rng.Range(0, 2);
                }
                crashes = Mathf.Clamp(crashes, 0, Math.Max(1, s.MaxCrashesPerRace));

                Traverse chunksField = Traverse.Create(__instance).Field("mCrashChunks");
                IList chunks = chunksField.GetValue() as IList;
                if (chunks == null || chunks.Count == 0)
                {
                    return;
                }
                bool isRace = session.sessionType == SessionDetails.SessionType.Race;

                // The game can call OnSessionStarting twice for the same race (seen in
                // the first playtest). Keep the first budget rather than re-rolling, so
                // the race gets one budget and the log shows one line.
                if (isRace && RaceLog.BudgetRolledThisSession && RaceLog.BudgetChunkCounts != null
                    && RaceLog.BudgetChunkCounts.Length == chunks.Count)
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        Traverse.Create(chunks[i]).Field("crashCount").SetValue(RaceLog.BudgetChunkCounts[i]);
                    }
                    return;
                }

                int baseGameRoll = 0;
                for (int i = 0; i < chunks.Count; i++)
                {
                    baseGameRoll += Traverse.Create(chunks[i]).Field("crashCount").GetValue<int>();
                    Traverse.Create(chunks[i]).Field("crashCount").SetValue(0);
                }
                // 1.0.1: Chaos only ever adds. If the base game rolled more crashes than our
                // budget, keep the base game's number.
                bool raised = false;
                if (s.NeverBelowBaseGame && crashes < baseGameRoll)
                {
                    crashes = baseGameRoll;
                    raised = true;
                }
                int[] counts = new int[chunks.Count];
                for (int c = 0; c < crashes; c++)
                {
                    counts[Rng.Range(0, chunks.Count)]++;
                }
                for (int i = 0; i < chunks.Count; i++)
                {
                    Traverse.Create(chunks[i]).Field("crashCount").SetValue(counts[i]);
                }

                if (isRace)
                {
                    RaceLog.ResetSession();
                    RaceLog.BudgetRolledThisSession = true;
                    RaceLog.BudgetChunkCounts = counts;
                    RaceLog.Budget = crashes;
                    RaceLog.BaseGameRoll = baseGameRoll;
                    Main.Log(string.Format("RACE START: crash budget {0} (base game rolled {1}" + (raised ? ", raised to match it" : "") + "); season {2:0%}, mean {3:0.0}, random-failure chance {4:0.0%} per car.",
                        crashes, baseGameRoll, Main.SeasonProgress(), mean,
                        Main.SeasonValue(s.RandomFailureChanceRound1, s.RandomFailureChanceSettled)));
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error("Crash budget patch failed; game default kept. " + e);
            }
        }
    }

    // ----------------------------------------------------------------------
    // 2. Random mechanical failures.
    //
    // Base game: a part only fails when its condition reaches 0. On each wear
    // tick we roll a small chance that a still-healthy part lets go, weighted
    // toward low-reliability parts. When the roll hits, we make *this tick's*
    // wear take the part to zero, so the game's own failure path runs
    // unchanged: RetireDirector decides limp-home vs retirement, commentary,
    // radio, slow-down, and pit repair all behave normally.
    // ----------------------------------------------------------------------
    [HarmonyPatch(typeof(CarPartCondition), "DecrementCondition")]
    internal static class RandomFailurePatch
    {
        [ThreadStatic] internal static bool ForceFailThisTick;

        private static void Prefix(CarPartCondition __instance, RacingVehicle inVehicle, out CarPartCondition.PartState __state)
        {
            __state = __instance.partState;
            ForceFailThisTick = false;
            if (!Main.Enabled || inVehicle == null)
            {
                return;
            }
            try
            {
                if (__instance.partState != CarPartCondition.PartState.Optimal || __instance.condition <= 0f)
                {
                    return;
                }

                float perRace = Main.SeasonValue(Main.Settings.RandomFailureChanceRound1, Main.Settings.RandomFailureChanceSettled);
                if (perRace <= 0f)
                {
                    return;
                }

                float ticks = DesignDataManager.instance.GetDesignData().carPerformance.carCondition.carConditionTicksPerSession;
                CarPart[] parts = inVehicle.car.seriesCurrentParts;
                if (ticks <= 0f || parts == null || parts.Length == 0)
                {
                    return;
                }

                // Weight each part by how unreliable it is, normalised so the car's
                // total per-race chance still matches the setting.
                float weightSum = 0f;
                float myWeight = 1f;
                for (int i = 0; i < parts.Length; i++)
                {
                    float w = Weight(parts[i]);
                    weightSum += w;
                    if (parts[i].partCondition == __instance)
                    {
                        myWeight = w;
                    }
                }
                float averageWeight = weightSum / parts.Length;

                double perPartTick = 1.0 - Math.Pow(1.0 - Mathf.Clamp(perRace, 0f, 0.99f), 1.0 / (ticks * parts.Length));
                double chance = perPartTick * (myWeight / averageWeight);

                if (Rng.Value01() < chance)
                {
                    ForceFailThisTick = true;
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error("Random failure roll failed. " + e);
            }
        }

        private static void Postfix(CarPartCondition __instance, CarPart.PartType inType, RacingVehicle inVehicle, CarPartCondition.PartState __state)
        {
            bool forced = ForceFailThisTick;
            ForceFailThisTick = false;
            if (!Main.Enabled)
            {
                return;
            }
            CarPartCondition.PartState now = __instance.partState;
            if (__state != CarPartCondition.PartState.Optimal || now == CarPartCondition.PartState.Optimal)
            {
                return;
            }
            bool retired = now == CarPartCondition.PartState.CatastrophicFailure;
            if (forced)
            {
                RaceLog.RandomFailures++;
            }
            else
            {
                RaceLog.WearFailures++;
            }
            if (retired)
            {
                RaceLog.Retirements++;
            }
            Main.Log(string.Format("{0} FAILURE: {1}, {2} — {3}{4}.",
                forced ? "RANDOM" : "WORN-OUT",
                RaceLog.DriverName(inVehicle), RaceLog.LapText(), inType,
                retired ? " — RETIRED" : " (limping, needs a pit repair)"));
        }

        private static float Weight(CarPart part)
        {
            float reliability = part != null ? Mathf.Clamp01(part.reliability) : 1f;
            return 1f + Main.Settings.UnreliabilityWeight * (1f - reliability);
        }
    }

    // ----------------------------------------------------------------------
    // 3. Wear multiplier, and the hook that turns a forced failure into a
    //    "this tick takes the part to zero".
    // ----------------------------------------------------------------------
    [HarmonyPatch(typeof(CarPartCondition), "GetNextConditionLoss")]
    internal static class WearPatch
    {
        private static void Postfix(float inCondition, ref float __result)
        {
            if (!Main.Enabled || __result <= 0f)
            {
                // A zero result means the game says "no wear right now" (bonus,
                // chequered flag, retired). Respect it, forced failure included.
                return;
            }
            if (RandomFailurePatch.ForceFailThisTick)
            {
                __result = inCondition + 1f;
                return;
            }
            __result *= Main.SeasonValue(Main.Settings.WearMultiplierRound1, Main.Settings.WearMultiplierSettled);
        }
    }

    // ----------------------------------------------------------------------
    // 4. Retirements: some limp-home failures become terminal.
    // ----------------------------------------------------------------------
    [HarmonyPatch(typeof(RetireDirector), "GetConditionLossOutcome")]
    internal static class RetirementPatch
    {
        private static void Postfix(ref CarPartCondition.PartState __result)
        {
            if (!Main.Enabled)
            {
                return;
            }
            if (__result == CarPartCondition.PartState.Failure && Rng.Value01() < Main.Settings.ExtraRetirementChance)
            {
                __result = CarPartCondition.PartState.CatastrophicFailure;
            }
        }
    }
}
