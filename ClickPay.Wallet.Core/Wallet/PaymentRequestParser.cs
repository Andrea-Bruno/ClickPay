using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Blockchain.Bitcoin;
using ClickPay.Wallet.Core.Blockchain.Ethereum;
using ClickPay.Wallet.Core.Blockchain.Solana;
using ClickPay.Wallet.Core.Services;
using Microsoft.Extensions.Options;
using NBitcoin;
using Solnet.Wallet;

namespace ClickPay.Wallet.Core.Wallet
{
    public sealed class PaymentRequestParser
    {
        private const int EthereumNativeDecimals = 18;

        private readonly BitcoinWalletOptions _bitcoinOptions;
        private readonly SolanaWalletOptions _solanaOptions;
        private readonly EthereumWalletOptions _ethereumOptions;
        private readonly Dictionary<BlockchainNetwork, CryptoAsset> _nativeAssets;
        private readonly Dictionary<BlockchainNetwork, Dictionary<string, CryptoAsset>> _tokenMaps;

        public PaymentRequestParser(
            IOptions<BitcoinWalletOptions> bitcoinOptions,
            IOptions<SolanaWalletOptions> solanaOptions,
            IOptions<EthereumWalletOptions> ethereumOptions)
        {
            _bitcoinOptions = bitcoinOptions.Value ?? BitcoinWalletOptions.Default;
            _solanaOptions = solanaOptions.Value ?? SolanaWalletOptions.Default;
            _ethereumOptions = ethereumOptions.Value ?? EthereumWalletOptions.Default;
            _nativeAssets = new Dictionary<BlockchainNetwork, CryptoAsset>();
            _tokenMaps = new Dictionary<BlockchainNetwork, Dictionary<string, CryptoAsset>>();
            BuildAssetLookups();
        }

        public PaymentRequestParseResult Parse(string? rawPayload)
        {
            var payload = Normalize(rawPayload);
            if (payload is null)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_NoData", payload);
            }

            return ParseInternal(payload, expectedAsset: null);
        }

        public PaymentRequestParseResult Parse(string expectedAssetCode, string? rawPayload)
        {
            if (!WalletAssetHelper.TryGetDefinition(expectedAssetCode, out var expectedAsset))
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", rawPayload);
            }

