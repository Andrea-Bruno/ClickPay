using System;
using NBitcoin;

namespace ClickPay.Wallet.Core.Services
{
    public sealed class WalletMnemonicService
    {
        private static readonly int[] ValidWordCounts = { 12, 15, 18, 21, 24 };

        public string GenerateMnemonic(int wordCount = 24)
        {
            if (Array.IndexOf(ValidWordCounts, wordCount) < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(wordCount), $"Il numero di parole deve essere uno tra {string.Join(", ", ValidWordCounts)}.");
            }

            var mnemonic = new Mnemonic(Wordlist.English, ResolveWordCount(wordCount));
            return mnemonic.ToString();
        }

        public bool ValidateMnemonic(string? mnemonic)
        {
            if (string.IsNullOrWhiteSpace(mnemonic))
            {
                return false;
            }

            try
            {
                _ = new Mnemonic(mnemonic.Trim());
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static WordCount ResolveWordCount(int wordCount) => wordCount switch
        {
            12 => WordCount.Twelve,
            15 => WordCount.Fifteen,
            18 => WordCount.Eighteen,
            21 => WordCount.TwentyOne,
            24 => WordCount.TwentyFour,
            _ => throw new ArgumentOutOfRangeException(nameof(wordCount))
        };
    }
}
