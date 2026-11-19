using System;
using System.IO;
using System.Text;
using GOA.Services;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>Writes analytics events as JSON lines to persistent storage (local only; no network).</summary>
    public sealed class FileAnalyticsSink : IAnalyticsSink
    {
        readonly string _path;
        readonly StringBuilder _buffer = new StringBuilder();
        public FileAnalyticsSink(string path) { _path = path; }
        public void Write(AnalyticsEvent e) { _buffer.Append(e.ToJson()).Append('\n'); if (_buffer.Length > 8000) Flush(); }
        public void Flush()
        {
            if (_buffer.Length == 0) return;
            try { File.AppendAllText(_path, _buffer.ToString()); } catch (Exception ex) { Debug.LogWarning("[GOA] analytics write failed: " + ex.Message); }
            _buffer.Clear();
        }
    }

    public sealed class UnityClock : IClock
    {
        public double Now => Time.realtimeSinceStartupAsDouble;
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public sealed class SessionGate : IGameplayGate
    {
        public Func<bool> Critical = () => false;
        public bool IsCriticalMoment => Critical();
        public bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable || Application.isEditor;
    }

    /// <summary>
    /// Wires the platform-independent services together. In the editor and browser the providers are the mock ad
    /// provider and the test store; native AdMob / Play Billing / StoreKit providers plug in behind the same
    /// interfaces for device builds (see docs/MONETIZATION.md). Live ads are never used in development builds.
    /// </summary>
    public sealed class ServicesHub
    {
        public readonly AnalyticsService Analytics;
        public readonly EntitlementStore Entitlements;
        public readonly AdManager Ads;
        public readonly MockAdProvider MockAds;
        public readonly IPurchaseProvider Store;
        public readonly SessionGate Gate = new SessionGate();
        readonly FileAnalyticsSink _sink;

        public ServicesHub(bool analyticsConsent)
        {
            var clock = new UnityClock();
            Analytics = new AnalyticsService(clock, analyticsConsent, Guid.NewGuid().ToString("N").Substring(0, 12)) { Platform = Application.platform.ToString() };
            _sink = new FileAnalyticsSink(Path.Combine(Application.persistentDataPath, "goa_analytics.jsonl"));
            Analytics.AddSink(_sink);
            // Development configuration: test mode is mandatory until production ad units are supplied by the account owner.
            Entitlements = new EntitlementStore(testMode: true, Analytics);
            MockAds = new MockAdProvider();
            Ads = new AdManager(AdConfig.Default(), MockAds, clock, Entitlements, Analytics, Gate);
            Ads.Initialize();
            Store = new TestPurchaseProvider();
            Store.Initialize(_ => { });
            Analytics.Track("session_start", ("version", Application.version));
            int dayIndex = (int)(DateTime.UtcNow - new DateTime(2026, 1, 1)).TotalDays;
            string lastDay = PlayerPrefs.GetString("goa_last_day", "");
            if (lastDay != dayIndex.ToString())
            {
                Analytics.Track("daily_active", ("day", dayIndex.ToString()));
                PlayerPrefs.SetString("goa_last_day", dayIndex.ToString());
            }
            RestoreEntitlements();
        }

        public void RestoreEntitlements()
        {
            // the test store has no memory between launches; remember test receipts locally so testers keep their unlock
            if (PlayerPrefs.GetInt("goa_test_removeads", 0) == 1 && Store is TestPurchaseProvider tp)
                tp.Seed(new PurchaseReceipt { productId = Products.RemoveAds, transactionId = "TEST-RESTORED", validated = true, isTest = true });
            Store.Restore(list => { foreach (var r in list) Entitlements.Accept(r); });
        }

        public void BuyRemoveAds(Action<PurchaseResult> done)
        {
            Analytics.Track("iap_purchase_start", ("product", Products.RemoveAds));
            Store.Purchase(Products.RemoveAds, (res, receipt) =>
            {
                if ((res == PurchaseResult.Success || res == PurchaseResult.AlreadyOwned) && Entitlements.Accept(receipt) && receipt.isTest)
                    PlayerPrefs.SetInt("goa_test_removeads", 1);
                Analytics.Track("iap_purchase_result", ("product", Products.RemoveAds), ("result", res.ToString()));
                done?.Invoke(res);
            });
        }

        public void Shutdown()
        {
            Analytics.Track("session_end");
            Analytics.Flush();
        }
    }
}
