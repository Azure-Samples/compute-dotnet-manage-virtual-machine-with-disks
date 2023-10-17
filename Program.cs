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
using System.Xml.Linq;

namespace ManageVirtualMachineWithDisk
{
    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;

        /**
         * Azure Compute sample for managing virtual machines -
         *  - Create a virtual machine with
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
            string linuxVmName1 = Utilities.CreateRandomName("VM1");
            string rgName = Utilities.CreateRandomName("ComputeSampleRG");
            string vnetName = Utilities.CreateRandomName("vnet");
            string nicName = Utilities.CreateRandomName("nic");
            string publicIpDnsLabel = Utilities.CreateRandomName("pip");
            string osDiskName = Utilities.CreateRandomName("OsDisk");
            string diskName1 = Utilities.CreateRandomName("disk1-");
            string diskName2 = Utilities.CreateRandomName("disk2-");
            string diskName3 = Utilities.CreateRandomName("disk3-");

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

                Utilities.Log("Creating three empty managed disk");
                ManagedDiskData diskInput = new ManagedDiskData(resourceGroup.Data.Location)
                {
                    Sku = new DiskSku()
                    {
                        Name = DiskStorageAccountType.StandardLrs
                    },
                    CreationData = new DiskCreationData(DiskCreateOption.Empty),
                    DiskSizeGB = 50,
                };
                var diskLro1 = await resourceGroup.GetManagedDisks().CreateOrUpdateAsync(WaitUntil.Completed, diskName1, diskInput);
                ManagedDiskResource disk1 = diskLro1.Value;
                Utilities.Log($"Created managed disk: {disk1.Data.Name}");

                var diskLro2 = await resourceGroup.GetManagedDisks().CreateOrUpdateAsync(WaitUntil.Completed, diskName2, diskInput);
                ManagedDiskResource disk2 = diskLro2.Value;
                Utilities.Log($"Created managed disk: {disk2.Data.Name}");

                var diskLro3 = await resourceGroup.GetManagedDisks().CreateOrUpdateAsync(WaitUntil.Completed, diskName3, diskInput);
                ManagedDiskResource disk3 = diskLro3.Value;
                Utilities.Log($"Created managed disk: {disk3.Data.Name}");

                //======================================================================
                // Create a Linux VM using a PIR image with managed OS and Data disks

                Utilities.Log("Pre-creating some resources that the VM depends on");

                // Creating a virtual network
                var vnet = await Utilities.CreateVirtualNetwork(resourceGroup, vnetName);

                // Creating public ip
                var pip = await Utilities.CreatePublicIP(resourceGroup, publicIpDnsLabel);

                // Creating network interface
                var nic = await Utilities.CreateNetworkInterface(resourceGroup, vnet.Data.Subnets[0].Id, pip.Id, nicName);

                Utilities.Log("Creating a managed Linux VM");
                VirtualMachineData linuxVMInput = new VirtualMachineData(resourceGroup.Data.Location)
                {
                    HardwareProfile = new VirtualMachineHardwareProfile()
                    {
                        VmSize = VirtualMachineSizeType.StandardF2
                    },
                    StorageProfile = new VirtualMachineStorageProfile()
                    {
                        ImageReference = new ImageReference()
                        {
                            Publisher = "Canonical",
                            Offer = "UbuntuServer",
                            Sku = "16.04-LTS",
                            Version = "latest",
                        },
                    },
                    OSProfile = new VirtualMachineOSProfile()
                    {
                        AdminUsername = Utilities.CreateUsername(),
                        AdminPassword = Utilities.CreatePassword(),
                        ComputerName = linuxVmName1,
                    },
                    NetworkProfile = new VirtualMachineNetworkProfile() { },
                };
                linuxVMInput.NetworkProfile.NetworkInterfaces.Add(
                    new VirtualMachineNetworkInterfaceReference()
                    {
                        Id = nic.Id,
                        Primary = true,
                    });

                // Managed data disks
                linuxVMInput.StorageProfile.OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                {
                    Name = osDiskName,
                    OSType = SupportedOperatingSystemType.Linux,
                    Caching = CachingType.ReadWrite,
                    ManagedDisk = new VirtualMachineManagedDisk()
                    {
                        StorageAccountType = StorageAccountType.StandardLrs
                    }
                };
                linuxVMInput.StorageProfile.DataDisks.Add(new VirtualMachineDataDisk(3, DiskCreateOptionType.Attach)
                {
                    Name = disk1.Data.Name,
                    Caching = CachingType.ReadOnly,
                    DiskSizeGB = 100,
                    ManagedDisk = new VirtualMachineManagedDisk()
                    {
                        Id = disk1.Id,
                    }
                });
                linuxVMInput.StorageProfile.DataDisks.Add(new VirtualMachineDataDisk(4, DiskCreateOptionType.Attach)
                {
                    Name = disk2.Data.Name,
                    Caching = CachingType.ReadWrite,
                    DiskSizeGB = 100,
                    ManagedDisk = new VirtualMachineManagedDisk()
                    {
                        Id = disk2.Id,
                    }
                });
                linuxVMInput.StorageProfile.DataDisks.Add(new VirtualMachineDataDisk(10, DiskCreateOptionType.Attach)
                {
                    Name = disk3.Data.Name,
                    Caching = CachingType.ReadWrite,
                    WriteAcceleratorEnabled = false,
                    ManagedDisk = new VirtualMachineManagedDisk()
                    {
                        Id = disk3.Id,
                        StorageAccountType = StorageAccountType.StandardLrs
                    }
                });
                var linuxVmLro = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, linuxVmName1, linuxVMInput);
                VirtualMachineResource linuxVM = linuxVmLro.Value;

                Utilities.Log("Created a Linux VM with managed OS and data disks: " + linuxVM.Id.Name);
                Utilities.Log("List all data disks status:");
                foreach (var item in linuxVM.Data.StorageProfile.DataDisks)
                {
                    Utilities.Log($"\t{item.Name}--{item.CreateOption.ToString()}");
                }

                //======================================================================
                // Update the virtual machine by detaching two data disks with lun 3 and 4

                Utilities.Log("Updating Linux VM");
                Utilities.Log("Detaching two data disks with lun 3 and 4...");

                VirtualMachineData updateVmInput = linuxVM.Data;
                updateVmInput.StorageProfile.DataDisks.Remove(updateVmInput.StorageProfile.DataDisks.Where(item => item.Lun == 3).First());
                updateVmInput.StorageProfile.DataDisks.Remove(updateVmInput.StorageProfile.DataDisks.Where(item => item.Lun == 4).First());
                linuxVmLro = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, linuxVmName1, updateVmInput);
                linuxVM = linuxVmLro.Value;

                Utilities.Log("Updated Linux VM: " + linuxVM.Id.Name);
                Utilities.Log("List all data disks status:");
                foreach (var item in linuxVM.Data.StorageProfile.DataDisks)
                {
                    Utilities.Log($"\t{item.Name}--{item.CreateOption.ToString()}");
                }

                // ======================================================================
                // Delete a managed disk

                Utilities.Log("Delete managed disk: " + disk1.Id.Name);

                await disk1.DeleteAsync(WaitUntil.Completed);

                Utilities.Log("Deleted managed disk");

                //======================================================================
                // Deallocate the virtual machine

                Utilities.Log("De-allocate Linux VM");

                await linuxVM.DeallocateAsync(WaitUntil.Completed);

                Utilities.Log("De-allocated Linux VM");
                Utilities.Log();

                //======================================================================
                // Resize the OS and Data Disks

                Utilities.Log("Upgrade the OS disk size to double its current size, and increase the size of all data disks by 10 GBs...");
                Utilities.Log("Current all disk size:");
                Utilities.Log($"OSDisk size: {linuxVM.Data.StorageProfile.OSDisk.DiskSizeGB}");
                foreach (var item in linuxVM.Data.StorageProfile.DataDisks)
                {
                    Utilities.Log($"Data disk {item.Name}: {item.DiskSizeGB}");
                }

                // Update os disk
                var osDisk = await resourceGroup.GetManagedDisks().GetAsync(osDiskName);
                ManagedDiskData osdiskUpdateInput = osDisk.Value.Data;
                osdiskUpdateInput.DiskSizeGB = osdiskUpdateInput.DiskSizeGB * 2;
                var osDiskLro = await resourceGroup.GetManagedDisks().CreateOrUpdateAsync(WaitUntil.Completed, osDiskName, osdiskUpdateInput);
                Utilities.Log("OSDisk updated");
                Utilities.Log($"OSDisk size: {osDiskLro.Value.Data.DiskSizeGB}");

                // Update data disks
                foreach (var vmDataDisk in updateVmInput.StorageProfile.DataDisks)
                {
                    var dataDisk = await resourceGroup.GetManagedDisks().GetAsync(vmDataDisk.Name);
                    ManagedDiskData dataDiskUpdateInput = dataDisk.Value.Data;
                    dataDiskUpdateInput.DiskSizeGB = dataDiskUpdateInput.DiskSizeGB + 10;
                    var dataDiskLro = await resourceGroup.GetManagedDisks().CreateOrUpdateAsync(WaitUntil.Completed, vmDataDisk.Name, dataDiskUpdateInput);
                    Utilities.Log($"OSDisk {vmDataDisk.Name} updated");
                    Utilities.Log($"Data disk {dataDiskLro.Value.Data.Name}: {dataDiskLro.Value.Data.DiskSizeGB}");
                }

                //======================================================================
                // Starting the virtual machine

                Utilities.Log("Starting Linux VM");

                await linuxVM.PowerOnAsync(WaitUntil.Completed);

                Utilities.Log("Started Linux VM");
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
