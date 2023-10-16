// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using System.Net;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;
using System.Net.NetworkInformation;
namespace ManageVirtualMachineWithDisk
{
    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;

        /**
         * Azure Compute sample for managing virtual machines -
         *  - Create a virtual machine with
         *      - Implicit data disks
         *      - Creatable data disks
         *      - Existing data disks
         *  - Update a virtual machine
         *      - Attach data disks
         *      - Detach data disks
         *  - Stop a virtual machine
         *  - Update a virtual machine
         *      - Expand the OS disk
         *      - Expand data disks.
         */
        public static async Task RunSample(ArmClient client)
        {
            var linuxVmName1 = Utilities.CreateRandomName("VM1");
            var rgName = Utilities.CreateRandomName("rgCOMV");
            var publicIpDnsLabel = Utilities.CreateRandomName("pip");

            try
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                //============================================================
                // Creates an empty data disk to attach to the virtual machine

                Utilities.Log("Creating an empty managed disk");

                var dataDisk1 = azure.Disks.Define(Utilities.CreateRandomName("dsk-"))
                        .WithRegion(region)
                        .WithNewResourceGroup(rgName)
                        .WithData()
                        .WithSizeInGB(50)
                        .Create();

                Utilities.Log("Created managed disk");

                // Prepare first creatable data disk
                //
                var dataDiskCreatable1 = azure.Disks.Define(Utilities.CreateRandomName("dsk-"))
                        .WithRegion(region)
                        .WithExistingResourceGroup(rgName)
                        .WithData()
                        .WithSizeInGB(100);

                // Prepare second creatable data disk
                //
                var dataDiskCreatable2 = azure.Disks.Define(Utilities.CreateRandomName("dsk-"))
                        .WithRegion(region)
                        .WithExistingResourceGroup(rgName)
                        .WithData()
                        .WithSizeInGB(50)
                        .WithSku(DiskSkuTypes.StandardLRS);

                //======================================================================
                // Create a Linux VM using a PIR image with managed OS and Data disks

                Utilities.Log("Creating a managed Linux VM");

                var linuxVM = azure.VirtualMachines.Define(linuxVmName1)
                        .WithRegion(region)
                        .WithNewResourceGroup(rgName)
                        .WithNewPrimaryNetwork("10.0.0.0/28")
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithNewPrimaryPublicIPAddress(publicIpDnsLabel)
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(userName)
                        .WithRootPassword(password)

                        // Begin: Managed data disks
                        .WithNewDataDisk(100)
                        .WithNewDataDisk(100, 1, CachingTypes.ReadWrite)
                        .WithNewDataDisk(dataDiskCreatable1)
                        .WithNewDataDisk(dataDiskCreatable2, 2, CachingTypes.ReadOnly)
                        .WithExistingDataDisk(dataDisk1)

                        // End: Managed data disks
                        .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                        .Create();

                Utilities.Log("Created a Linux VM with managed OS and data disks: " + linuxVM.Id);
                Utilities.PrintVirtualMachine(linuxVM);

                //======================================================================
                // Update the virtual machine by detaching two data disks with lun 3 and 4 and adding one

                Utilities.Log("Updating Linux VM");

                var lun3DiskId = linuxVM.DataDisks[3].Id;

                linuxVM.Update()
                        .WithoutDataDisk(3)
                        .WithoutDataDisk(4)
                        .WithNewDataDisk(200)
                        .Apply();

                Utilities.Log("Updated Linux VM: " + linuxVM.Id);
                Utilities.PrintVirtualMachine(linuxVM);

                // ======================================================================
                // Delete a managed disk

                var disk = azure.Disks.GetById(lun3DiskId);
                Utilities.Log("Delete managed disk: " + disk.Id);

                azure.Disks.DeleteByResourceGroup(disk.ResourceGroupName, disk.Name);

                Utilities.Log("Deleted managed disk");

                //======================================================================
                // Deallocate the virtual machine

                Utilities.Log("De-allocate Linux VM");

                linuxVM.Deallocate();

                Utilities.Log("De-allocated Linux VM");

                //======================================================================
                // Resize the OS and Data Disks

                var osDisk = azure.Disks.GetById(linuxVM.OSDiskId);
                var dataDisks = new List<IDisk>();
                foreach (var vmDataDisk  in  linuxVM.DataDisks.Values)
                {
                    var dataDisk = azure.Disks.GetById(vmDataDisk.Id);
                    dataDisks.Add(dataDisk);
                }

                Utilities.Log("Update OS disk: " + osDisk.Id);

                osDisk.Update()
                        .WithSizeInGB(2 * osDisk.SizeInGB)
                        .Apply();

                Utilities.Log("OS disk updated");

                foreach (var dataDisk in dataDisks)
                {
                    Utilities.Log("Update data disk: " + dataDisk.Id);

                    dataDisk.Update()
                            .WithSizeInGB(dataDisk.SizeInGB + 10)
                            .Apply();

                    Utilities.Log("Data disk updated");
                }

                //======================================================================
                // Starting the virtual machine

                Utilities.Log("Starting Linux VM");

                linuxVM.Start();

                Utilities.Log("Started Linux VM");
                Utilities.PrintVirtualMachine(linuxVM);
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group: {_resourceGroupId}");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception g)
                {
                    Utilities.Log(g);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception e)
            {
                Utilities.Log(e);
            }
        }
    }
}
