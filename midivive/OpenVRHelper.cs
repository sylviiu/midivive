using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Valve.VR;

namespace MidiVive
{
    class OpenVRHelper
    {
        private OpenVRHelper()
        {

        }
        private static bool initialized = false;
        public static bool InitHMD()
        {
            if (initialized)
                return false; //Already initialized

            EVRInitError error = EVRInitError.None;
            CVRSystem vrSystem = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);

            if (error != EVRInitError.None)
            {
                Console.WriteLine("OpenVR init error: " + error);
                return false;
            }
            //vrSystem.ResetSeatedZeroPose();

            {
                string filePath = string.Concat(Directory.GetCurrentDirectory(), "\\bindings\\actions.json");
                if (!File.Exists(filePath))
                {
                    Console.WriteLine("File {0} doesn't exist!", filePath);
                    return false;
                }
                EVRInputError err = OpenVR.Input.SetActionManifestPath(filePath);
                if (err != EVRInputError.None)
                {
                    Console.WriteLine("OpenVR SetActionManifestPath error: {0}", err);
                    return false;
                }
            }
            if (!EnumerateDevices())
            {
                return false;
            }
            vibrationHandle = 0;
            {
                EVRInputError err = OpenVR.Input.GetActionHandle("/actions/default/out/haptic", ref vibrationHandle);
                if (err != EVRInputError.None)
                {
                    Console.WriteLine("OpenVR GetActionHandle error: {0}", err);
                    return false;
                }
            }
            
            InitActionSet();
            int periodMS = 500;
            Timer timer = new Timer(Refresh, null, 0, periodMS);
            Refresh(null);
            //wait for the timer to start
            Thread.Sleep(periodMS);
            initialized = true;
            return true;
        }

        public class Device
        {
            public ulong handle { get; set; }
            public string path { get; set; }
            public int index { get; set; }
            public Device(ulong handle, string path, int index)
            {
                this.handle = handle;
                this.path = path;
                this.index = index;
            }
        }

        public static List<Device> DeviceHandles = new List<Device>();

        private static ulong vibrationHandle = 0;

        public static void PlayNote(Device controller, float duration, float frequency, float volume)
        {
            if (!initialized)
            {
                Console.WriteLine("PlayNote: OpenVR not initialized");
                return;
            }

            if (controller.index > DeviceCount() - 1)
            {
                Console.WriteLine("PlayNote: device {0} not found", controller);
                return;
            }

            Console.WriteLine("PlayNote: device {0}/{1} (at path {2})", controller.index, DeviceCount() - 1, controller.path);
            EVRInputError err = OpenVR.Input.TriggerHapticVibrationAction(vibrationHandle, 0, duration, frequency, volume, controller.handle);
            Refresh(null);

            if (err != EVRInputError.None)
            {
                Console.WriteLine("OpenVR output error: {0}", err);
                return;
            }
        }

        public static int DeviceCount()
        {
            return DeviceHandles.Count;
        }


        private static VRActiveActionSet_t[] actionSet;

        private static uint actionSetSize = Convert.ToUInt32(Marshal.SizeOf(typeof(VRActiveActionSet_t)));

        private static void InitActionSet()
        {
            ulong handleLegacy = 0;
            OpenVR.Input.GetActionSetHandle("/actions/default", ref handleLegacy);
            actionSet = new VRActiveActionSet_t[1];
            actionSet[0] = new VRActiveActionSet_t
            {
                ulActionSet = handleLegacy,
                ulRestrictedToDevice = 0
            };
        }

        private static void Refresh(object state)
        {
            EVRInputError err = OpenVR.Input.UpdateActionState(actionSet, actionSetSize);
            if (err != EVRInputError.None)
            {
                Console.WriteLine("OpenVR UpdateActionState error: {0}", err);
            }
        }

        public static void Shutdown()
        {
            OpenVR.Shutdown();
        }

        /// <summary>
        /// Lists all connected VR controllers that have actuators
        /// </summary>
        private static bool EnumerateDevices()
        {
            
            for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                //ETrackedDeviceProperty.Prop_RegisteredDeviceType_String returns htc/vive_controllerLHR-XXXXXXX
                string path = GetTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_RegisteredDeviceType_String);
                if (path == null)
                    continue;
                Console.WriteLine(i);
                Console.WriteLine(path);
                path = "/devices/" + path;
                Console.WriteLine("Found device {0} with path {1}", i, path);
                ulong handle = 0;
                EVRInputError err = OpenVR.Input.GetInputSourceHandle(path, ref handle);
                if (err != EVRInputError.None)
                {
                    Console.WriteLine("OpenVR GetInputSourceHandle error: {0}", err);
                    return false;
                }
                //create device class instance
                Device controller = new Device(handle, path, DeviceHandles.Count);
                DeviceHandles.Add(controller);
                Console.WriteLine("Added device \"{0}\" with handle {1} at index {2} (loop {3})", path, handle, controller.index, i);
            }
            return true;
        }
        public static string GetTrackedDeviceProperty(uint index, ETrackedDeviceProperty property)
        {
            var error = ETrackedPropertyError.TrackedProp_Success;
            uint len = OpenVR.System.GetStringTrackedDeviceProperty(index, property, null, 0, ref error);
            if (len > 1)
            {
                var result = new StringBuilder((int)len);
                OpenVR.System.GetStringTrackedDeviceProperty(index, property, result, len, ref error);
                return result.ToString();
            }
            return null;
        }
    }
}
