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

## 2026-11-21 - Round 2: readability, layout, the challenge opening, visual moments

Feedback after the first polish pass: about 7/10 visuals, 6/10 gameplay. Goal: 8+ on both. Branch `polish/round-2`.

**1. Readability and aiming.**
- Villagers are chunkier, with a strong rim light for clear silhouettes. They grow with distance from the camera (up to 2.3× when far away), so they stay readable from the god's-eye view.
- Distract and Drop snap to the nearest valid villager within a generous radius (Drop can also land on open ground). A ring and a name label ("Distract Corvin Hale", "Drop beside Wren") show the target before you confirm. On touch it shows while your finger is down, and releasing confirms; a drag is a camera move, never a choice.
- Drag powers: the tap threshold now scales with screen density. A pan with Nudge armed no longer shows a misleading hint.

**2. Small screens.**
- On compact screens, Goals and Chronicle share one drawer with tabs (tap the open tab to tuck it away). It never stacks over the energy panel, and it hides while the top-bar chips are unfolded.
- HUD pills never wrap or clip at 1280 wide: shorter labels ("Food 11d", "Wealth 50%", "Rain soon", "Clearing") with full detail in tooltips.
- The HUD now hides behind every dimmed dialog, not just the Web of History.
- The Web of History button opened normally at both 634 and 1280 wide in this build; I couldn't reproduce the report.

**3. Forgotten Genius opening.**
- After the briefing the camera flies to Wren and opens her card.
- A new first goal, "Catch Wren's eye with something new", is reachable early.
- The headline counts down to Ambrose's afternoon walk.
- When Ambrose is within glimmer reach of Wren, time pauses, the camera follows him, a strong violet marker shows on him and a toast says what to do. This repeats each afternoon until you act.
- Curious villagers now notice a glimmer from 14 paces instead of 10 (the briefing already said curious people notice more). Without that, the moment often didn't happen on day one.
- Test: following the game's own hint within two days starts Wren's apprenticeship and ticks the first goal.

**4. Balance and naming.**
- Hearth sparks can't light a fire in the first three days, and only rarely after (4% plus up to 8% on windy days, was 25-60%). Tested: no natural fires in the first three days across four seeds.
- No two living villagers share a first name, at generation or at birth (deterministic, no extra random draws). Tested across three seeds and 12 years.

**5. Visual moments.**
- Midday: firmer sun, softer fill light, and a grade that adds saturation and contrast and lowers exposure when the sun is high.
- The dramatic moment: when your chain reaches something big (magnitude 3+, or a hero, rescue, fire, spark, apprenticeship or courtship two links deep), the camera swoops low, time slows, cinematic bars slide in and the vignette deepens. It's limited to once every 12 seconds.
- Bigger effects:
  - fires: brighter, wider light and an ember gout when they catch;
  - crashes and collapses: flying debris;
  - a broken well: a burst of water.
- Codex discovered: a gilded card with a shimmer sweep, the discovery count and a golden burst in the world. Turning points and codex reveals queue up and show one at a time.
- The camera's roof clearance is wider and firmer (the first swoop ended beside a wall).

**Verified.**
- EditMode tests: 56/56 (9 new this round).
- Web build in the browser pane at 60 fps, 634×593 and 1280×720 (the pane was in focus this time):
  - the guided opening, snapping, compact drawer and pills;
  - the Web of History button; the HUD hidden behind dialogs.
- New `-goaShowcase` mode in the Windows player plays the round's moments by itself and saves screenshots (kept locally in `Screenshots/round2/`, which git ignores). Seen there:
  - the guided moment on day one at 15:10, leading to apprenticeship, spark, dramatic swoop and codex reveals;
  - the lantern fire put out by a villager (hero codex);
  - the new midday look.
- Dev commands for browser playtests: `unityInstance.SendMessage("GOA", "Dev", "...")`, only when the page URL has `dev=1`.

**Not verified.**
- Phones in the hand: portrait was checked only by emulation in the last round.
- The final Web build after the last fixes: in this session the browser pane was not on screen, so it couldn't run game frames. The same code was checked in the Windows player.

## 2026-11-20 - Polish pass: the first ten minutes

Goal from playtest feedback (rated 4/10): make the first ten minutes exciting and beautiful. Work is on branch `polish/first-ten-minutes`.

**1. Pacing.**
- Ambient mishaps, about seven a day: dropped apples, runaway animals, tipped barrows, arguments, hearth sparks and lucky finds. Each one is an ordinary cause in the ledger, so it can chain into meetings, friendships, rivalries, sparks or fires, and none of them is ever credited to the player. In challenges they stay among adults and drop no gears or books, so the puzzle still needs the player (the Forgotten Genius test caught the first version solving itself).
- Every world opens with a guaranteed small chain. Seen live: "Agnes Thornbury drops a basket of apples" at 07:01, then "Silas Quill and Agnes Thornbury meet", less than a minute of real time after Begin.
- Friendships and rivalries are now Chronicle milestones. Titles use full names.
- Energy: worlds start full and regeneration is tripled. Rewarded ads can't overfill energy, and the button reads "Energy full".
- Opportunity markers flash where a nudge would cause something now: gold for Nudge, violet for Distract. `World.FindOpportunities` is a pure query (tested).

