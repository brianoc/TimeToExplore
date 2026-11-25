using System;
using UnityEngine;

namespace TimeToExplore.Core
{
    /// <summary>
    /// The one seam between the trade data and the rest of the game. The UI and
    /// the session read a parsed <see cref="TradeCatalogue"/>; swapping
    /// <see cref="Resources"/> for Addressables or StreamingAssets replaces this
    /// class, not the call sites.
    /// </summary>
    public static class TradeCatalogueLoader
    {
        public const string ResourcePath = "trade_catalogue";

        public static TradeCatalogue LoadFromResources()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw new InvalidOperationException(
                    $"Assets/Resources/{ResourcePath}.json was not found; Resources.Load "
                    + $"returned null for '{ResourcePath}'.");
            return TradeCatalogue.Parse(asset.text);
        }

        public static TradeCatalogue Load(string json) => TradeCatalogue.Parse(json);
    }
}
