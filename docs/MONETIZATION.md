# Monetization

Status: **Phase 1 (foundation) and Phase 2 (gameplay integration with mock ads) are done.** No live ads, no real purchases, and no ad or store accounts touched.

## Design

```
gameplay / UI ──► AdManager ──► IAdProvider ──► MockAdProvider (editor, browser)
                     │                          AdMob / LevelPlay provider (device, not yet written)
                     ├─ EntitlementStore ◄── IPurchaseProvider ◄── TestPurchaseProvider | Unity IAP (device, not yet written)
                     ├─ AnalyticsService (consent-gated) ──► sinks (local JSONL file now)
                     └─ IGameplayGate (critical moment? online?)
```

Gameplay code only ever names a **placement** (`Placements.*`); it never calls an SDK.

### Rewarded placements (voluntary)

| Placement | Reward | Default limits |
|---|---|---|
| `rw_glimpse_future` | foresee 2 years of consequences before acting | 60 s cooldown, 6 per session, 15 per day |
| `rw_extra_energy` | +3 divine energy (recorded as a timeline input, so replays stay exact) | 120 s, 4 per session, 10 per day |
| `rw_second_chance` | undo your last intervention after a failed challenge | 30 s, 3 per session, 8 per day |
| `rw_historical_insight` | full cause web plus a counterfactual check | 60 s, 6 per session, 15 per day |
| `rw_bonus_world` | unlocks "The Restless Year" for the session | 600 s, 1 per session, 2 per day |
| `rw_timeline_experiment` | one extra alternate timeline slot | 120 s, 3 per session, 8 per day |

A reward is granted **only** on `Completed` with `rewardEarned`, and only once per show token. Duplicate SDK callbacks, skips, failures, no-fill and offline are all handled and covered by tests.

### Interstitials (natural breaks only)

These fire after closing a challenge results card and when returning to the main menu. They are blocked during critical moments: an active fire, a rolling cart, a crow in flight, a recent player-caused chain, a cinematic, a replay, or a power gesture in progress. They are also blocked in the first 180 s of a session, back-to-back without gameplay in between, beyond 3 per session, within 240 s of each other, and for anyone who owns `goa_remove_ads`. All values live in `AdConfig.Default()`. Tune them with real retention data.

### Ad-free purchase

The product is `goa_remove_ads`, non-consumable. Entitlements can only come from a *validated* receipt. Test-store receipts are rejected unless the store runs in test mode, so a development unlock cannot leak into production. Restore is supported.

### Analytics

Nothing is recorded without consent, and the consent dialog appears on first launch. Each launch gets a random session id and no personal data is collected.

Events tracked:
- session start and end, and daily active
- world start and load, power used, challenge won/failed
- codex unlocks, branches and replays
- ad offers, starts, completions, skips and unavailability
- interstitial impressions and skips
- estimated revenue per placement (mock eCPM)
- IAP start and result

Events are written to `goa_analytics.jsonl` in the persistent data path. An internal dashboard is under Settings ▸ Developer ▸ Analytics dashboard.

## Phase 3: going native (needs your accounts)

1. **Create accounts and apps:** Google AdMob (Android and iOS apps), Google Play Console, Apple Developer. *This is your step; I won't create accounts or change them.*
2. **Install the plugin:** Google Mobile Ads Unity plugin (official, `googleads-mobile-unity` GitHub releases), or Unity LevelPlay if you want mediation through ironSource.
3. Implement `AdMobProvider : IAdProvider`:
   - map placements to ad unit ids from a config asset that is not committed;
   - use **Google's test ad unit ids** until release;
   - grant rewards only from `OnUserEarnedReward`;
   - forward paid events into `AnalyticsService.TrackRevenue` for impression-level revenue.
4. **Consent:** use Google UMP (User Messaging Platform) for GDPR/US states, and pass the result plus `GameSettings.PersonalizedAds`. On iOS, request ATT before personalised ads.
5. **Audience:** the game is family-friendly but not child-directed. If you target under-13s or Google Play Families, set `tagForChildDirectedTreatment` / max ad content rating G and use certified networks only.
6. **Purchases:** implement `UnityIapProvider : IPurchaseProvider` with Unity IAP. Validate receipts (Unity's local validator at minimum; a server is better) and set `EntitlementStore(testMode: false)` in release builds.
7. **Store compliance:** privacy policy URL, the Play Data safety form, Apple privacy nutrition labels, and age ratings (IARC).

## Phase 5

Only after you provide production ad unit ids and explicitly approve: switch off test mode, build, and submit. Nothing is published or activated without your go-ahead.