            return Parse(expectedAsset, rawPayload);
        }

        public PaymentRequestParseResult Parse(CryptoAsset expectedAsset, string? rawPayload)
        {
            if (expectedAsset is null)
            {
                throw new ArgumentNullException(nameof(expectedAsset));
            }

            var payload = Normalize(rawPayload);
            if (payload is null)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_NoData", payload);
            }

            return ParseInternal(payload, expectedAsset);
        }

        private PaymentRequestParseResult ParseInternal(string payload, CryptoAsset? expectedAsset)
        {
            var schemeSeparator = payload.IndexOf(':');
            if (schemeSeparator > 0)
            {
                var scheme = payload[..schemeSeparator].ToLowerInvariant();
                return scheme switch
                {
                    "bitcoin" => ParseBitcoin(payload, expectedAsset),
                    "solana" => ParseSolana(payload, expectedAsset),
                    "ethereum" or "eth" => ParseEthereum(payload, expectedAsset),
                    _ => PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidFormat", payload)
                };
            }

            if (expectedAsset is not null)
            {
                return expectedAsset.Network switch
                {
                    BlockchainNetwork.Bitcoin => ParseBitcoin(payload, expectedAsset),
                    BlockchainNetwork.Solana => ParseSolana(payload, expectedAsset),
                    BlockchainNetwork.Ethereum => ParseEthereum(payload, expectedAsset),
                    _ => PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload)
                };
            }

            var btcResult = ParseBitcoin(payload, GetNativeAsset(BlockchainNetwork.Bitcoin));
            if (btcResult.Success)
            {
                return btcResult;
            }

            var solResult = ParseSolana(payload, null);
            if (solResult.Success)
            {
                return solResult;
            }

            var ethResult = ParseEthereum(payload, null);
            if (ethResult.Success)
            {
                return ethResult;
            }

            return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
        }

        private PaymentRequestParseResult ParseBitcoin(string payload, CryptoAsset? expectedAsset)
        {
            if (expectedAsset is not null && expectedAsset.Network != BlockchainNetwork.Bitcoin)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
            }

            string address = payload;
            decimal? amount = null;

            if (payload.Contains(':', StringComparison.Ordinal))
            {
                if (!payload.StartsWith("bitcoin:", StringComparison.OrdinalIgnoreCase))
                {
                    return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
                }

                if (!Uri.TryCreate(payload, UriKind.Absolute, out var uri))
                {
                    return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidFormat", payload);
                }

                address = TrimLeadingSlash(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(uri.Query))
                {
                    var query = ParseQuery(uri.Query);
                    if (query.TryGetValue("amount", out var amountValue))
                    {
                        if (TryParseDecimal(amountValue, out var parsedAmount) && parsedAmount > 0m)
                        {
                            amount = parsedAmount;
                        }
                        else
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAmount", payload);
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAddress", payload);
            }

            var networkName = _bitcoinOptions.Network ?? Network.Main.ToString();
            var expectedNetwork = Network.GetNetwork(networkName) ?? Network.Main;

            try
            {
                var btcAddress = BitcoinAddress.Create(address, expectedNetwork);
                address = btcAddress.ToString();
            }
            catch (FormatException)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAddress", payload);
            }

            var resolved = expectedAsset ?? GetNativeAsset(BlockchainNetwork.Bitcoin);
            if (resolved is null)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
            }

            return PaymentRequestParseResult.CreateSuccess(resolved, address, amount, payload);
        }

        private PaymentRequestParseResult ParseSolana(string payload, CryptoAsset? expectedAsset)
        {
            string address = payload;
            decimal? amount = null;
            string? mint = null;

            if (payload.Contains(':', StringComparison.Ordinal))
            {
                if (!payload.StartsWith("solana:", StringComparison.OrdinalIgnoreCase))
                {
                    return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
                }

                if (!Uri.TryCreate(payload, UriKind.Absolute, out var uri))
                {
                    return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidFormat", payload);
                }

                address = TrimLeadingSlash(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(uri.Query))
                {
                    var query = ParseQuery(uri.Query);
                    if (query.TryGetValue("amount", out var amountValue))
                    {
                        if (TryParseDecimal(amountValue, out var parsedAmount) && parsedAmount > 0m)
                        {
                            amount = parsedAmount;
                        }
                        else
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAmount", payload);
                        }
                    }

                    if (query.TryGetValue("token", out var tokenValue))
                    {
                        mint = tokenValue;
                    }
                    else if (query.TryGetValue("mint", out var mintValue))
                    {
                        mint = mintValue;
                    }
                    else if (query.TryGetValue("spl-token", out var splValue))
                    {
                        mint = splValue;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAddress", payload);
            }

            try
            {
                var publicKey = new PublicKey(address);
                address = publicKey.Key;
            }
            catch (Exception)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAddress", payload);
            }

            mint = NormalizeMint(mint);
            CryptoAsset? resolved;

            if (expectedAsset is not null)
            {
                if (expectedAsset.Network != BlockchainNetwork.Solana)
                {
                    return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
                }

                var expectedIdentifier = ResolveExpectedTokenIdentifier(expectedAsset);
                if (string.IsNullOrEmpty(expectedIdentifier))
                {
                    if (!string.IsNullOrEmpty(mint))
                    {
                        return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                    }

                    resolved = expectedAsset;
                }
                else
                {
                    var providedIdentifier = string.IsNullOrEmpty(mint)
                        ? expectedIdentifier
                        : NormalizeTokenIdentifier(BlockchainNetwork.Solana, mint);

                    if (!string.Equals(providedIdentifier, expectedIdentifier, StringComparison.Ordinal))
                    {
                        return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                    }

                    mint = expectedIdentifier;
                    resolved = expectedAsset;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(mint))
                {
                    resolved = GetNativeAsset(BlockchainNetwork.Solana);
                }
                else if (!TryResolveToken(BlockchainNetwork.Solana, mint, out resolved))
                {
                    return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                }
            }

            if (resolved is null)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
            }

            return PaymentRequestParseResult.CreateSuccess(resolved, address, amount, payload);
        }

        private static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        private static string TrimLeadingSlash(string value) => string.IsNullOrEmpty(value) ? string.Empty : value.TrimStart('/');

        private static bool TryParseDecimal(string input, out decimal parsed) =>
            decimal.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query))
            {
                return result;
            }

            var span = query.AsSpan();
            if (span.Length > 0 && span[0] == '?')
            {
                span = span[1..];
            }

            var segments = span.ToString().Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var kvp = segment.Split('=', 2);
                var key = Uri.UnescapeDataString(kvp[0]);
                var value = kvp.Length > 1 ? Uri.UnescapeDataString(kvp[1]) : string.Empty;
                result[key] = value;
            }

            return result;
        }

        private static string NormalizeMint(string? mint) => string.IsNullOrWhiteSpace(mint) ? string.Empty : mint.Trim();

        private static bool MintEquals(string? candidate, string? reference)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            return string.Equals(candidate.Trim(), reference.Trim(), StringComparison.Ordinal);
        }

        private PaymentRequestParseResult ParseEthereum(string payload, CryptoAsset? expectedAsset)
        {
            if (expectedAsset is not null && expectedAsset.Network != BlockchainNetwork.Ethereum)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
            }

            string address = payload;
            decimal? amount = null;
            decimal? tokenAmount = null;
            string? tokenIdentifier = null;

            if (payload.Contains(':', StringComparison.Ordinal))
            {
                if (!payload.StartsWith("ethereum:", StringComparison.OrdinalIgnoreCase) &&
                    !payload.StartsWith("eth:", StringComparison.OrdinalIgnoreCase))
                {
                    return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
                }

                var schemeIndex = payload.IndexOf(':');
                var content = payload[(schemeIndex + 1)..];
                var queryIndex = content.IndexOf('?');
                var queryString = queryIndex >= 0 ? content[(queryIndex + 1)..] : string.Empty;
                var basePart = queryIndex >= 0 ? content[..queryIndex] : content;

                var segments = basePart.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var primary = segments.Length > 0 ? segments[0] : string.Empty;
                var action = segments.Length > 1 ? segments[1] : string.Empty;

                var query = ParseQuery(queryString);

                if (string.Equals(action, "transfer", StringComparison.OrdinalIgnoreCase))
                {
                    tokenIdentifier = primary;
                    address = query.TryGetValue("address", out var to)
                        ? to
                        : query.TryGetValue("to", out var alt) ? alt : string.Empty;

                    if (string.IsNullOrWhiteSpace(address))
                    {
                        return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAddress", payload);
                    }

                    if (query.TryGetValue("uint256", out var uintValue))
                    {
                        if (!TryParseTokenQuantity(uintValue, expectedAsset, out var parsedTokenAmount))
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAmount", payload);
                        }

                        tokenAmount = parsedTokenAmount;
                    }
                    else if (query.TryGetValue("value", out var rawValue))
                    {
                        if (!TryParseTokenQuantity(rawValue, expectedAsset, out var parsedTokenAmount))
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAmount", payload);
                        }

                        tokenAmount = parsedTokenAmount;
                    }
                }
                else
                {
                    tokenIdentifier = null;
                    address = primary;

                    if (query.TryGetValue("value", out var valueRaw))
                    {
                        if (!TryParseEthereumValue(valueRaw, out amount))
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAmount", payload);
                        }
                    }
                    else if (query.TryGetValue("amount", out var amountValue))
                    {
                        if (TryParseDecimal(amountValue, out var parsedAmount) && parsedAmount > 0m)
                        {
                            amount = parsedAmount;
                        }
                        else
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAmount", payload);
                        }
                    }
                }
            }

            address = NormalizeEthereumAddress(address);
            if (!IsValidEthereumAddress(address))
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidAddress", payload);
            }

            CryptoAsset? resolved;

            if (expectedAsset is not null)
            {
                var expectedIdentifier = ResolveExpectedTokenIdentifier(expectedAsset);
                if (string.IsNullOrEmpty(expectedIdentifier))
                {
                    if (!string.IsNullOrEmpty(tokenIdentifier))
                    {
                        return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                    }

                    resolved = expectedAsset;
                }
                else
                {
                    var provided = string.IsNullOrEmpty(tokenIdentifier)
                        ? expectedIdentifier
                        : NormalizeTokenIdentifier(BlockchainNetwork.Ethereum, tokenIdentifier);

                    if (!string.Equals(provided, expectedIdentifier, StringComparison.Ordinal))
                    {
                        return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                    }

                    resolved = expectedAsset;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(tokenIdentifier))
                {
                    if (!TryResolveToken(BlockchainNetwork.Ethereum, tokenIdentifier, out resolved))
                    {
                        return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                    }
                }
                else
                {
                    resolved = GetNativeAsset(BlockchainNetwork.Ethereum);
                }
            }

            if (resolved is null)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
            }

            var resolvedAmount = string.IsNullOrWhiteSpace(resolved.ContractAddress)
                ? amount
                : tokenAmount ?? amount;

            return PaymentRequestParseResult.CreateSuccess(resolved, address, resolvedAmount, payload);
        }

        private static bool TryParseEthereumValue(string value, out decimal? amount)
        {
            amount = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!BigInteger.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    {
                        return false;
                    }

                    amount = ConvertFromAtomic(hex, EthereumNativeDecimals);
                    return true;
                }

                if (BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
                {
                    amount = ConvertFromAtomic(raw, EthereumNativeDecimals);
                    return true;
                }
            }
            catch (OverflowException)
            {
                return false;
            }

            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0m)
            {
                amount = parsed;
                return true;
            }

            return false;
        }

        private bool TryParseTokenQuantity(string value, CryptoAsset? expectedAsset, out decimal amount)
        {
            amount = 0m;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var decimals = expectedAsset?.Decimals ?? EthereumWalletOptions.DefaultTokenDecimals;

            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!BigInteger.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    {
                        return false;
                    }

                    amount = ConvertFromAtomic(hex, decimals);
                    return true;
                }

                if (BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
                {
                    amount = ConvertFromAtomic(raw, decimals);
                    return true;
                }
            }
            catch (OverflowException)
            {
                return false;
            }

            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0m)
            {
                amount = parsed;
                return true;
            }

            return false;
        }

        private static decimal ConvertFromAtomic(BigInteger value, int decimals)
        {
            if (decimals <= 0)
            {
                return (decimal)value;
            }

            var scale = BigInteger.Pow(10, decimals);
            var quotient = BigInteger.DivRem(value, scale, out var remainder);
            var result = (decimal)quotient;
            if (remainder != 0)
            {
                result += (decimal)remainder / (decimal)scale;
            }

            return result;
        }

        private void BuildAssetLookups()
        {
            _nativeAssets.Clear();
            _tokenMaps.Clear();

            foreach (var asset in WalletAssetHelper.GetRegisteredAssets())
            {
                if (!WalletAssetHelper.IsSupportedNetwork(asset.Network))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(asset.ContractAddress))
                {
                    if (!_nativeAssets.ContainsKey(asset.Network))
                    {
                        _nativeAssets[asset.Network] = asset;
                    }

                    continue;
                }

                var identifier = NormalizeTokenIdentifier(asset.Network, asset.ContractAddress);
                if (string.IsNullOrEmpty(identifier))
                {
                    continue;
                }

                var map = GetOrCreateTokenMap(asset.Network);
                map[identifier] = asset;
            }
        }

        private Dictionary<string, CryptoAsset> GetOrCreateTokenMap(BlockchainNetwork network)
        {
            if (!_tokenMaps.TryGetValue(network, out var map))
            {
                map = new Dictionary<string, CryptoAsset>(StringComparer.Ordinal);
                _tokenMaps[network] = map;
            }

            return map;
        }

        private CryptoAsset? GetNativeAsset(BlockchainNetwork network)
        {
            return _nativeAssets.TryGetValue(network, out var asset) ? asset : null;
        }

        private bool TryResolveToken(BlockchainNetwork network, string identifier, out CryptoAsset asset)
        {
            asset = default!;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            var normalized = NormalizeTokenIdentifier(network, identifier);
            if (_tokenMaps.TryGetValue(network, out var map) && map.TryGetValue(normalized, out var resolved))
            {
                asset = resolved;
                return true;
            }

            return false;
        }

        private string NormalizeTokenIdentifier(BlockchainNetwork network, string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return string.Empty;
            }

            var trimmed = identifier.Trim();
            return network switch
            {
                BlockchainNetwork.Ethereum => trimmed.ToLowerInvariant(),
                _ => trimmed
            };
        }

        private string? ResolveExpectedTokenIdentifier(CryptoAsset asset)
        {
            if (string.IsNullOrWhiteSpace(asset.ContractAddress))
            {
                return null;
            }

            return NormalizeTokenIdentifier(asset.Network, asset.ContractAddress);
        }

        private static string NormalizeEthereumAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return string.Empty;
            }

            var normalized = address.Trim();
            if (!normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "0x" + normalized;
            }

            return normalized.ToLowerInvariant();
        }

        private static bool IsValidEthereumAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            if (!address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (address.Length != 42)
            {
                return false;
            }

            for (var i = 2; i < address.Length; i++)
            {
                if (!Uri.IsHexDigit(address[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed record PaymentRequestParseResult(
        bool Success,
        string? AssetCode,
        CryptoAsset? Asset,
        string? Address,
        decimal? Amount,
        string? RawPayload,
        string? ErrorCode)
    {
        public static PaymentRequestParseResult CreateSuccess(CryptoAsset asset, string address, decimal? amount, string rawPayload)
            => new(true, asset.Code, asset, address, amount, rawPayload, null);

        public static PaymentRequestParseResult CreateFailure(string errorCode, string? rawPayload)
            => new(false, null, null, null, null, rawPayload, errorCode);
    }
}
