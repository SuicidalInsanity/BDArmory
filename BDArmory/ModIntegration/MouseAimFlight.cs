using UnityEngine;
using System;
using System.Reflection;

using BDArmory.Settings;

namespace BDArmory.ModIntegration
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class MouseAimFlight : MonoBehaviour
    {
        public static MouseAimFlight Instance;
        public static bool hasMouseAimFlight = false;

        Type mouseAimFlightType = null;
        object mouseAimFlightInstance = null;
        Func<object, bool> mouseAimFlightActiveFieldGetter = null;
        bool mouseAimActive = false;
        float lastChecked = 0;
        Vessel activeVessel = null;

        void Awake()
        {
            if (Instance is not null) Destroy(Instance);
            Instance = this;
        }

        void Start()
        {
            FindMouseAimFlight();
            if (hasMouseAimFlight)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ModIntegration.MouseAimFlight]: MouseAimFlight mod detected.");
                FindMouseAimFlightModule();
            }
            else
            {
                Destroy(this); // Destroy ourselves to not take up any further CPU cycles.
            }
        }

        void FindMouseAimFlight()
        {
            try
            {
                foreach (var assy in AssemblyLoader.loadedAssemblies)
                {
                    if (assy.assembly.FullName.Contains("MouseAimFlight"))
                    {
                        foreach (var type in assy.assembly.GetTypes())
                        {
                            if (type == null) continue;
                            if (type.Name == "MouseAimVesselModule")
                            {
                                hasMouseAimFlight = true;
                                mouseAimFlightType = type;
                                foreach (var fieldInfo in mouseAimFlightType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    if (fieldInfo != null && fieldInfo.Name == "mouseAimActive")
                                    {
                                        mouseAimFlightActiveFieldGetter = ReflectionUtils.CreateGetter<object, bool>(fieldInfo);
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BDArmory.ModIntegration.MouseAimFlight]: Failed to locate mouseAimActive in MouseAimFlight module: {e.Message}");
                hasMouseAimFlight = false;
                Destroy(this);
            }
        }

        void FindMouseAimFlightModule()
        {
            mouseAimFlightInstance = null;
            activeVessel = FlightGlobals.ActiveVessel;
            lastChecked = 0;
            if (!hasMouseAimFlight || activeVessel == null) return;
            mouseAimFlightInstance = (object)activeVessel.GetComponent(mouseAimFlightType);
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log($"[BDArmory.ModIntegration.MouseAimFlight]: Mouse Aim Flight module {(mouseAimFlightInstance != null ? "" : "not ")}found on {activeVessel.vesselName}");
        }

        bool CheckMouseAimActive()
        {
            lastChecked = Time.realtimeSinceStartup;
            if (FlightGlobals.ActiveVessel != activeVessel) FindMouseAimFlightModule();
            if (mouseAimFlightInstance == null) return false;
            return mouseAimFlightActiveFieldGetter(mouseAimFlightInstance);
        }

        public bool IsMouseAimFlightActive()
        {
            if (!hasMouseAimFlight) return false;
            if (FlightGlobals.ActiveVessel != activeVessel) FindMouseAimFlightModule();
            if (Time.realtimeSinceStartup - lastChecked > 1f) mouseAimActive = CheckMouseAimActive(); // Only check at most once per second unless a vessel switch occurs.
            return mouseAimActive;
        }

        public static bool IsMouseAimActive => hasMouseAimFlight && Instance != null && Instance.IsMouseAimFlightActive();
    }
}