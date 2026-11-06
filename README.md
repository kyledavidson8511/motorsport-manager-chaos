# Chaos — More Crashes & Failures for Motorsport Manager

Chaos makes incidents and mechanical failures more varied, especially early in a season while parts are unproven. All behavior is configurable from Unity Mod Manager.

## Install

1. Install Unity Mod Manager for Motorsport Manager.
2. Download `ChaosMod-1.1.0.zip` from the GitHub Release.
3. Extract the `ChaosMod` folder to `Steam/steamapps/common/Motorsport Manager/Mods/`.
4. Start the game and confirm **Chaos — More Crashes & Failures** is active in UMM.

## Practice and qualifying (1.1.0)

The base game has no crashes or mechanical failures outside races. Chaos 1.1 adds them:

- **Crashes** use the game's own crash animation. The car then sits out a repair window, about half the session by default. A qualifying crash ends that car's session.
- **Failures** hit a part (weighted toward unreliable ones). The car smokes and returns to the garage, then can't go out until repairs are done (about a fifth of the session by default).
- Chances are per car per session and follow the same season curve: higher early in the season, settling later. Rain raises crash risk.
- No safety car, VSC or yellow flag outside races. Crash damage is reset right after the crash, so the race car is untouched (a toggle lets the damage carry over instead).
- A banner shows when one of your cars is under repair.
- Simulated (skipped) sessions aren't affected.

Everything is in the UMM panel under **Practice & qualifying**.

## Scope

The mod writes event-level race logs for crashes, safety cars, failures, and race summaries. It does not modify game files.

## Build from source

`src/chaos-mod/` targets .NET 3.5 and requires locally supplied Motorsport Manager managed assemblies, Unity Mod Manager, and Harmony. Proprietary game assemblies are intentionally excluded.
