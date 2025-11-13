using ClickPay.Wallet.Core;
using Microsoft.Extensions.Configuration;

namespace API
{
    public static class ApiGlobals
    {
        public static ClickPayServerAPIClient? Instance { get; private set; }

        public static void Initialize(IConfiguration config)
        {
            var entryPoint = config["Api:EntryPoint"];
            Instance = new ClickPayServerAPIClient(entryPoint!);
        }
    }
}
