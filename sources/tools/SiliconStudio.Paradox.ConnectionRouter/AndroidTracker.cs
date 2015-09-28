﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiliconStudio.Core.Diagnostics;
using SiliconStudio.Paradox.Engine.Network;

namespace SiliconStudio.Paradox.ConnectionRouter
{
    /// <summary>
    /// Track android devices (with adb.exe) and establish port mapping.
    /// </summary>
    static class AndroidTracker
    {
        private static readonly Logger Log = GlobalLogger.GetLogger("AndroidTracker");

        public static void TrackDevices(Router router)
        {
            var currentAndroidDevices = new Dictionary<string, ConnectedDevice>();

            // Start new devices
            int startLocalPort = 51153; // Use ports in the dynamic port range

            // Check if ADB is on the path
            var adbPath = AndroidDeviceEnumerator.GetAdbPath();
            if (adbPath == null)
                return;

            // Wait and process android device changes events
            while (true)
            {
                // Fill list of android devices
                var newAndroidDevices = new Dictionary<string, string>();
                foreach (var device in AndroidDeviceEnumerator.ListAndroidDevices())
                {
                    newAndroidDevices.Add(device.Serial, string.Format("{0} ({1})", device.Name, device.Serial));
                }

                DeviceHelper.UpdateDevices(Log, newAndroidDevices, currentAndroidDevices, (connectedDevice) =>
                {
                    // First, try adb reverse port mapping (supported on newest adb+device)
                    // This is the best solution, as nothing specific needs to be done.
                    // NOTE: disabled for now, as it's difficult to know what to try first from the device itself.
                    //var output = ShellHelper.RunProcessAndGetOutput(AndroidDeviceEnumerator.GetAdbPath(), string.Format(@"-s {0} reverse tcp:{1} tcp:{2}", newAndroidDevice, LocalPort, LocalPort));
                    //if (output.ExitCode == 0)
                    //    continue;

                    // Setup adb port forward (tries up to 5 times for open ports)
                    int localPort = 0;
                    int firstTestedLocalPort = startLocalPort;
                    for (int i = 0; i < 4; ++i)
                    {
                        int testedLocalPort = startLocalPort++;
                        if (startLocalPort >= 65536) // Make sure we stay in the range of dynamic ports: 49152-65535
                            startLocalPort = 49152;

                        var output = ShellHelper.RunProcessAndGetOutput(adbPath, string.Format(@"-s {0} forward tcp:{1} tcp:{2}", connectedDevice.Key, testedLocalPort, RouterClient.DefaultListenPort));

                        if (output.ExitCode == 0)
                        {
                            localPort = testedLocalPort;
                            Log.Info("Device connected: {0}; successfully mapped port {1}:{2}", connectedDevice.Name, testedLocalPort, RouterClient.DefaultListenPort);
                            break;
                        }
                    }

                    if (localPort == 0)
                    {
                        int lastTestedLocalPort = startLocalPort;
                        Log.Info("Device connected: {0}; error when mapping port [{1}-{2}]:{3}", connectedDevice.Name, firstTestedLocalPort, lastTestedLocalPort - 1, RouterClient.DefaultListenPort);
                        return;
                    }

                    // Launch a client thread that will automatically tries to connect to this port
                    Task.Run(() => DeviceHelper.LaunchPersistentClient(connectedDevice, router, "localhost", localPort));
                });

                Thread.Sleep(1000); // Detect new devices every 1000 msec
            }
        }
    }
}