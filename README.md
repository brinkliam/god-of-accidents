# God of Accidents

*The smallest accident can change the fate of a civilisation.*

A 3D god game built in Unity 6 (URP). You are an invisible force watching a living miniature village, Thistlewick Vale. You can't command anyone. You can nudge a pebble, bend the wind, put a glimmer in someone's eye, or send a crow, and then watch the consequences unfold across decades. Every consequence is recorded in a real causal ledger that you can inspect, replay and branch.

## Run it

- **Editor:** open the project with Unity **6000.6.9f1**. Open `Assets/Scenes/Main.unity` and press Play.
  - First time only: run **God of Accidents ▸ Setup Project** from the menu. It creates the scene, the UI panel settings and the renderer settings.
- **Web build:** **God of Accidents ▸ Build Web** writes to `Builds/Web`. Serve that folder over HTTP.
- **Windows build and benchmark:** **God of Accidents ▸ Build Windows** writes `Builds/Windows/GodOfAccidents.exe`. Run it with `-goaBenchmark` to write frame-time stats and screenshots to the persistent data folder.
- **Tests:** `unity test . --mode EditMode`. That is 36 tests covering determinism, causality, challenges, save/load, ads, purchases and analytics.

## Controls

| | Desktop | Touch |
|---|---|---|
| Pan | left-drag, WASD | one-finger drag |
| Orbit | right/middle-drag, Q/E | two-finger twist |
| Zoom | wheel, R/F | pinch |
| Powers | 1 Nudge · 2 Gust · 3 Distract · 4 Drop | power bar |
| Time | Space pause, F1–F6 speeds | time bar |
| Timeline of causes | T | timeline button |
| Inspect | click a villager, building or object | tap |

## What's in this build (Milestone 1 vertical slice)

- **A living valley:** about 33 named villagers with traits, families, jobs, daily routines, memories and relationships. The economy, seasons, weather, births, deaths, courtships, elections, inventions and eras all run on their own.
- **Four indirect powers:** Nudge, Gust, Distract and Drop. Each has its own gesture, cost and failure cases.
- **Six-plus genuine causal chains,** each verified by tests:
  - a pebble trips the merchant, his cargo spills, a curious child finds it and an inventor is born
  - a gust carries forge embers, a barn or granary burns, food runs short and a merchant gets rich
  - a glimmer makes strangers meet, which leads to courtship, marriage and children
  - the crow drops a lens, coin or key, with outcomes that depend on who finds it
  - the tavern lantern falls, and either someone becomes a hero or the tavern burns
  - the runaway cart smashes the well, and the whole village walks to the river
- **History tools:** the chronicle, an inspector showing "how it came to pass", glowing chain trails in the world, an interactive web-of-history graph, replays, branching timelines, and comparison against "a world where you never acted".
- **Challenges:** *The Forgotten Genius* and *One Pebble*. The sandbox has any seed, plus a codex of discovered consequence types.
- **Monetization foundation:** a central ad manager with a mock provider, all six rewarded placements, two interstitial break points, the ad-free purchase on a test store, and consent-aware analytics. No real ads, no real money. See `docs/MONETIZATION.md`.
- **Fully procedural, original assets:** stylized shaders, terrain, river, waterfall and lake, forests, buildings that change through five architectural tiers, villagers, day/night, seasons, snow, weather, lightning, fire and smoke. All audio is synthesised at startup.

The full design is in `docs/ARCHITECTURE.md`. Progress, verification evidence and known issues are in `docs/DEVLOG.md`.
