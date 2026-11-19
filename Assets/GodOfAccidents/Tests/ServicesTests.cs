using GOA.Services;
using NUnit.Framework;

namespace GOA.Tests
{
    class FakeGate : IGameplayGate
    {
        public bool IsCriticalMoment { get; set; }
        public bool IsOnline { get; set; } = true;
    }

    public class AdManagerTests
    {
        ManualClock _clock;
        MockAdProvider _ads;
        EntitlementStore _ent;
        AnalyticsService _an;
        FakeGate _gate;
        AdManager _mgr;

        [SetUp]
        public void Setup()
        {
            _clock = new ManualClock { Now = 1000 };
            _ads = new MockAdProvider();
            _an = new AnalyticsService(_clock, true, "test");
            _ent = new EntitlementStore(true, _an);
            _gate = new FakeGate();
            _mgr = new AdManager(AdConfig.Default(), _ads, _clock, _ent, _an, _gate);
            _mgr.Initialize();
        }

        [Test]
        public void RewardGrantedExactlyOnceEvenWithDuplicateCallbacks()
        {
            _ads.DuplicateCallbacks = true;
            int grants = 0;
            _mgr.ShowRewarded(Placements.ExtraEnergy, g => grants++);
            Assert.AreEqual(1, grants);
            Assert.AreEqual(1, _an.Count("ad_duplicate_callback"));
        }

        [Test]
        public void NoRewardWhenSkippedOrFailed()
        {
            int grants = 0;
            _ads.SkipNextShow = true;
            _mgr.ShowRewarded(Placements.ExtraEnergy, g => grants++);
            _clock.Now += 1000;
            _ads.FailNextShow = true;
            _mgr.ShowRewarded(Placements.ExtraEnergy, g => grants++);
            Assert.AreEqual(0, grants);
        }

        [Test]
        public void RewardedCooldownAndDailyCapsApply()
        {
            int grants = 0;
            _mgr.ShowRewarded(Placements.ExtraEnergy, g => grants++);
            AdResult last = AdResult.Completed;
            _mgr.ShowRewarded(Placements.ExtraEnergy, g => grants++, (r, _) => last = r);
            Assert.AreEqual(1, grants, "second request inside the cooldown must not grant");
            Assert.AreEqual(AdResult.Blocked, last);
            for (int i = 0; i < 20; i++) { _clock.Now += 200; _mgr.ShowRewarded(Placements.ExtraEnergy, g => grants++); }
            Assert.AreEqual(4, grants, "session cap of 4 for extra energy");
        }

        [Test]
        public void OfflineMeansUnavailableAndNoReward()
        {
            _gate.IsOnline = false;
            AdResult res = AdResult.Completed; int grants = 0;
            _mgr.ShowRewarded(Placements.GlimpseOfFuture, g => grants++, (r, _) => res = r);
            Assert.AreEqual(AdResult.Unavailable, res);
            Assert.AreEqual(0, grants);
        }

        [Test]
        public void NoFillIsHandledGracefully()
        {
            _ads.Fill = false;
            var fresh = new AdManager(AdConfig.Default(), _ads, _clock, _ent, _an, _gate);
            fresh.Initialize();
            AdResult res = AdResult.Completed;
            fresh.ShowRewarded(Placements.GlimpseOfFuture, g => Assert.Fail("no reward without an ad"), (r, _) => res = r);
            Assert.AreEqual(AdResult.Unavailable, res);
        }

        [Test]
        public void InterstitialNeverDuringCriticalMoments()
        {
            _gate.IsCriticalMoment = true;
            Assert.IsFalse(_mgr.TryShowInterstitial(Placements.AfterChallengeResults));
            _gate.IsCriticalMoment = false;
            Assert.IsTrue(_mgr.TryShowInterstitial(Placements.AfterChallengeResults));
        }

        [Test]
        public void InterstitialNeverRightAfterLaunch()
        {
            var clock = new ManualClock { Now = 5 };
            var m = new AdManager(AdConfig.Default(), new MockAdProvider(), clock, _ent, _an, _gate);
            m.Initialize();
            Assert.IsFalse(m.TryShowInterstitial(Placements.AfterChallengeResults));
            clock.Now = 400;
            Assert.IsTrue(m.TryShowInterstitial(Placements.AfterChallengeResults));
        }

        [Test]
        public void NoConsecutiveInterstitials()
        {
            Assert.IsTrue(_mgr.TryShowInterstitial(Placements.AfterChallengeResults));
            _clock.Now += 2000;
            Assert.IsFalse(_mgr.TryShowInterstitial(Placements.ReturnToWorldSelect), "needs gameplay in between");
            _mgr.NotifyGameplay();
            Assert.IsTrue(_mgr.TryShowInterstitial(Placements.ReturnToWorldSelect));
        }

        [Test]
        public void AdFreeBlocksInterstitialsButNotRewardedAds()
        {
            var store = new TestPurchaseProvider();
            store.Purchase(Products.RemoveAds, (r, receipt) => _ent.Accept(receipt));
            Assert.IsTrue(_ent.Has(Products.RemoveAds));
            Assert.IsFalse(_mgr.TryShowInterstitial(Placements.AfterChallengeResults));
            int grants = 0;
            _mgr.ShowRewarded(Placements.HistoricalInsight, g => grants++);
            Assert.AreEqual(1, grants);
        }

        [Test]
        public void TestReceiptsAreRejectedOutsideTestMode()
        {
            var prod = new EntitlementStore(false);
            Assert.IsFalse(prod.Accept(new PurchaseReceipt { productId = Products.RemoveAds, transactionId = "TEST-1", validated = true, isTest = true }));
            Assert.IsFalse(prod.Accept(new PurchaseReceipt { productId = Products.RemoveAds, transactionId = "GPA-1", validated = false }));
            Assert.IsTrue(prod.Accept(new PurchaseReceipt { productId = Products.RemoveAds, transactionId = "GPA-1", validated = true }));
        }

        [Test]
        public void RestoreRegrantsEntitlement()
        {
            var store = new TestPurchaseProvider();
            store.Purchase(Products.RemoveAds, (r, rec) => { });
            var fresh = new EntitlementStore(true);
            store.Restore(list => { foreach (var r in list) fresh.Accept(r); });
            Assert.IsTrue(fresh.Has(Products.RemoveAds));
        }

        [Test]
        public void AnalyticsRecordsNothingWithoutConsent()
        {
            var a = new AnalyticsService(_clock, false, "s");
            var sink = new MemorySink();
            a.AddSink(sink);
            a.Track("session_start");
            a.TrackRevenue("x", 1);
            Assert.AreEqual(0, sink.Events.Count);
            Assert.AreEqual(0, a.EstimatedRevenueUsd);
            a.SetConsent(true);
            a.Track("session_start");
            Assert.AreEqual(1, sink.Events.Count);
            StringAssert.Contains("\"name\":\"session_start\"", sink.Events[0].ToJson());
        }
    }
}
