# Development log

## 2026-11-19 — Milestone 1: vertical slice

**Setup.** No earlier God of Accidents project existed anywhere on the machine, so there was nothing to delete. A fresh Unity 6000.6.9f1 URP project was created with the Unity CLI (`com.unity.pipeline` for live-Editor control, plus the test framework).

**Built.**

- **Simulation** (`Assets/GodOfAccidents/Simulation`):
  - discrete-event world with context-keyed determinism and a causal ledger
  - villagers with routines, traits, memories and relationships
  - accidents: trips, spilled cargo, found items, fire and embers, the lantern, the runaway cart, the crow
  - economy, seasons and weather, births, deaths, courtship and marriage, coming of age
  - a seven-invention tech tree, elections and policies, five eras
  - four powers, two challenges plus a sandbox and a bonus world
  - timelines with save/load, replay, branching, comparison and foresight
- **Services:** ad manager with a mock provider, entitlements on a test store, consent-aware analytics.
- **Game** (Unity):
  - procedural terrain, river, waterfall and lake; forests and meadows
  - 15 building types with five house tiers; villagers, animals and crops
  - day/night, seasons, snow, rain and storms, lightning, clouds, fire and smoke
  - stylised URP shaders and post FX
  - orbit/pan/zoom camera for mouse and touch; power gestures
  - full UI: menu, HUD, inspector, chronicle, causal trails, timeline graph, replay, branches, codex, settings, consent, results, glimpse, mock ad overlay
  - synthesised audio and adaptive music

**Verified (evidence, not assumptions).**

- **EditMode tests: 36/36 pass.** They cover:
  - determinism across seeds, chunked vs single advance, and live play vs replay
  - save/load hash and continuation; branching
  - causes always precede effects, and roots are always interventions; no player-caused claims without input
  - pebble → merchant trip → cargo spill, with a counterfactual showing no trip without the pebble
  - gust → forge embers → fire; cart → broken well → repair
  - the Forgotten Genius baseline fails, and the challenge is winnable with one Distract (3 stars)
  - plausible 30-year population and economy
  - ad rules: duplicate callbacks, skip/fail/no-fill/offline, cooldowns and caps, critical-moment and early-session blocks, no consecutive interstitials, ad-free entitlement
  - receipt validation, restore, analytics consent
- **In the running Editor** (Play mode with frames confirmed advancing, screenshots in `Screenshots/`, no game errors in the console):
  - the menu renders over the live valley
  - noon, golden hour, aerial and night views
  - distracting Ambrose near the green causes meetings, the apprenticeship and Wren's spark, live in the HUD and chronicle
  - fast-forwarding about 6 years, Wren invents the Waterwheel; the challenge resolves *Won ★★★* and the results card shows the real causal chain; the village enters the Village Age

**Bugs found by looking at real frames, then fixed.**

- Houses had 2 submeshes but 3 materials, so Unity re-drew the windows with the forge "hot" material and they glowed at noon.
- The water shader had no camera depth texture, so foam covered the whole river.
- The image was over-exposed; the grade was rebalanced.
- Rain was anchored at the world origin instead of the camera.
- The tinkerer's afternoon walk never happened because his work block ran straight to 18:00. The challenge test caught this.
- UI fades used the UI Toolkit scheduler, which fired all at once when the Editor was driven externally. Fades are now frame-driven.

**Web build (WebGL), played in a browser.**

- The first two builds failed. Compilation and then the Emscripten link ran out of memory on this 10 GB machine: "GC heap initialization failed", then the linker exited with no message.
- `BuildWeb` now uses build-time code optimisation, Medium managed stripping, engine stripping and size-optimised IL2CPP. With these settings the build succeeds (33 MB uncompressed, wasm 27 MB).
- Served from `localhost` and played in a Chromium-based browser:
  - consent, the menu, the Challenges list, the Forgotten Genius briefing, the HUD, time controls and the building inspector all work;
  - no console errors.
- Bug found there: General Punctuation glyphs (— – … " " • →) are missing from the runtime font in the web player and rendered as blanks. User-facing strings now use ASCII and Latin-1 equivalents.
- Browser frame rate could not be measured honestly: the browser pane was in the background, where the page is throttled (about 2 fps).

**Player builds: two black-screen bugs found and fixed.**

1. **SSAO.** The template's SSAO renderer feature lost its shader resources in the player build, so URP failed to create its pipeline on every frame. The log filled with 15k exceptions and the first benchmark numbers (about 357 fps) were measuring empty frames. `ProjectSetup` now removes the SSAO feature; the game never used it.
2. **Shader stripping.** URP's scriptable stripping removed every UberPost (post-processing) variant whenever a build ran while the *other* platform was the active target, giving a black world with stale UI. Fixes:
   - both build methods now switch the active build target first;
   - "strip unused post-processing variants" is off, because our Volume profile is created at runtime and the stripper can't see it.

   After the fix, UberPost keeps 720 variants on WebGL (it had 0).

**Player performance (Windows build, measured).** `-goaBenchmark` starts the Forgotten Genius, holds four camera shots (village, close, aerial, fast-forward at "Days" speed) with vSync off, writes frame-time stats to `goa_benchmark.txt` plus a screenshot per shot, and quits. Copies are in `Screenshots/player/`.

| Setup | Average | Median frame | Worst 1% |
|---|---|---|---|
| Ryzen 3 3250U, integrated Radeon, D3D11, 1280×720, "PC" quality | **79 fps** | 12.7 ms | 15.2 ms (66 fps) |

That is a low-end 2019 laptop chip. A shorter 20 s run gave 179 fps average, so expect thermal and power variance on laptops.

**Hosting.** Code is public at https://github.com/brinkliam/god-of-accidents. The Web build is live at https://god-of-accidents.vercel.app:
- it's built with Brotli (35 MB down to 9.5 MB) and committed to `web/`;
- Vercel serves `web/` with no build step and redeploys on every push to `main`;
- `web/vercel.json` sends `Content-Encoding: br`, and the loader's decompression fallback covers hosts that don't;
- checked live: the right headers, Unity starts on WebGL 2, and the consent screen shows over the valley.

To update the site, run `BuildWeb`, copy `Builds/Web` into `web/`, then commit and push.

**Known limitations / next.**

- The turning-point banner fix (frame-driven fade) has not yet been confirmed in a screenshot. The Editor session closed before the capture.
- Browser (WebGL) frame rate is still unmeasured: it needs the page in the foreground. The Web build renders and plays.
- The camera rig has no collision with buildings. A low close-up can sit inside a roof.
- The "Mobile" quality level (used on WebGL) gets fewer post effects; depth of field is off there by design.
- Not built yet (later milestones):
  - advanced powers (Delay, Redirect and others), shown as locked
  - multiple settlements, trade and war
  - eras beyond the procedural house tiers
  - daily and weekly seeded challenges
  - native AdMob, Unity IAP and UMP integrations (need your accounts; see MONETIZATION.md)
  - Android and iOS builds; iOS needs a Mac
- Art is procedural low-poly by design, not placeholders, but there is room for richer detail: roof textures in geometry, more building variety, villager faces and clothing.
