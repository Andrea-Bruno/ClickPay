using ClickPay.Wallet.Core.Blockchain;
using ClickPay.Wallet.Core.CryptoAssets;
using System.Globalization;

namespace ClickPay.Wallet.Core.Utility
{
    public static class PaymentUriBuilder
    {
        public static string BuildPaymentUri(CryptoAsset asset, string address, decimal amount, string? mint = null)
        {
            return asset.Network switch
            {
                BlockchainNetwork.Bitcoin => BuildBitcoinPaymentUri(address, amount),
                BlockchainNetwork.Solana => BuildSolanaPaymentUri(address, amount, mint),
                BlockchainNetwork.Ethereum => BuildEthereumPaymentUri(address, amount, mint),
                _ => address
            };
        }

        private static string BuildBitcoinPaymentUri(string address, decimal amount) =>
            amount > 0m ? $"bitcoin:{address}?amount={amount.ToString("0.########", CultureInfo.InvariantCulture)}" : $"bitcoin:{address}";

        private static string BuildSolanaPaymentUri(string address, decimal amount, string? mint)
        {
            var queryParts = new List<string>();
            if (amount > 0m)
            {
                queryParts.Add($"amount={amount.ToString("0.########", CultureInfo.InvariantCulture)}");
            }

            if (!string.IsNullOrWhiteSpace(mint))
            {
                queryParts.Add($"token={Uri.EscapeDataString(mint)}");
            }

            if (queryParts.Count == 0)
            {
                return $"solana:{address}";
            }

            return $"solana:{address}?{string.Join('&', queryParts)}";
        }

        private static string BuildEthereumPaymentUri(string address, decimal amount, string? mint)
        {
            // For ETH native
            if (string.IsNullOrWhiteSpace(mint))
            {
                return amount > 0m ? $"ethereum:{address}?value={amount.ToString("0.########", CultureInfo.InvariantCulture)}" : $"ethereum:{address}";
            }

            // For ERC20 tokens, it's more complex, but for simplicity, use the address
            // In a full implementation, would need to handle contract interactions
            return address;
        }
    }
}