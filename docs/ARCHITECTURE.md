# Architecture

## Engine choice: Unity 6 (URP)

| Need | Unity 6 URP | Three.js / R3F | Godot 4 |
|---|---|---|---|
| Stylised 3D quality, shadows, post FX | strong, with custom HLSL | good, but lots of hand-built plumbing | good |
| Android and iOS export | first-class | needs wrappers (Capacitor etc.) | good, weaker iOS tooling |
| AdMob, mediation, platform IAP | official plugins (Google Mobile Ads Unity, LevelPlay, Unity IAP) | web ads only, no AdMob in a browser | community plugins |
| Browser playable | WebGL / WebGPU build | native | web export |
| Tooling on this machine | Unity 6000.6.9f1 + Unity CLI with live-Editor control | Node | none installed |

Unity wins on the commercial requirement that matters most here: native AdMob and store billing on Android and iOS. It also still ships a browser build, and the Unity CLI lets the Editor be driven, tested and screenshotted from the command line.

## Layers

```
GOA.Simulation  (pure C#, no UnityEngine)   — the world, causality, timelines, challenges
GOA.Services    (pure C#, no UnityEngine)   — ads, purchases/entitlements, analytics
GOA.Game        (Unity)                     — rendering, camera, input, UI, audio, bootstrap
GOA.Editor      (Unity Editor)              — project setup and builds
GOA.Tests       (EditMode NUnit)            — simulation + services tests
```

The simulation knows nothing about rendering. The view reads world state each frame and interpolates continuous time. Player input reaches the world only through `GameSession.Act`, then `Timeline.Act`, then `World.Apply`.

## The simulation: a discrete-event world

- **Time** is whole simulated seconds. A day is 86 400 s and a year is 28 days (four 7-day seasons), so a lifetime fits in minutes of play.
- **Event queue:** a binary heap ordered by *(time, insertion sequence)*. Villagers schedule a `Decide` when each activity ends. Walking, encounters, fires, the runaway cart, the crow and the daily tick are all tasks too. Fast-forward simply processes more tasks, so speed never changes outcomes.
- **Layered fidelity:** individuals are event-driven, not updated every frame. Walking positions are analytic along road polylines and rendered by interpolation. Economy, aging, births, deaths, inventions and politics run once per simulated day.
- **Encounters are geometric, not dice:** when someone starts walking, their route is tested against pebbles, dropped items and glimmers. If the route passes close to one, an encounter is scheduled at the moment they arrive. Moving a pebble re-checks everyone currently on the road.

## Context-keyed randomness (the key to honest causality)

Every random decision is `Hash(seed, context…, purpose)`. Examples: weather is `(day)`, a trip is `(citizen, time, pebble)`, a birth is `(day, mother)`. Nothing draws from a shared stream. As a result, an intervention can only change outcomes that actually depend on the state it altered. Unrelated rolls stay identical across timelines, so when two histories differ, the difference is a genuine consequence. The tests check this (`EventsBeforeAnInterventionAreIdenticalAcrossTimelines`).

## The causal ledger

Each `SimEvent` lists the earlier events whose resulting state was read when it was produced. That state is carried explicitly: a pebble's `causeEventId`, a villager's `marks["spark:engineering"]`, a relationship's `causeEventId`, a fire's origin, the active `ScarcityEventId`, and so on. `roots` (the upstream player interventions) and `depth` are derived when the event is recorded. The ledger answers ancestry, descendants and the primary chain queries. Minor happenings are recorded only when they belong to a chain, which keeps the ledger meaningful.

**No fake claims:** a chain exists only if code actually propagated a cause. In addition, *Historical Insight* re-simulates the world without the root intervention and reports whether the event would still have happened.

## Timelines, save/load, replay and branching

A world is exactly `(seed, scenario, ordered inputs)`.

- **Save:** writes the inputs, the current time and a state hash. Load re-simulates and verifies the hash; 30 simulated years take about 7 s.
- **Replay:** rebuilds the world just before an event and plays it back, applying later inputs at their recorded times.
- **Branch:** keeps the inputs before time T and diverges from there.
- **Glimpse of the future:** clones the world, applies a hypothetical input, runs two years with and without it, and lists the events that differ.
- **Compare:** diffs event signatures between any two worlds at the same moment.

## Rendering

All art is generated in code:

- `MeshKit`: faceted low-poly primitives with vertex colours; vertex alpha carries the wind-sway weight
- `TerrainBuilder`: a pure height function plus surface painting for roads, fields, shore, rock and snow
- `Foliage`: GPU instancing in 48 m cells with frustum and distance culling
- `BuildingFactory`: 15 building types and five house tiers
- `CitizenView`: articulated villagers with procedural animation

Custom URP shaders live in `Resources/Shaders`:

- `GOA/Stylized`: wrapped lighting, shadows, point lights, rim light, wind, season tint and snow
- `GOA/Water`: depth colour, foam and glints
- `GOA/Sky`: gradient, sun, moon and stars
- `GOA/Particle`: texture-free particles
- `GOA/Glow`: causal threads and ripples

Post-processing is ACES, bloom, grading, vignette, and a tilt-shift-style depth of field on High quality. The renderer is set to Forward for mobile friendliness.

## Performance notes

- The simulation is event-driven, so 30 simulated years take about 7 s headless.
- Foliage is instanced and culled per cell, and grass is skipped on Low quality.
- Quality presets change shadow distance and cascades, MSAA, render scale and DOF.
- Windows player, Ryzen 3 3250U with integrated graphics at 1280×720: about 79 fps average, 66 fps for the worst 1% (`-goaBenchmark`; see DEVLOG). In the Editor, about 35–45 fps with Game view overhead.
