﻿using HomeSeerAPI;
using Hspi.Exceptions;
using Hspi.WUWeather;
using Scheduler.Classes;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Hspi
{
    using static Hspi.StringUtil;

    public class WUWeatherPlugin : HspiBase
    {
        public WUWeatherPlugin()
            : base(WUWeatherData.PlugInName)
        {
        }

        public override string InitIO(string port)
        {
            string result = string.Empty;
            try
            {
                pluginConfig = new PluginConfig(HS);
                configPage = new ConfigPage(HS, this.pluginConfig);
                LogInfo("Starting Plugin");
#if DEBUG
                pluginConfig.DebugLogging = true;
#endif
                LogDebug(INV($"APIKey:{pluginConfig.APIKey} Refresh Interval:{pluginConfig.RefreshIntervalMinutes} Minutes"));

                pluginConfig.ConfigChanged += PluginConfig_ConfigChanged;

                RegisterConfigPage();

                RestartPeriodicTask();

                LogDebug("Plugin Started");
            }
            catch (Exception ex)
            {
                result = INV($"Failed to initialize PlugIn With {ex.Message}");

                LogError(result);
            }

            return result;
        }

        private void PluginConfig_ConfigChanged(object sender, EventArgs e)
        {
            RestartPeriodicTask();
        }

        protected override void LogDebug(string message)
        {
            if (pluginConfig.DebugLogging)
            {
                base.LogDebug(message);
            }
        }

        private static string CreateChildAddress(string parentAddress, string childAddress)
        {
            return INV($"{parentAddress}.{childAddress}");
        }

        private DeviceClass CreateDevice(DeviceClass parent, RootDeviceData rootDeviceData, DeviceDataBase deviceData)
        {
            if (rootDeviceData != null)
            {
                LogDebug(INV($"Creating {deviceData.Name} under {rootDeviceData.Name}"));
            }
            else
            {
                LogDebug(INV($"Creating Root {deviceData.Name}"));
            }

            DeviceClass device = null;
            int refId = HS.NewDeviceRef(deviceData.Name);
            if (refId > 0)
            {
                device = (DeviceClass)HS.GetDeviceByRef(refId);
                string address = rootDeviceData != null ? CreateChildAddress(rootDeviceData.Name, deviceData.Name) : deviceData.Name;
                device.set_Address(HS, address);
                device.set_Device_Type_String(HS, deviceData.HSDeviceTypeString);
                var deviceType = new DeviceTypeInfo_m.DeviceTypeInfo();
                deviceType.Device_API = DeviceTypeInfo_m.DeviceTypeInfo.eDeviceAPI.Plug_In;
                deviceType.Device_Type = deviceData.HSDeviceType;

                device.set_DeviceType_Set(HS, deviceType);
                device.set_Interface(HS, Name);
                device.set_InterfaceInstance(HS, string.Empty);
                device.set_Last_Change(HS, DateTime.Now);
                device.set_Location2(HS, parent != null ? parent.get_Name(HS) : deviceData.Name);
                device.set_Location(HS, Name);
                var pairs = deviceData.GetStatusPairs(pluginConfig);
                foreach (var pair in pairs)
                {
                    HS.DeviceVSP_AddPair(refId, pair);
                }

                var gPairs = deviceData.GetGraphicsPairs(pluginConfig);
                foreach (var gpair in gPairs)
                {
                    HS.DeviceVGP_AddPair(refId, gpair);
                }

                device.MISC_Set(HS, Enums.dvMISC.STATUS_ONLY);
                device.MISC_Set(HS, Enums.dvMISC.SHOW_VALUES);
                device.MISC_Clear(HS, Enums.dvMISC.AUTO_VOICE_COMMAND);
                device.MISC_Clear(HS, Enums.dvMISC.SET_DOES_NOT_CHANGE_LAST_CHANGE);
                device.set_Status_Support(HS, false);

                if (parent != null)
                {
                    parent.set_Relationship(HS, Enums.eRelationship.Parent_Root);
                    device.set_Relationship(HS, Enums.eRelationship.Child);
                    device.AssociatedDevice_Add(HS, parent.get_Ref(HS));
                    parent.AssociatedDevice_Add(HS, device.get_Ref(HS));
                }

                HS.SetDeviceValueByRef(refId, deviceData.InitialValue, false);
                HS.SetDeviceString(refId, deviceData.InitialString, false);
            }

            return device;
        }

        private void CreateDevices()
        {
            try
            {
                IDictionary<string, DeviceClass> currentDevices = GetCurrentDevices();

                foreach (var deviceDefinition in WUWeatherData.DeviceDefinitions)
                {
                    this.CancellationToken.ThrowIfCancellationRequested();
                    DeviceClass parentDevice;
                    currentDevices.TryGetValue(deviceDefinition.Name, out parentDevice);

                    foreach (var childDeviceDefinition in deviceDefinition.Children)
                    {
                        this.CancellationToken.ThrowIfCancellationRequested();

                        if (!pluginConfig.GetEnabled(deviceDefinition, childDeviceDefinition))
                        {
                            continue;
                        }

                        if (parentDevice == null)
                        {
                            parentDevice = CreateDevice(null, null, deviceDefinition);
                        }

                        string childAddress = CreateChildAddress(parentDevice.get_Address(HS), childDeviceDefinition.Name);

                        if (!currentDevices.ContainsKey(childAddress))
                        {
                            CreateDevice(parentDevice, deviceDefinition, childDeviceDefinition);
                        }
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogError(INV($"Failed to Create Devices For PlugIn With {ex.Message}"));
            }
        }

        private IDictionary<string, DeviceClass> GetCurrentDevices()
        {
            var deviceEnumerator = HS.GetDeviceEnumerator() as clsDeviceEnumeration;

            if (deviceEnumerator == null)
            {
                throw new HspiException(INV($"{Name} failed to get a device enumerator from HomeSeer."));
            }

            var currentDevices = new Dictionary<string, DeviceClass>();
            do
            {
                this.CancellationToken.ThrowIfCancellationRequested();
                DeviceClass device = deviceEnumerator.GetNext();
                if ((device != null) &&
                    (device.get_Interface(HS) != null) &&
                    (device.get_Interface(HS).Trim() == Name))
                {
                    string address = device.get_Address(HS);
                    currentDevices.Add(address, device);
                }
            } while (!deviceEnumerator.Finished);
            return currentDevices;
        }

        public override string GetPagePlugin(string page, string user, int userRights, string queryString)
        {
            if (page == configPage.Name)
            {
                return configPage.GetWebPage();
            }

            return string.Empty;
        }

        public override string PostBackProc(string page, string data, string user, int userRights)
        {
            if (page == configPage.Name)
            {
                return configPage.PostBackProc(data, user, userRights);
            }

            return string.Empty;
        }

        private void RestartPeriodicTask()
        {
            lock (periodicTaskLock)
            {
                if (periodicTask != null)
                {
                    cancellationTokenSourceForUpdateDevice.Cancel();
                    try
                    {
                        periodicTask.Wait(CancellationToken);
                    }
                    catch (Exception) { }
                    cancellationTokenSourceForUpdateDevice.Dispose();
                }

                cancellationTokenSourceForUpdateDevice = new CancellationTokenSource();
                periodicTask = CreateAndUpdateDevices(); // dont wait
            }
        }

        private async Task CreateAndUpdateDevices()
        {
            using (var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, cancellationTokenSourceForUpdateDevice.Token))
            {
                while (!combinedToken.IsCancellationRequested)
                {
                    try
                    {
                        CreateDevices();
                        await FetchAndUpdateDevices(combinedToken.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        LogWarning(INV($"Failed to Fetch Data with {ex.Message}"));
                    }

                    await Task.Delay(TimeSpan.FromMinutes(pluginConfig.RefreshIntervalMinutes), combinedToken.Token).ConfigureAwait(false);
                }
            }
        }

        private async Task FetchAndUpdateDevices(CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(this.pluginConfig.APIKey) || string.IsNullOrWhiteSpace(this.pluginConfig.StationId))
            {
                LogWarning("Configuration not defained to fetch weather data");
                return;
            }

            WUWeatherService service = new WUWeatherService(pluginConfig.APIKey);
            var response = await service.GetDataForStationAsync(pluginConfig.StationId, true, true, true, token).ConfigureAwait(false);

            var existingDevices = GetCurrentDevices();
            foreach (var deviceDefinition in WUWeatherData.DeviceDefinitions)
            {
                this.CancellationToken.ThrowIfCancellationRequested();

                DeviceClass rootDevice;
                existingDevices.TryGetValue(deviceDefinition.Name, out rootDevice);

                if (rootDevice == null)
                {
                    // no root device exists yet
                    continue;
                }

                var subObject = response.SelectNodes(deviceDefinition.PathData.GetPath(this.pluginConfig.Unit));

                if (subObject != null && subObject.Count != 0)
                {
                    deviceDefinition.UpdateDeviceData(HS, rootDevice, subObject);

                    DateTimeOffset? lastUpdate = deviceDefinition.LastUpdateTime;
                    foreach (var childDeviceDefinition in deviceDefinition.Children)
                    {
                        string childAddress = CreateChildAddress(deviceDefinition.Name, childDeviceDefinition.Name);

                        DeviceClass childDevice;
                        existingDevices.TryGetValue(childAddress, out childDevice);

                        if (childDevice != null)
                        {
                            this.CancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                XmlNodeList elements = subObject.Item(0).SelectNodes(childDeviceDefinition.PathData.GetPath(this.pluginConfig.Unit));
                                childDeviceDefinition.UpdateDeviceData(HS, childDevice, elements);

                                if (lastUpdate.HasValue)
                                {
                                    childDevice.set_Last_Change(HS, lastUpdate.Value.DateTime);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                            }
                            catch (Exception ex)
                            {
                                LogError($"Failed to update {childAddress} with {ex.Message}");
                            }
                        }
                    }
                }
            }
        }

        private void RegisterConfigPage()
        {
            string link = configPage.Name;
            HS.RegisterPage(link, Name, string.Empty);

            HomeSeerAPI.WebPageDesc wpd = new HomeSeerAPI.WebPageDesc();
            wpd.plugInName = Name;
            wpd.link = link;
            wpd.linktext = link;
            wpd.page_title = $"{Name} Config";
            Callback.RegisterConfigLink(wpd);
            Callback.RegisterLink(wpd);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (pluginConfig != null)
                {
                    pluginConfig.ConfigChanged -= PluginConfig_ConfigChanged;
                }
                cancellationTokenSourceForUpdateDevice.Dispose();
                if (configPage != null)
                {
                    configPage.Dispose();
                }

                disposedValue = true;
            }

            base.Dispose(disposing);
        }

        private CancellationTokenSource cancellationTokenSourceForUpdateDevice = new CancellationTokenSource();
        private Task periodicTask;
        private readonly object periodicTaskLock = new object();
        private ConfigPage configPage;
        private PluginConfig pluginConfig;
        private bool disposedValue = false;
    }
}