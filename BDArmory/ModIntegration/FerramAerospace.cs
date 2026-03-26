using System;
using System.Reflection;
using UnityEngine;
using BDArmory.Settings;

namespace BDArmory.ModIntegration
{
    [KSPAddon(KSPAddon.Startup.FlightAndEditor, true)]
    public class FerramAerospace : MonoBehaviour
    {
        public static FerramAerospace Instance;
        public static bool hasFAR = false;
        private static bool hasCheckedForFAR = false;
        public static bool hasFARWing = false;
        public static bool hasFARControllableSurface = false;
        private static bool hasCheckedForFARWing = false;
        private static bool hasCheckedForFARControllableSurface = false;

        public static Assembly FARAssembly;
        public static Type FARWingModule;
        public static Type FARControllableSurfaceModule;


        void Awake()
        {
            if (Instance != null) return; // Don't replace existing instance.
            Instance = new FerramAerospace();
        }

        void Start()
        {
            CheckForFAR();
            if (hasFAR)
            {
                CheckForFARWing();
                CheckForFARControllableSurface();
            }
        }

        public static bool CheckForFAR()
        {
            if (hasCheckedForFAR) return hasFAR;
            hasCheckedForFAR = true;
            foreach (var assy in AssemblyLoader.loadedAssemblies)
            {
                if (assy.assembly.FullName.StartsWith("FerramAerospaceResearch"))
                {
                    FARAssembly = assy.assembly;
                    hasFAR = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found FAR Assembly: {FARAssembly.FullName}");
                }
            }
            return hasFAR;
        }

        public static bool CheckForFARWing()
        {
            if (!hasFAR) return false;
            if (hasCheckedForFARWing) return hasFARWing;
            hasCheckedForFARWing = true;
            foreach (var type in FARAssembly.GetTypes())
            {
                if (type.Name == "FARWingAerodynamicModel")
                {
                    FARWingModule = type;
                    hasFARWing = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found FAR wing module type.");
                }
            }
            return hasFARWing;
        }

        public static bool CheckForFARControllableSurface()
        {
            if (!hasFAR) return false;
            if (hasCheckedForFARControllableSurface) return hasFARControllableSurface;
            hasCheckedForFARControllableSurface = true;
            foreach (var type in FARAssembly.GetTypes())
            {
                if (type.Name == "FARControllableSurface")
                {
                    FARControllableSurfaceModule = type;
                    hasFARControllableSurface = true;
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found FAR controllable surface module type.");
                }
            }
            return hasFARControllableSurface;
        }

        public static float GetFARMassMult(Part part)
        {
            if (!hasFARWing) return 1;

            foreach (var module in part.Modules)
            {
                if (module.GetType() == FARWingModule)
                {
                    var massMultiplier = (float)FARWingModule.GetField("massMultiplier", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found wing Mass multiplier of {massMultiplier} for {part.name}.");
                    return massMultiplier;
                }
                if (module.GetType() == FARControllableSurfaceModule)
                {
                    var massMultiplier = (float)FARControllableSurfaceModule.GetField("massMultiplier", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found ctrl. srf. Mass multiplier of {massMultiplier} for {part.name}.");
                    return massMultiplier;
                }
            }
            return 1;
        }
        public static float GetFARcurrWingMass(Part part)
        {
            if (!hasFARWing) return -1;
            foreach (var module in part.Modules)
            {
                if (module.GetType() == FARWingModule)
                {
                    var wingMass = (float)FARWingModule.GetField("curWingMass", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found wing Mass of {wingMass} for {part.name}.");
                    return wingMass;
                }
                if (module.GetType() == FARControllableSurfaceModule)
                {
                    var wingMass = (float)FARControllableSurfaceModule.GetField("curWingMass", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found ctrl. srf. Mass multiplier of {wingMass} for {part.name}.");
                    return wingMass;
                }
            }
            return -1;
        }
        public static double GetFARWingSweep(Part part)
        {
            if (!hasFARWing) return 0;

            foreach (var module in part.Modules)
            {
                if (module.GetType() == FARWingModule)
                {
                    var sweep = (double)FARWingModule.GetField("MidChordSweep", BindingFlags.Public | BindingFlags.Instance).GetValue(module); //leading + trailing angle / 2
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found mid chord sweep of {sweep} for {part.name}.");
                    return sweep;
                }
                if (module.GetType() == FARControllableSurfaceModule)
                {
                    var sweep = (double)FARControllableSurfaceModule.GetField("MidChordSweep", BindingFlags.Public | BindingFlags.Instance).GetValue(module);
                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.FerramAerospace]: Found ctrl. srf. mid chord sweep of {sweep} for {part.name}.");
                    return sweep;
                }
            }
            return 0;
        }
    }
}
