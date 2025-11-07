using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Options;
using NBitcoin;
using Solnet.Wallet;

namespace ClickPay.Shared.Services
{
    public sealed class PaymentRequestParser
    {
        private readonly BitcoinWalletOptions _bitcoinOptions;
        private readonly SolanaWalletOptions _solanaOptions;
        private readonly string _eurcMintNormalized;
        private readonly string _xautMintNormalized;

        public PaymentRequestParser(IOptions<BitcoinWalletOptions> bitcoinOptions, IOptions<SolanaWalletOptions> solanaOptions)
        {
            _bitcoinOptions = bitcoinOptions.Value ?? BitcoinWalletOptions.Default;
            _solanaOptions = solanaOptions.Value ?? SolanaWalletOptions.Default;
            _eurcMintNormalized = NormalizeMint(_solanaOptions.EurcMintAddress);
            _xautMintNormalized = NormalizeMint(_solanaOptions.XautMintAddress);
        }

        public PaymentRequestParseResult Parse(string? rawPayload)
        {
            var payload = Normalize(rawPayload);
            if (payload is null)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_NoData", payload);
            }

            return ParseInternal(payload, null);
        }

        public PaymentRequestParseResult Parse(WalletAsset expectedAsset, string? rawPayload)
        {
            var payload = Normalize(rawPayload);
            if (payload is null)
            {
                return PaymentRequestParseResult.CreateFailure("QrScan_Error_NoData", payload);
            }

            return ParseInternal(payload, expectedAsset);
        }

        private PaymentRequestParseResult ParseInternal(string payload, WalletAsset? expectedAsset)
        {
            var schemeSeparator = payload.IndexOf(':');
            if (schemeSeparator > 0)
            {
                var scheme = payload[..schemeSeparator].ToLowerInvariant();
                return scheme switch
                {
                    "bitcoin" => ParseBitcoin(payload, expectedAsset),
                    "solana" => ParseSolana(payload, expectedAsset),
                    _ => PaymentRequestParseResult.CreateFailure("QrScan_Error_InvalidFormat", payload)
                };
            }

            // No scheme, fallback detection or expected asset
            if (expectedAsset.HasValue)
            {
                return expectedAsset.Value switch
                {
                    WalletAsset.Bitcoin => ParseBitcoin(payload, expectedAsset),
                    WalletAsset.Eurc => ParseSolana(payload, expectedAsset),
                    WalletAsset.Xaut => ParseSolana(payload, expectedAsset),
                    WalletAsset.Sol => ParseSolana(payload, expectedAsset),
                    _ => PaymentRequestParseResult.CreateFailure("QrScan_Error_Generic", payload)
                };
            }

            var btcResult = ParseBitcoin(payload, WalletAsset.Bitcoin);
            if (btcResult.Success)
            {
                return btcResult;
            }

            var solResult = ParseSolana(payload, null);
            if (solResult.Success)
            {
                return solResult;
            }

            return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
        }

        private PaymentRequestParseResult ParseBitcoin(string payload, WalletAsset? expectedAsset)
        {
            if (expectedAsset.HasValue && expectedAsset.Value != WalletAsset.Bitcoin)
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

            return PaymentRequestParseResult.CreateSuccess(WalletAsset.Bitcoin, address, amount, payload);
        }

        private PaymentRequestParseResult ParseSolana(string payload, WalletAsset? expectedAsset)
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

            WalletAsset resolvedAsset;
            if (expectedAsset.HasValue)
            {
                switch (expectedAsset.Value)
                {
                    case WalletAsset.Sol:
                        if (!string.IsNullOrEmpty(mint))
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                        }

                        resolvedAsset = WalletAsset.Sol;
                        break;
                    case WalletAsset.Eurc:
                        if (!string.IsNullOrEmpty(mint) && !MintEquals(mint, _eurcMintNormalized))
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                        }

                        mint = _eurcMintNormalized;
                        resolvedAsset = WalletAsset.Eurc;
                        break;
                    case WalletAsset.Xaut:
                        if (!string.IsNullOrEmpty(mint) && !MintEquals(mint, _xautMintNormalized))
                        {
                            return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                        }

                        mint = _xautMintNormalized;
                        resolvedAsset = WalletAsset.Xaut;
                        break;
                    default:
                        return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongNetwork", payload);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(mint))
                {
                    resolvedAsset = WalletAsset.Sol;
                }
                else if (MintEquals(mint, _eurcMintNormalized))
                {
                    resolvedAsset = WalletAsset.Eurc;
                }
                else if (MintEquals(mint, _xautMintNormalized))
                {
                    resolvedAsset = WalletAsset.Xaut;
                }
                else
                {
                    return PaymentRequestParseResult.CreateFailure("QrScan_Error_WrongToken", payload);
                }
            }

            return PaymentRequestParseResult.CreateSuccess(resolvedAsset, address, amount, payload);
        }

        private static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        private static string TrimLeadingSlash(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.TrimStart('/');
        }

        private static bool TryParseDecimal(string input, out decimal parsed)
        {
            return decimal.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

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

        private bool MintEquals(string? candidate, string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(candidate) && string.Equals(candidate.Trim(), reference, StringComparison.Ordinal);
        }
    }

    public sealed record PaymentRequestParseResult(bool Success, WalletAsset? Asset, string? Address, decimal? Amount, string? RawPayload, string? ErrorCode)
    {
        public static PaymentRequestParseResult CreateSuccess(WalletAsset asset, string address, decimal? amount, string rawPayload)
            => new(true, asset, address, amount, rawPayload, null);

        public static PaymentRequestParseResult CreateFailure(string errorCode, string? rawPayload)
            => new(false, null, null, null, rawPayload, errorCode);
    }
}
