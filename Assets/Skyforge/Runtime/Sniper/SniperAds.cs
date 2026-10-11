using System;
using UnityEngine;
#if SNIPER_ADS
using GoogleMobileAds.Api;
#endif

namespace Sniper
{
    /// <summary>
    /// Advertising, kept at arm's length: the game asks for a rewarded video or a full-screen
    /// break and is told whether it happened. Without the SDK in the build (or with it failing)
    /// every question is answered "no", and nothing else in the game notices.
    /// </summary>
    public sealed class SniperAds
    {
        // Google's own demonstration units, used when the build was given none.
        const string TestRewarded = "ca-app-pub-3940256099942544/5224354917", TestInterstitial = "ca-app-pub-3940256099942544/1033173712";
        readonly string _rewardedId, _interstitialId;
        bool _ready;
#if SNIPER_ADS
        RewardedAd _rewarded;
        InterstitialAd _interstitial;
#endif

        public SniperAds()
        {
            _rewardedId = TestRewarded;
            _interstitialId = TestInterstitial;
            var given = Resources.Load<TextAsset>("ads");
            if (given != null)
            {
                foreach (string line in given.text.Split('\n'))
                {
                    int cut = line.IndexOf('=');
                    if (cut <= 0) continue;
                    string key = line.Substring(0, cut).Trim(), value = line.Substring(cut + 1).Trim();
                    if (value.Length < 10) continue;
                    if (key == "rewarded") _rewardedId = value;
                    else if (key == "interstitial") _interstitialId = value;
                }
            }
#if SNIPER_ADS && UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                MobileAds.RaiseAdEventsOnUnityMainThread = true;
                MobileAds.Initialize(status =>
                {
                    _ready = true;
                    Debug.Log("[Sniper] ads ready");
                    LoadRewarded();
                    LoadInterstitial();
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sniper] ads unavailable: " + e.Message);
            }
#endif
        }

        public bool RewardedReady
        {
            get
            {
#if SNIPER_ADS && UNITY_ANDROID && !UNITY_EDITOR
                try { return _ready && _rewarded != null && _rewarded.CanShowAd(); }
                catch (Exception) { return false; }
#else
                return false;
#endif
            }
        }

#if SNIPER_ADS && UNITY_ANDROID && !UNITY_EDITOR
        void LoadRewarded()
        {
            try
            {
                RewardedAd.Load(_rewardedId, new AdRequest(), (ad, error) =>
                {
                    if (error != null || ad == null)
                    {
                        Debug.Log("[Sniper] rewarded not loaded: " + (error != null ? error.GetMessage() : "none"));
                        return;
                    }
                    _rewarded = ad;
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sniper] rewarded load failed: " + e.Message);
            }
        }

        void LoadInterstitial()
        {
            try
            {
                InterstitialAd.Load(_interstitialId, new AdRequest(), (ad, error) =>
                {
                    if (error != null || ad == null)
                    {
                        Debug.Log("[Sniper] interstitial not loaded: " + (error != null ? error.GetMessage() : "none"));
                        return;
                    }
                    _interstitial = ad;
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sniper] interstitial load failed: " + e.Message);
            }
        }
#endif

        /// <summary>Shows a rewarded video; the answer is whether it was watched to the end.</summary>
        public void ShowRewarded(Action<bool> done)
        {
#if SNIPER_ADS && UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!RewardedReady)
                {
                    done(false);
                    return;
                }
                RewardedAd ad = _rewarded;
                _rewarded = null;
                bool earned = false, answered = false;
                void Answer()
                {
                    if (answered) return;
                    answered = true;
                    done(earned);
                    LoadRewarded();
                }
                ad.OnAdFullScreenContentClosed += Answer;
                ad.OnAdFullScreenContentFailed += error => Answer();
                ad.Show(reward => earned = true);
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sniper] rewarded show failed: " + e.Message);
            }
#endif
            done(false);
        }

        /// <summary>A full-screen break between contracts, if one is loaded; the game carries on either way.</summary>
        public void ShowInterstitial(Action then)
        {
#if SNIPER_ADS && UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_ready && _interstitial != null && _interstitial.CanShowAd())
                {
                    InterstitialAd ad = _interstitial;
                    _interstitial = null;
                    bool went = false;
                    void Go()
                    {
                        if (went) return;
                        went = true;
                        then();
                        LoadInterstitial();
                    }
                    ad.OnAdFullScreenContentClosed += Go;
                    ad.OnAdFullScreenContentFailed += error => Go();
                    ad.Show();
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sniper] interstitial show failed: " + e.Message);
            }
#endif
            then();
        }
    }
}
