using System;
using System.Diagnostics;
using System.Linq;

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;

namespace CreateImage
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // NOTE:
            // -----
            // Configure your Azure credentials as environment variables in
            // your debug launch profile.

            var subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");

            //-----------------------------------------------------------------
            // Establish the Azure connection.

            var azure = new ArmClient(new DefaultAzureCredential(),
                subscriptionId,
                new ArmClientOptions()
                {
                    Environment = ArmEnvironment.AzurePublicCloud
                });

            var subscription = await azure.GetDefaultSubscriptionAsync();
            var locations    = (await subscription.GetAvailableLocationsAsync()).Value;
        }
    }
}
