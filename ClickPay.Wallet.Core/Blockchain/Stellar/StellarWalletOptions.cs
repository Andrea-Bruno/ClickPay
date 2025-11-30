using System;

namespace ClickPay.Wallet.Core.Blockchain.Stellar
{
    /// <summary>
    /// Configuration options for Stellar wallet operations.
    /// </summary>
    public sealed class StellarWalletOptions
    {
        public const string TestnetHorizonEndpoint = "https://horizon-testnet.stellar.org";
        public const string MainnetHorizonEndpoint = "https://horizon.stellar.org";
        public const string DefaultNetworkPassphrase = "Test SDF Network ; September 2015";
        public const string MainnetNetworkPassphrase = "Public Global Stellar Network ; September 2015";

        public static StellarWalletOptions Default => new StellarWalletOptions();

        /// <summary>
        /// Horizon server endpoint for Stellar network interactions.
        /// </summary>
        public string HorizonEndpoint { get; set; } = TestnetHorizonEndpoint;

        /// <summary>
        /// Network passphrase for transaction signing.
        /// </summary>
        public string NetworkPassphrase { get; set; } = DefaultNetworkPassphrase;

        /// <summary>
        /// Maximum number of transactions to fetch in history queries.
        /// </summary>
        public int TransactionHistoryLimit { get; set; } = 20;

        /// <summary>
        /// BIP44 purpose field for key derivation.
        /// </summary>
        public int Purpose { get; set; } = 44;

        /// <summary>
        /// BIP44 coin type for Stellar (148).
        /// </summary>
        public int CoinType { get; set; } = 148;

        /// <summary>
        /// Default account index for derivation.
        /// </summary>
        public int AccountIndex { get; set; } = 0;

        /// <summary>
        /// Minimum XLM balance required for account activation (in lumens).
        /// </summary>
        public decimal MinimumBalance { get; set; } = 1.0m;

        /// <summary>
        /// Base fee for transactions (in stroops).
        /// </summary>
        public uint BaseFee { get; set; } = 100;

        /// <summary>
        /// Current Horizon endpoint based on build configuration.
        /// </summary>
        public static string CurrentHorizonEndpoint
        {
            get
            {
#if DEBUG
                return TestnetHorizonEndpoint;
#else
                return MainnetHorizonEndpoint;
#endif
            }
        }

        /// <summary>
        /// Current network passphrase based on build configuration.
        /// </summary>
        public static string CurrentNetworkPassphrase
        {
            get
            {
#if DEBUG
                return DefaultNetworkPassphrase;
#else
                return MainnetNetworkPassphrase;
#endif
            }
        }
    }

    /// <summary>
    /// Represents a Stellar wallet account with key derivation information.
    /// </summary>
    public sealed record StellarWalletAccount(string PublicKey, string SecretKey, string DerivationPath, int AccountIndex);
}