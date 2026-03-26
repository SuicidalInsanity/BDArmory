using System;
using System.Reflection;
using UnityEngine;
using BDArmory.Settings;

namespace BDArmory.ModIntegration
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, true)]
    public class B9PartSwitch : MonoBehaviour
    {
        public static B9PartSwitch Instance;
        public static bool hasB9 = false;
        private static bool hasCheckedForB9 = false;
        public static bool hasB9Module = false;
        private static bool hasCheckedForB9Module = false;

        public static Assembly B9PSAssembly;
        public static Type B9PSModule;


        void Awake()
        {
            if (Instance != null) return; // Don't replace existing instance.
            Instance = new B9PartSwitch();
        }

        void Start()
        {
            CheckForB9PS();
            if (hasB9)
            {
                CheckForB9Module();
            }
        }

        public static bool CheckForB9PS()
        {
            if (hasCheckedForB9) return hasB9;
            hasCheckedForB9 = true;
            foreach (var assy in AssemblyLoader.loadedAssemblies)
            {
                if (assy.assembly.FullName.StartsWith("B9PartSwitch"))
                {
                    B9PSAssembly = assy.assembly;
                    hasB9 = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.B9Utils]: Found B9PS Assembly: {B9PSAssembly.FullName}");
                }
            }
            return hasB9;
        }

        public static bool CheckForB9Module()
        {
            if (!hasB9) return false;
            if (hasCheckedForB9Module) return hasB9Module;
            hasCheckedForB9Module = true;
            foreach (var type in B9PSAssembly.GetTypes())
            {
                if (type.Name == "ModuleB9PartSwitch")
                {
                    B9PSModule = type;
                    hasB9Module = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.B9Utils]: Found B9 module type.");
                }
            }
            return hasB9Module;
        }

        public static bool checkForSimpleRepaint(Part part)
        {
            if (!hasB9Module) return false;
            foreach (var module in part.Modules)
            {
                if (module.GetType() == B9PSModule)
                {
                    string SR = (string)B9PSModule.GetField("moduleID", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    return SR == "SimpleRepaint" ? true : false;
                }
            }
            return false;
        }
    }
}
