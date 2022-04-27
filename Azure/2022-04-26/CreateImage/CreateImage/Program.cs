using System;
using System.Linq;

using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.Storage.Fluent;

namespace CreateImage
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // NOTE:
            // -----
            // Configure your Azure credentials as environment variables in your debug
            // launch profile.

            var subscriptionId    = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTIONID");
            var tenantId          = Environment.GetEnvironmentVariable("AZURE_TENANTID");
            var clientId          = Environment.GetEnvironmentVariable("AZURE_CLIENTID");
            var clientSecret      = Environment.GetEnvironmentVariable("AZURE_CLIENTSECRET");
            var vmUserName        = "ubuntu";
            var vmSize            = "Standard_D4ads_v5";
            var vmPassword        = "crappy.password.Aa0";
            var region            = "westus";
            var resourceGroupName = "create-image-rg";
            var galleryName       = "test-gallery";
            var imageName         = "test";
            var imageRef          = new ImageReference()
            {
                Publisher = "Canonical",
                Offer     = "0001-com-ubuntu-server-focal",
                Sku       = "20_04-lts-gen2",
                Version   = "20.04.202204190"
            };

            //-----------------------------------------------------------------
            // Establish the Azure connection.

            var azureCredentials =
                new AzureCredentials(
                    new ServicePrincipalLoginInformation()
                    {
                        ClientId     = clientId,
                        ClientSecret = clientSecret
                    },
                    tenantId:    tenantId,
                    environment: AzureEnvironment.AzureGlobalCloud);

            var azure = Azure.Configure()
                .Authenticate(azureCredentials)
                .WithSubscription(subscriptionId);

            //-----------------------------------------------------------------
            // Remove any existing resource group and then create a fresh group.

            if (azure.ResourceGroups.List().Any(resourceGroupItem => resourceGroupItem.Name == resourceGroupName && resourceGroupItem.RegionName == region))
            {
                await azure.ResourceGroups.DeleteByNameAsync(resourceGroupName);
            }

            await azure.ResourceGroups
                .Define(resourceGroupName)
                .WithRegion(region)
                .CreateAsync();

            //-----------------------------------------------------------------
            // Prepare network settings for the VM.

            var publicAddress = await azure.PublicIPAddresses
                .Define("public-address")
                .WithRegion(region)
                .WithExistingResourceGroup(resourceGroupName)
                .WithSku(PublicIPSkuType.Standard)
                .WithStaticIP()
                .CreateAsync();

            var network = azure.Networks
                .Define("network")
                .WithRegion(region)
                .WithExistingResourceGroup(resourceGroupName)
                .WithAddressSpace("10.0.0.0/24")
                .WithSubnet("subnet", "10.0.0.0/24")
                .Create();

            var nsg = azure.NetworkSecurityGroups
                .Define("nsg")
                .WithRegion(region)
                .WithExistingResourceGroup(resourceGroupName)
                .DefineRule("Allow-SSH")
                    .AllowInbound()
                    .FromAnyAddress()
                    .FromAnyPort()
                    .ToAnyAddress()
                    .ToPort(22)
                    .WithProtocol(SecurityRuleProtocol.Tcp)
                    .WithPriority(100)
                    .Attach()
                .Create();

            var nic = azure.NetworkInterfaces
                .Define("nic")
                .WithRegion(region)
                .WithExistingResourceGroup(resourceGroupName)
                .WithExistingPrimaryNetwork(network)
                .WithSubnet("subnet")
                .WithPrimaryPrivateIPAddressDynamic()
                .WithExistingPrimaryPublicIPAddress(publicAddress)
                .WithExistingNetworkSecurityGroup(nsg)
                .Create();

            //-----------------------------------------------------------------
            // Start the VM and wait until it's ready.

            var vm = azure.VirtualMachines
                .Define("vm")
                .WithRegion(region)
                .WithExistingResourceGroup(resourceGroupName)
                .WithExistingPrimaryNetworkInterface(nic)
                .WithSpecificLinuxImageVersion(imageRef)
                .WithRootUsername(vmUserName)
                .WithRootPassword(vmPassword)
                .WithComputerName("ubuntu")
                .WithOSDiskStorageAccountType(StorageAccountTypes.StandardSSDLRS)
                //.WithAvailabilityZone(AvailabilityZoneId.Zone_3)
                .WithSize(vmSize)
                .WithOSDiskSizeInGB(32)
                .WithBootDiagnostics()
                .Create();

            while (true)
            {
                var vmStatus = await azure.VirtualMachines.GetByIdAsync(vm.Id);

                if (vmStatus.ProvisioningState == "")
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            //-----------------------------------------------------------------
            // Power down the VM and generalize it.

            await azure.VirtualMachines.PowerOffAsync(vm.ResourceGroupName, vm.Name);
            await azure.VirtualMachines.GeneralizeAsync(vm.ResourceGroupName, vm.Name);

            //-----------------------------------------------------------------
            // Create the image gallery if it doesn't already exist.

            var gallery = (await azure.Galleries.ListAsync()).SingleOrDefault(gallery => gallery.Name == galleryName);

            if (gallery == null)
            {
                gallery = await azure.Galleries
                    .Define(galleryName)
                    .WithRegion(region)
                    .WithExistingResourceGroup(resourceGroupName)
                    .WithDescription("test gallery")
                    .CreateAsync();
            }

            //-----------------------------------------------------------------
            // Create the gallery image if it doesn't already exist.

            var image = (await azure.GalleryImages.ListByGalleryAsync(gallery.ResourceGroupName, gallery.Name)).SingleOrDefault(image => image.Name == imageName);

            if (image == null)
            {
                image = await azure.GalleryImages
                    .Define(imageName)
                    .WithExistingGallery(gallery)
                    .WithLocation(region)
                    .WithIdentifier("test-publisher", "test-offer", "test-sku")
                    .WithGeneralizedLinux()
                    .WithDescription("This is a test image.")
                    .CreateAsync();
            }

            //-----------------------------------------------------------------
            // Remove any existing image version.

            var imageVersion = (await azure.GalleryImageVersions.ListByGalleryImageAsync(gallery.ResourceGroupName, gallery.Name, imageName)).SingleOrDefault(imageVersion => imageVersion.Name == imageName);

            if (imageVersion != null)
            {
                await azure.GalleryImageVersions.DeleteByGalleryImageAsync(gallery.ResourceGroupName, gallery.Name, image.Name, imageVersion.Name);
            }

            //-----------------------------------------------------------------
            // Create a custom image from the VM.

            var customImage = await azure.VirtualMachineCustomImages
                .Define("my-image")
                .WithRegion(region)
                .WithExistingResourceGroup(gallery.ResourceGroupName)
                .WithHyperVGeneration(HyperVGenerationTypes.V2)
                .WithLinuxFromDisk(vm.OSDiskId, OperatingSystemStateTypes.Generalized)
                .CreateAsync();

            //-----------------------------------------------------------------
            // Publish the image version to the gallery.

            await azure.GalleryImageVersions
                .Define("1.0.0")
                .WithExistingImage(gallery.ResourceGroupName, gallery.Name, image.Name)
                .WithLocation(region)
                .WithSourceCustomImage(customImage.Id)
                .WithRegionAvailability(Region.Create(region), replicaCount: 1)
                .CreateAsync();
        }
    }
}
