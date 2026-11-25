using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeToExplore.Core
{
    /// <summary>Mirrors Assets/Resources/trade_catalogue.json.</summary>
    [Serializable]
    public class TradeFile
    {
        public CommodityData[] commodities;
        public MarketData[] markets;
    }

    [Serializable]
    public class CommodityData
    {
        public string id;
        public string name;
        public double volumePerUnitM3;
        public double massPerUnitKg;
        public string description;
    }

    [Serializable]
    public class MarketData
    {
        public string stationId;
        public TradeOfferData[] offers;
    }

    [Serializable]
    public class TradeOfferData
    {
        public string commodityId;
        public int buyPrice;
        public int sellPrice;
    }

    /// <summary>
    /// Read-only view over the parsed trade file. <see cref="Parse"/> validates
    /// the documented schema once, so a market row that reaches the UI always
    /// names a commodity that exists and a spread that cannot print free money.
    /// </summary>
    public sealed class TradeCatalogue
    {
        private readonly List<CommodityData> _commodityList = new List<CommodityData>();
        private readonly List<MarketData> _marketList = new List<MarketData>();
        private readonly Dictionary<string, CommodityData> _commodities =
            new Dictionary<string, CommodityData>(StringComparer.Ordinal);
        private readonly Dictionary<string, MarketData> _markets =
            new Dictionary<string, MarketData>(StringComparer.Ordinal);

        public IReadOnlyList<CommodityData> Commodities => _commodityList;
        public IReadOnlyList<MarketData> Markets => _marketList;

        private TradeCatalogue() { }

        /// <summary>
        /// Parse and validate a trade file. The exception names the offending
        /// row: a commodity id, a market's station id, or an offer's commodity.
        /// </summary>
        public static TradeCatalogue Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("trade_catalogue.json is empty.");

            TradeFile file = JsonUtility.FromJson<TradeFile>(json);
            if (file == null || file.commodities == null || file.markets == null)
                throw new InvalidOperationException(
                    "trade_catalogue.json must contain 'commodities' and 'markets' arrays.");

            var catalogue = new TradeCatalogue();

            for (int i = 0; i < file.commodities.Length; i++)
            {
                CommodityData commodity = file.commodities[i];
                if (commodity == null)
                    throw new InvalidOperationException($"trade_catalogue.json: commodity[{i}] is null.");
                if (string.IsNullOrEmpty(commodity.id))
                    throw new InvalidOperationException($"trade_catalogue.json: commodity[{i}] has no id.");
                if (catalogue._commodities.ContainsKey(commodity.id))
                    throw new InvalidOperationException(
                        $"trade_catalogue.json: duplicate commodity id '{commodity.id}'.");
                if (!(commodity.volumePerUnitM3 > 0.0))
                    throw new InvalidOperationException(
                        $"trade_catalogue.json: commodity '{commodity.id}' has volumePerUnitM3 "
                        + $"{commodity.volumePerUnitM3}, which must be positive.");
                if (!(commodity.massPerUnitKg > 0.0))
                    throw new InvalidOperationException(
                        $"trade_catalogue.json: commodity '{commodity.id}' has massPerUnitKg "
                        + $"{commodity.massPerUnitKg}, which must be positive.");

                catalogue._commodities.Add(commodity.id, commodity);
                catalogue._commodityList.Add(commodity);
            }

            for (int i = 0; i < file.markets.Length; i++)
            {
                MarketData market = file.markets[i];
                if (market == null)
                    throw new InvalidOperationException($"trade_catalogue.json: market[{i}] is null.");
                if (string.IsNullOrEmpty(market.stationId))
                    throw new InvalidOperationException($"trade_catalogue.json: market[{i}] has no stationId.");
                if (catalogue._markets.ContainsKey(market.stationId))
                    throw new InvalidOperationException(
                        $"trade_catalogue.json: duplicate market stationId '{market.stationId}'.");

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (TradeOfferData offer in market.offers ?? Array.Empty<TradeOfferData>())
                {
                    if (offer == null || string.IsNullOrEmpty(offer.commodityId))
                        throw new InvalidOperationException(
                            $"trade_catalogue.json: market '{market.stationId}' has an offer with no commodityId.");
                    if (!catalogue._commodities.ContainsKey(offer.commodityId))
                        throw new InvalidOperationException(
                            $"trade_catalogue.json: market '{market.stationId}' offers unknown commodity "
                            + $"'{offer.commodityId}'.");
                    if (!seen.Add(offer.commodityId))
                        throw new InvalidOperationException(
                            $"trade_catalogue.json: market '{market.stationId}' lists commodity "
                            + $"'{offer.commodityId}' twice.");
                    if (offer.buyPrice < 0)
                        throw new InvalidOperationException(
                            $"trade_catalogue.json: market '{market.stationId}' offer '{offer.commodityId}' "
                            + $"has buyPrice {offer.buyPrice}, which cannot be negative.");
                    if (offer.sellPrice < 0)
                        throw new InvalidOperationException(
                            $"trade_catalogue.json: market '{market.stationId}' offer '{offer.commodityId}' "
                            + $"has sellPrice {offer.sellPrice}, which cannot be negative.");
                    if (offer.sellPrice > offer.buyPrice)
                        throw new InvalidOperationException(
                            $"trade_catalogue.json: market '{market.stationId}' offer '{offer.commodityId}' "
                            + $"pays sellPrice {offer.sellPrice} above buyPrice {offer.buyPrice}.");
                }

                catalogue._markets.Add(market.stationId, market);
                catalogue._marketList.Add(market);
            }

            return catalogue;
        }

        public bool TryGetCommodity(string id, out CommodityData commodity)
        {
            if (string.IsNullOrEmpty(id))
            {
                commodity = null;
                return false;
            }

            return _commodities.TryGetValue(id, out commodity);
        }

        public bool TryGetMarket(string bodyId, out MarketData market)
        {
            if (string.IsNullOrEmpty(bodyId))
            {
                market = null;
                return false;
            }

            return _markets.TryGetValue(bodyId, out market);
        }

        public bool HasMarket(string bodyId) => TryGetMarket(bodyId, out _);

        /// <summary>Commodity by id. Throws when the row does not exist.</summary>
        public CommodityData Commodity(string id)
        {
            if (TryGetCommodity(id, out CommodityData commodity)) return commodity;
            throw new KeyNotFoundException($"The trade catalogue has no commodity '{id}'.");
        }
    }
}
