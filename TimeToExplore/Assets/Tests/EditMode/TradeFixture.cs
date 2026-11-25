using System.IO;
using TimeToExplore.Core;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>Shared access to the shipped trade catalogue: one parsed
    /// instance for the cargo and trade suites.</summary>
    internal static class TradeFixture
    {
        private static TradeCatalogue _catalogue;

        public static TradeCatalogue Catalogue
        {
            get
            {
                if (_catalogue == null)
                {
                    string path = Path.Combine(
                        Application.dataPath, "Resources", "trade_catalogue.json");
                    _catalogue = TradeCatalogueLoader.Load(File.ReadAllText(path));
                }

                return _catalogue;
            }
        }
    }
}
