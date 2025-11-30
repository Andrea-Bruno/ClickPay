using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace API
{
    /// <summary>
    /// API Client Factory to manage HttpClient instances efficiently.
    /// </summary>
    public class APIClientFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public APIClientFactory()
        {
            var services = new ServiceCollection();
            services.AddHttpClient();
            var serviceProvider = services.BuildServiceProvider();
            _httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        }

        public HttpClient CreateClient() => _httpClientFactory.CreateClient();
    }

    /// <summary>
    /// API consumer client for interacting with API methods.
    /// </summary>
    public class ClickPayServerAPIClient
    {
        private readonly APIClientFactory _clientFactory;
        private readonly string _apiEntryPoint;

        /// <summary>
        /// Initializes a new instance of <see cref="ClickPayServerAPIClient"/>.
        /// </summary>
        /// <param name="apiEntryPoint">Base API URL.</param>
        public ClickPayServerAPIClient(string apiEntryPoint)
        {
            _clientFactory = new APIClientFactory();
            _apiEntryPoint = apiEntryPoint;
        }

        /// <summary>
        /// Allows the merchant to set the next payment, with the amounts that will be shown to the customer
        /// </summary>
        /// <param name="shopId">It is the seller's identifier, which is obtained with the hash of the QR code displayed in the store</param>
        /// <param name="currencyCode">the code of the currency required for payment (for example "EURC")</param>
        /// <param name="amount">The payment amount in the currency indicated by the Currency code</param>
        /// <returns>True if the operation was successful</returns>
        public async Task<bool> SetNextPayment(String shopId, String currencyCode, Single amount)
        {
            using var httpClient = _clientFactory.CreateClient();
            var requestData = new {
                shopId = shopId,
                currencyCode = currencyCode,
                amount = amount,
            };
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(_apiEntryPoint + "/setnextpayment", jsonContent).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Retrieves the next payment information for the specified shop.
        /// </summary>
        /// <param name="shopId">The unique identifier of the shop (obtained by the hash of QR code) for which to retrieve the next payment information.</param>
        /// <returns>An instance of  containing the currency code and amount of the next payment if available; otherwise null</returns>
        public async Task<JsonDocument?> GetNextPayment(String shopId)
        {
            using var httpClient = _clientFactory.CreateClient();
            var requestData = new {
                shopId = shopId,
            };
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(_apiEntryPoint + "/getnextpayment", jsonContent).ConfigureAwait(false);
            var responseData = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(responseData)) return null;
            return JsonDocument.Parse(responseData);
        }

    }
}