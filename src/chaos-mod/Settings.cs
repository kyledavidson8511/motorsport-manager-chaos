using UnityModManagerNet;

namespace ChaosMod
{
    public class Settings : UnityModManager.ModSettings
    {
        // ---- Season curve ----
        // Chaos starts at the "round 1" values and eases down to the "settled" values
        // by this fraction of the season (0.5 = halfway through the calendar).
        public float RampEndsAt = 0.5f;

        // ---- Crashes ----
        // Average crashes per race. The base game averages ~0.9 (50% of races have none).
        public float CrashesPerRaceRound1 = 2.2f;
        public float CrashesPerRaceSettled = 1.3f;
        public int MaxCrashesPerRace = 8;
        public bool NeverBelowBaseGame = true;

        // ---- Random mechanical failures ----
        // Chance that any given car suffers a random (non-wear) part failure during a race.
        // The base game is 0%: parts only fail when worn to zero.
        public float RandomFailureChanceRound1 = 0.10f;
        public float RandomFailureChanceSettled = 0.02f;
        // How much more likely unreliable parts are to fail. At 2, a 40%-reliability part
        // is ~2.2x as likely to let go as a 100%-reliability one.
        public float UnreliabilityWeight = 2.0f;

        // ---- Wear ----
        // Multiplier on normal part wear during a race.
        public float WearMultiplierRound1 = 1.25f;
        public float WearMultiplierSettled = 1.0f;

        // ---- Retirements ----
        // Extra chance that a failure is terminal (retirement) rather than limp-home.
        // Added on top of the game's own per-part catastrophic rates.
        public float ExtraRetirementChance = 0.10f;

        // ---- Practice & qualifying (1.1.0) ----
        // The base game has no crashes or failures outside races. Chances are per car per session.
        public bool PracticeQualiChaos = true;
        public float PracticeCrashChanceRound1 = 0.06f;
        public float PracticeCrashChanceSettled = 0.03f;
        public float PracticeFailureChanceRound1 = 0.10f;
        public float PracticeFailureChanceSettled = 0.03f;
        public float QualiCrashChanceRound1 = 0.04f;
        public float QualiCrashChanceSettled = 0.02f;
        public float QualiFailureChanceRound1 = 0.06f;
        public float QualiFailureChanceSettled = 0.02f;
        // Repair time as a share of the session (randomised ±30-40%).
        public float CrashRepairShare = 0.5f;
        public float FailureRepairShare = 0.2f;
        public bool QualiCrashEndsSession = true;
        // Off: crash damage is put back so the race car is untouched. On: the game's
        // crash damage (parts at 0-20%) stays for the race.
        public bool CrashDamageCarriesOver = false;

        // ---- Debug ----
        public bool LogEvents = true;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
