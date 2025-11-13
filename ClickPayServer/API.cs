using UISupportGeneric.Resources;

namespace ClickPayServer
{
    public static class API
    {
        /// <summary>
        /// Allows the merchant to set the next payment, with the amounts that will be shown to the customer
        /// </summary>
        /// <param name="shopId">It is the seller's identifier, which is obtained with the hash of the QR code displayed in the store</param>
        /// <param name="currencyCode">the code of the currency required for payment (for example "EURC")</param>
        /// <param name="amount">The payment amount in the currency indicated by the Currency code</param>
        public static void SetNextPayment(string shopId, string currencyCode, float amount)
        {
            ClearNextPayments();
            lock (NextPayments)
            {
                NextPayments[shopId] = new NextPayment(shopId, currencyCode, amount);
            }
        }

        /// <summary>
        /// Retrieves the next payment information for the specified shop.
        /// </summary>
        /// <remarks>This method clears the current list of next payments before attempting to retrieve
        /// the payment information. The operation is thread-safe.</remarks>
        /// <param name="shopId">The unique identifier of the shop (obtained by the hash of QR code) for which to retrieve the next payment information.</param>
        /// <returns>An instance of <see cref="NextPaymentInfo"/> containing the currency code and amount of the next payment if
        /// available; otherwise, <see langword="null"/>.</returns>
        public static NextPaymentInfo? GetNextPayment(string shopId)
        {
            ClearNextPayments();
            lock (NextPayments)
            {
                if (NextPayments.TryGetValue(shopId, out var nextPayment))
                {
                    return new NextPaymentInfo
                    {
                        CurrencyCode = nextPayment.CurrencyCode,
                        Amount = nextPayment.Amount
                    };
                }
            }
            return null;
        }

        /// <summary>
        /// Clears all expired next payments from the dictionary.
        /// </summary>
        private static void ClearNextPayments()
        {
            lock (NextPayments)
            {
                // Get the current UTC time
                var now = DateTime.UtcNow;

                // Find all keys for payments that have expired (older than the timeout)
                var expiredKeys = NextPayments
                    .Where(kv => now - kv.Value.CreationTime > NextPaymentTimeOut)
                    .Select(kv => kv.Key)
                    .ToList();

                // Remove all expired payments from the dictionary
                foreach (var key in expiredKeys)
                {
                    NextPayments.Remove(key);
                }
            }
        }

        private static TimeSpan NextPaymentTimeOut = TimeSpan.FromMinutes(5);

        private static Dictionary<string, NextPayment> NextPayments = [];

        class NextPayment(string shopId, string currencyCode, float amount)
        {
            public DateTime CreationTime = DateTime.UtcNow;
            public string ShopId = shopId;
            public string CurrencyCode = currencyCode;
            public float Amount = amount;
        }

        /// <summary>
        /// Represents information about the next payment, including the amount and currency.
        /// </summary>
        /// <remarks>This class provides details about a payment, such as the currency code and the
        /// payment amount. It is typically used to store or transfer payment-related data.</remarks>
        public class NextPaymentInfo
        {
            public string CurrencyCode;
            public float Amount;
        }
    }
}