**2. Powers.**
- A single tap now does a Nudge: it rolls the pebble or trinket onto the path of whoever is coming (`SuggestNudgeTarget`; tested to be legal and side-effect free).
- Armed state: a cursor per power, the chosen orb glows and the others dim, valid targets get rings and invalid ones grey rings.
- Hit radii are larger and grow with screen size.
- Feedback: an impact effect per power, camera shake, slow motion while your chain reaction unfolds, a "cause and effect" card when a chain settles, and a bigger codex unlock moment.

**3. Mobile and UI.**
- New WebGL template: the canvas fills the window and follows resize, rotation and fullscreen changes. The fullscreen button calls the browser API directly on the first click. The template also adds a themed loader and saves that persist across visits.
- Compact top bar on narrow screens (tap the clock to unfold the other chips) and a portrait layout scaled for phones. Targets are at least 44px, and text is larger with higher contrast.
- Fixed: HUD showing through the Web of History; the Codex title collapsing in long dialogs; energy above the cap after an ad; the weather label (it now follows the visible sky); duplicate first names. The Codex shows a hint for every undiscovered entry. Locked power slots are hidden until those powers exist.

**4. Visuals.**
- More chimney smoke, sparrows flitting over the green, villagers carrying sacks, baskets and logs, footstep dust, stronger crop sway, and water splashes when fires are put out.
- Colour grading follows the season and the hour: warm golden hours, blue nights, rich autumn, crisp winter. Banners and cards pop in.
- The Web build defaults to Medium quality. SMAA is used only on Medium (none on Low, MSAA on High).

**6. Fixes from review.**
- Presses on UI never reach the world.
- The camera lifts over roofs and holds still while paused.
- Sandbox worlds get optional goals ("First steps") with a toast for each one reached.
- Fast-forward runs on a per-frame time budget. Load, branch, replay and Glimpse show a "folding time" overlay while history is re-simulated.
- The WebGL heap starts at 256 MB and grows to 2 GB (was 96 MB).
- Removed SampleScene, the unused Festival event, the no-op ChallengeCheck task and the unused bonus PlayerPref.

**Verified.**
- EditMode tests: 47/47 (11 new).
- Web build played in the browser pane:
  - opening chain; full names in the Chronicle;
  - tap Nudge (twice); Distract leading to a meeting; the chain card and codex fanfare together;
  - goals ticking with toasts; armed rings; opportunity markers;
  - Codex hints and title; the Web of History without the HUD;
  - compact layout and portrait at 375×812; no console errors.
- Found and fixed during the playtest:
  - no opportunity markers in the opening minutes (rules widened, now tested);
  - Chronicle rows overlapping in compact mode;
  - the chain card overlapping the codex banner;
  - toasts covering the top bar;
  - unreadably small UI in portrait;
  - the fullscreen button covering an orb on phones;
  - mishaps in the wrong timeline lane.

**Not verified.**
- Real fullscreen: the button's request fires on the first click, but the desktop app's browser pane never grants fullscreen.
- Web frame rate: during this session the pane was throttled to about 1 fps even for an empty page (window not focused). The old live build measured the same, so it's the environment, not the build.
- The camera-over-roofs and paused-camera fixes are in code but haven't been seen in a screenshot.
- Midday still looks pale; the Milestone 1 noon capture is the same, so this isn't a regression, but it's worth a look later.

**Known limitations / next.**

- The turning-point banner fix (frame-driven fade) has not yet been confirmed in a screenshot. The Editor session closed before the capture.
- Browser (WebGL) frame rate is still unmeasured: it needs the page in the foreground. The Web build renders and plays.
- The "Mobile" quality level (used on WebGL) gets fewer post effects; depth of field is off there by design.
- Not built yet (later milestones):
  - advanced powers (Delay, Redirect and others), hidden until they exist
  - a world map with 2-3 locations reusing the same powers, each with a signature accident (step 5 of the polish brief, deferred by request)
  - multiple settlements, trade and war
  - eras beyond the procedural house tiers
  - daily and weekly seeded challenges
  - native AdMob, Unity IAP and UMP integrations (need your accounts; see MONETIZATION.md)
  - Android and iOS builds; iOS needs a Mac
- Art is procedural low-poly by design, not placeholders, but there is room for richer detail: roof textures in geometry, more building variety, villager faces and clothing.
