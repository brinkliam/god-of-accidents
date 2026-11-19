using System;
using System.Collections.Generic;

namespace GOA.Services
{
    public enum AdFormat { Rewarded, Interstitial }

    public enum AdResult { Completed, Skipped, Failed, Unavailable, Blocked }

    /// <summary>Identifiers for every ad opportunity in the game. Gameplay code only ever names a placement.</summary>
    public static class Placements
    {
        public const string GlimpseOfFuture = "rw_glimpse_future";
        public const string ExtraEnergy = "rw_extra_energy";
        public const string SecondChance = "rw_second_chance";
        public const string HistoricalInsight = "rw_historical_insight";
        public const string BonusWorld = "rw_bonus_world";
        public const string TimelineExperiment = "rw_timeline_experiment";
        public const string AfterChallengeResults = "int_after_results";
        public const string ReturnToWorldSelect = "int_world_select";
    }

    [Serializable]
    public class PlacementConfig
    {
        public string id;
        public AdFormat format;
        public bool enabled = true;
        public double cooldownSeconds;        // minimum gap between shows of this placement
        public int maxPerSession;             // 0 = unlimited
        public int maxPerDay;                 // 0 = unlimited
        public string rewardId;               // rewarded only
        public double estimatedEcpmUsd;       // for the internal revenue estimate only
    }

    [Serializable]
    public class AdConfig
    {
        public bool adsEnabled = true;
        public bool testMode = true;          // must stay true in development; production ids come from the account owner
        public double interstitialGlobalCooldownSeconds = 240;
        public double interstitialMinSessionSeconds = 180;   // never right after launch
        public int interstitialMaxPerSession = 3;
        public List<PlacementConfig> placements = new List<PlacementConfig>();

        public PlacementConfig Get(string id)
        {
            foreach (var p in placements) if (p.id == id) return p;
            return null;
        }

        /// <summary>Conservative launch defaults. Tune with real retention and revenue data.</summary>
        public static AdConfig Default()
        {
            var c = new AdConfig();
            void R(string id, string reward, double cd, int perSession, int perDay) =>
                c.placements.Add(new PlacementConfig { id = id, format = AdFormat.Rewarded, rewardId = reward, cooldownSeconds = cd, maxPerSession = perSession, maxPerDay = perDay, estimatedEcpmUsd = 12 });
            R(Placements.GlimpseOfFuture, Rewards.Glimpse, 60, 6, 15);
            R(Placements.ExtraEnergy, Rewards.Energy, 120, 4, 10);
            R(Placements.SecondChance, Rewards.SecondChance, 30, 3, 8);
            R(Placements.HistoricalInsight, Rewards.Insight, 60, 6, 15);
            R(Placements.BonusWorld, Rewards.BonusWorld, 600, 1, 2);
            R(Placements.TimelineExperiment, Rewards.TimelineExperiment, 120, 3, 8);
            c.placements.Add(new PlacementConfig { id = Placements.AfterChallengeResults, format = AdFormat.Interstitial, cooldownSeconds = 300, maxPerSession = 2, estimatedEcpmUsd = 6 });
            c.placements.Add(new PlacementConfig { id = Placements.ReturnToWorldSelect, format = AdFormat.Interstitial, cooldownSeconds = 420, maxPerSession = 1, estimatedEcpmUsd = 6 });
            return c;
        }
    }

    public static class Rewards
    {
        public const string Glimpse = "glimpse";
        public const string Energy = "energy_3";
        public const string SecondChance = "second_chance";
        public const string Insight = "insight";
        public const string BonusWorld = "bonus_world";
        public const string TimelineExperiment = "timeline_experiment";

        public static string Describe(string id) => id switch
        {
            Glimpse => "Glimpse of the Future - preview where an action leads",
            Energy => "+3 Divine Energy",
            SecondChance => "Second Chance - undo your last intervention",
            Insight => "Historical Insight - reveal a hidden causal chain",
            BonusWorld => "Bonus World - The Restless Year",
            TimelineExperiment => "Timeline Experiment - one extra alternate history",
            _ => id
        };
    }

    /// <summary>Wraps a time source so cooldown logic is testable.</summary>
    public interface IClock { double Now { get; } DateTime UtcNow { get; } }

    public sealed class SystemClock : IClock
    {
        readonly DateTime _start = DateTime.UtcNow;
        public double Now => (DateTime.UtcNow - _start).TotalSeconds;
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public sealed class ManualClock : IClock
    {
        public double Now { get; set; }
        public DateTime UtcNow => new DateTime(2026, 1, 1).AddSeconds(Now);
    }

    /// <summary>The thing that actually plays ads. Mock in the editor/browser, AdMob or LevelPlay on device.</summary>
    public interface IAdProvider
    {
        string Name { get; }
        bool IsTestMode { get; }
        void Initialize(Action<bool> done);
        bool IsReady(string placementId, AdFormat format);
        void Load(string placementId, AdFormat format);
        /// <summary>Shows an ad. The callback MUST be invoked exactly once by a well-behaved provider, but the manager
        /// defends against duplicate or missing callbacks anyway. rewardEarned is true only on verified completion.</summary>
        void Show(string placementId, AdFormat format, Action<AdResult, bool, double> onClosed);
    }

    /// <summary>Lets the ad manager ask whether this is a bad moment to interrupt the player.</summary>
    public interface IGameplayGate
    {
        bool IsCriticalMoment { get; }   // active chain reaction, cinematic, replay, or the player mid-gesture
        bool IsOnline { get; }
    }
}
