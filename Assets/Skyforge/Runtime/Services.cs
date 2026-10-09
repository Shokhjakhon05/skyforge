using System;

namespace Skyforge
{
    /// <summary>
    /// Advertising, as the game sees it. The rules about when an ad may be offered live in
    /// <see cref="Skyforge.Core.AdRules"/>; an implementation of this interface only knows how to
    /// load and show one. Swapping the ad network means writing another implementation.
    /// </summary>
    public interface IAdsProvider
    {
        /// <summary>A rewarded ad is loaded and can be shown right now.</summary>
        bool RewardedReady { get; }

        /// <summary>Shows a rewarded ad. The callback gets true only if it was watched to the end.</summary>
        void ShowRewarded(Action<bool> finished);

        bool InterstitialReady { get; }

        /// <summary>Shows an interstitial; the callback runs when it is closed, or at once if nothing was shown.</summary>
        void ShowInterstitial(Action closed);
    }

    /// <summary>
    /// The provider used until an ad SDK is wired in: no ad is ever ready, so every ad button stays
    /// hidden and the game plays exactly as it would offline.
    /// </summary>
    public sealed class NoAds : IAdsProvider
    {
        public bool RewardedReady => false;
        public bool InterstitialReady => false;
        public void ShowRewarded(Action<bool> finished) => finished(false);
        public void ShowInterstitial(Action closed) => closed();
    }

    /// <summary>Where game events go for measurement. Gameplay never depends on anything coming back.</summary>
    public interface IAnalytics
    {
        void Event(string name, string detail = null, int value = 0);
    }

    /// <summary>Writes events to the device log only; nothing leaves the phone.</summary>
    public sealed class LogAnalytics : IAnalytics
    {
        public void Event(string name, string detail = null, int value = 0)
        {
            UnityEngine.Debug.Log("[Skyforge] event " + name + (detail == null ? "" : " " + detail) + (value == 0 ? "" : " " + value));
        }
    }
}
