using System;
using System.Linq;

using Microsoft.Azure.Management.AppService.Fluent.Models;
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
            // Configure your Azure credentials as environment variables in
            // your debug launch profile.

            var subscriptionId    = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTIONID");
            var tenantId          = Environment.GetEnvironmentVariable("AZURE_TENANTID");
            var clientId          = Environment.GetEnvironmentVariable("AZURE_CLIENTID");
            var clientSecret      = Environment.GetEnvironmentVariable("AZURE_CLIENTSECRET");
            var vmUserName        = "ubuntu";
            var vmSize            = "Standard_D4as_v4";
            var vmPassword        = "crappy.password.Aa0";
            var region            = "northeurope";
            var resourceGroupName = "create-image";
            var galleryName       = "test.gallery";
            var imageName         = "test";

            //-----------------------------------------------------------------
            // Establish the Azure connection.

            Console.WriteLine($"AZURE connect");

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
            // Remove any existing resource group and then create a new one.

            if (azure.ResourceGroups.List().Any(resourceGroupItem => resourceGroupItem.Name == resourceGroupName && resourceGroupItem.RegionName == region))
            {
                Console.WriteLine($"Remove existing resource group: {resourceGroupName}");

                await azure.ResourceGroups.DeleteByNameAsync(resourceGroupName);
            }

            Console.WriteLine($"Create resource group: {resourceGroupName}");

            await azure.ResourceGroups
                .Define(resourceGroupName)
                .WithRegion(region)
                .CreateAsync();

            //-----------------------------------------------------------------
            // Prepare the network for the VM.

            Console.WriteLine($"Prepare network");

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

            Console.WriteLine($"Create VM");

            var vm = azure.VirtualMachines
                .Define("vm")
                .WithRegion(region)
                .WithExistingResourceGroup(resourceGroupName)
                .WithExistingPrimaryNetworkInterface(nic)
                .WithLatestLinuxImage(publisher: "Canonical", offer: "0001-com-ubuntu-server-focal", sku: "20_04-lts-gen2" )    // <-- SHOULD BE A GEN2 IMAGE, RIGHT???
                .WithRootUsername(vmUserName)
                .WithRootPassword(vmPassword)
                .WithComputerName("ubuntu")
                .WithSize(vmSize)
                .WithOSDiskSizeInGB(32)
                .WithBootDiagnostics()
                .Create();

            Console.WriteLine($"Wait for VM");

            while (true)
            {
                var vmStatus = await azure.VirtualMachines.GetByIdAsync(vm.Id);

                if (vmStatus.ProvisioningState == "Succeeded")
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            // NOTE: I've confirmed on the portal that the VM is gen2.

            //-----------------------------------------------------------------
            // Power down the VM and generalize it.

            Console.WriteLine($"Power down VM");
            await azure.VirtualMachines.PowerOffAsync(vm.ResourceGroupName, vm.Name);

            Console.WriteLine($"Generalize VM");
            await azure.VirtualMachines.GeneralizeAsync(vm.ResourceGroupName, vm.Name);

            //-----------------------------------------------------------------
            // Create the image gallery if it doesn't already exist.

            Console.WriteLine($"Create image gallery: {galleryName}");

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
            // Create a custom image from the VM.

            Console.WriteLine($"Create custom image: my-image");

            var customImage = await azure.VirtualMachineCustomImages
                .Define("my-image")
                .WithRegion(region)
                .WithExistingResourceGroup(gallery.ResourceGroupName)
                .WithHyperVGeneration(HyperVGenerationTypes.V2)                                                 // <-- Shouldn't this create a gen2 image???
                .WithLinuxFromDisk(vm.OSDiskId, OperatingSystemStateTypes.Generalized)
                .CreateAsync();

            // NOTE: I've verified that this was created as a gen2 image.

            //-----------------------------------------------------------------
            // Create the gallery image if it doesn't already exist.

            Console.WriteLine($"Create gallery image: {imageName}");

            var image = (await azure.GalleryImages.ListByGalleryAsync(gallery.ResourceGroupName, gallery.Name)).SingleOrDefault(image => image.Name == imageName);

            if (image == null)
            {
                image = await azure.GalleryImages
                    .Define(imageName)
                    .WithExistingGallery(gallery)
                    .WithLocation(region)
                    .WithIdentifier(publisher: "test-publisher", offer: "test-offer", sku: "test-sku-gen2")     // <-- NOTE: the SKU ends with "-gen2" if that matters
                    .WithGeneralizedLinux()
                    .WithDescription("This is a test image.")
                    .CreateAsync();
            }

            //-----------------------------------------------------------------
            // Remove any existing image version.

            var imageVersion = (await azure.GalleryImageVersions.ListByGalleryImageAsync(gallery.ResourceGroupName, gallery.Name, imageName)).SingleOrDefault(imageVersion => imageVersion.Name == imageName);

            if (imageVersion != null)
            {
                Console.WriteLine($"Remove existing gallery image version: {imageName}");

                await azure.GalleryImageVersions.DeleteByGalleryImageAsync(gallery.ResourceGroupName, gallery.Name, image.Name, imageVersion.Name);
            }

            //-----------------------------------------------------------------
            // Publish the image version to the gallery.

            Console.WriteLine($"Publish image to gallery as: 1.0.0");

            await azure.GalleryImageVersions
                .Define("1.0.0")
                .WithExistingImage(gallery.ResourceGroupName, gallery.Name, imageName)
                .WithLocation(region)
                .WithSourceCustomImage(customImage.Id)
                .WithRegionAvailability(Region.Create(region), replicaCount: 1)
                .CreateAsync();                                                                                 // <-- throws: Microsoft.Rest.Azure.CloudException

            // NOTE: The CreateAsync() method above throws:
            //
            // TYPE: Microsoft.Rest.Azure.CloudException
            //
            // MESSAGE:
            // Long running operation failed with status 'Failed'.
            // Additional Info:'The resource with id '/subscriptions/f11f67a7-d42b-4d7e-8df1-7017823f1780/resourceGroups/create-image/providers/Microsoft.Compute/images/my-image'
            // has a different Hypervisor generation ['V2'] than the parent gallery image Hypervisor generation ['V1'].'

            // So, somehow using the gen2 custom image created above to create the
            // gallery image version failed because the image version thinks it's
            // gen1.
            //
            // So there must be a way to specify that the gallery image should be gen2, 
            // and I can see that as an option in the Azure portal, but I but I can't figure
            // out how to set this using the API.
        }
    }
}
