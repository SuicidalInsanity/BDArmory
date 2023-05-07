using System.Collections.Generic;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Utils;

namespace BDArmory.Control
{
    public class BDAirspeedControl : MonoBehaviour //: PartModule
    {
        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "TargetSpeed"),
        //	UI_FloatRange(minValue = 1f, maxValue = 420f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float targetSpeed = 0;
        public float throttleOverride = -1f;
        public bool useBrakes = true;
        public bool allowAfterburner = true;
        public bool forceAfterburner = false;
        public float afterburnerPriority = 50f;
        public bool forceAfterburnerIfMaxThrottle = false;

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "ThrottleFactor"),
        //	UI_FloatRange(minValue = 1f, maxValue = 20f, stepIncrement = .5f, scene = UI_Scene.All)]
        public float throttleFactor = 2f;

        public Vessel vessel;

        AxisGroupsModule axisGroupsModule;
        bool hasAxisGroupsModule = false; // To avoid repeated null checks

        bool controlEnabled;

        private float smoothedAccel = 0; // smoothed acceleration, prevents super fast toggling of afterburner
        bool shouldSetAfterburners = false;
        bool setAfterburnersEnabled = false;

        //[KSPField(guiActive = true, guiName = "Thrust")]
        public float debugThrust;

        public List<MultiModeEngine> multiModeEngines;

        void Start()
        {
            axisGroupsModule = vessel.FindVesselModuleImplementingBDA<AxisGroupsModule>(); // Look for an axis group module.
            if (axisGroupsModule != null) hasAxisGroupsModule = true;
        }

        //[KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "ToggleAC")]
        public void Toggle()
        {
            if (controlEnabled)
            {
                Deactivate();
            }
            else
            {
                Activate();
            }
        }

        public void Activate()
        {
            controlEnabled = true;
            vessel.OnFlyByWire -= AirspeedControl;
            vessel.OnFlyByWire += AirspeedControl;
            multiModeEngines = new List<MultiModeEngine>();
        }

        public void Deactivate()
        {
            controlEnabled = false;
            vessel.OnFlyByWire -= AirspeedControl;
        }

        void AirspeedControl(FlightCtrlState s)
        {
            if (targetSpeed == 0)
            {
                if (useBrakes)
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                SetThrottle(s, 0);
                return;
            }

            float currentSpeed = (float)vessel.srfSpeed;
            float speedError = targetSpeed - currentSpeed;

            float setAccel = speedError * throttleFactor;

            SetAcceleration(setAccel, s);

            if (forceAfterburnerIfMaxThrottle && s.mainThrottle == 1f)
                SetAfterBurners(true);
            else if (shouldSetAfterburners)
                SetAfterBurners(setAfterburnersEnabled);
        }

        void SetAcceleration(float accel, FlightCtrlState s)
        {
            float gravAccel = GravAccel();
            float requestEngineAccel = accel - gravAccel;

            possibleAccel = 0; //gravAccel;

            float dragAccel = 0;
            float engineAccel = MaxEngineAccel(requestEngineAccel, out dragAccel);

            if (throttleOverride >= 0)
            {
                SetThrottle(s, throttleOverride);
                return;
            }
            if (engineAccel == 0)
            {
                SetThrottle(s, accel > 0 ? 1 : 0);
                return;
            }

            requestEngineAccel = Mathf.Clamp(requestEngineAccel, -engineAccel, engineAccel);

            float requestThrottle = (requestEngineAccel - dragAccel) / engineAccel;

            SetThrottle(s, Mathf.Clamp01(requestThrottle));

            //use brakes if overspeeding too much
            if (useBrakes)
            {
                if (requestThrottle < -0.5f)
                {
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                }
                else
                {
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
                }
            }
        }

        /// <summary>
        /// Set the main throttle and the corresponding axis group.
        /// </summary>
        /// <param name="s">The flight control state</param>
        /// <param name="value">The throttle value</param>
        public void SetThrottle(FlightCtrlState s, float value)
        {
            s.mainThrottle = value;
            if (hasAxisGroupsModule)
            {
                axisGroupsModule.UpdateAxisGroup(KSPAxisGroup.MainThrottle, 2f * value - 1f); // Throttle is full-axis: 0—1 throttle maps to -1—1 axis.
            }
        }

        float MaxEngineAccel(float requestAccel, out float dragAccel)
        {
            float maxThrust = 0;
            float finalThrust = 0;
            multiModeEngines.Clear();

            using (var engines = VesselModuleRegistry.GetModules<ModuleEngines>(vessel).GetEnumerator())
                while (engines.MoveNext())
                {
                    if (engines.Current == null) continue;
                    if (!engines.Current.EngineIgnited) continue;

                    MultiModeEngine mme = engines.Current.part.FindModuleImplementing<MultiModeEngine>();
                    if (IsAfterBurnerEngine(mme))
                    {
                        multiModeEngines.Add(mme);
                        mme.autoSwitch = false;
                    }

                    if (mme && mme.mode != engines.Current.engineID) continue;
                    float engineThrust = engines.Current.maxThrust;
                    if (engines.Current.atmChangeFlow)
                    {
                        engineThrust *= engines.Current.flowMultiplier;
                    }
                    maxThrust += Mathf.Max(0f, engineThrust * (engines.Current.thrustPercentage / 100f)); // Don't include negative thrust percentage drives (Danny2462 drives) as they don't contribute to the thrust.

                    finalThrust += engines.Current.finalThrust;
                }

            debugThrust = maxThrust;

            float vesselMass = vessel.GetTotalMass();

            float accel = maxThrust / vesselMass; // This assumes that all thrust is in the same direction.

            float alpha = 0.05f; // Approx 25 frame (0.5s) lag (similar to 50 frames moving average, but with more weight on recent values and much faster to calculate).
            smoothedAccel = smoothedAccel * (1f - alpha) + alpha * accel;

            //estimate drag
            float estimatedCurrentAccel = finalThrust / vesselMass - GravAccel();
            Vector3 vesselAccelProjected = Vector3.Project(vessel.acceleration_immediate, vessel.velocityD.normalized);
            float actualCurrentAccel = vesselAccelProjected.magnitude * Mathf.Sign(Vector3.Dot(vesselAccelProjected, vessel.velocityD.normalized));
            float accelError = (actualCurrentAccel - estimatedCurrentAccel); // /2 -- why divide by 2 here?
            dragAccel = accelError;

            possibleAccel += accel; // This assumes that the acceleration from engines is in the same direction as the original possibleAccel.
            forceAfterburner = forceAfterburner || (afterburnerPriority == 100f);
            allowAfterburner = allowAfterburner && (afterburnerPriority != 0f);

            //use multimode afterburner for extra accel if lacking
            if (allowAfterburner && (forceAfterburner || smoothedAccel < requestAccel * (1.5f / (Mathf.Exp(100f / 27f) - 1f) * (Mathf.Exp(Mathf.Clamp(afterburnerPriority, 0f, 100f) / 27f) - 1f))))
            { shouldSetAfterburners = true; setAfterburnersEnabled = true; }
            else if (!allowAfterburner || (!forceAfterburner && smoothedAccel > requestAccel * (1f + 0.5f / (Mathf.Exp(50f / 25f) - 1f) * (Mathf.Exp(Mathf.Clamp(afterburnerPriority, 0f, 100f) / 25f) - 1f))))
            { shouldSetAfterburners = true; setAfterburnersEnabled = false; }
            else
            { shouldSetAfterburners = false; }
            return accel;
        }

        void SetAfterBurners(bool enable)
        {
            using (var mmes = multiModeEngines.GetEnumerator())
                while (mmes.MoveNext())
                {
                    if (mmes.Current == null) continue;
                    if (enable)
                    {
                        if (mmes.Current.runningPrimary)
                        {
                            mmes.Current.Events["ModeEvent"].Invoke();
                        }
                    }
                    else
                    {
                        if (!mmes.Current.runningPrimary)
                        {
                            mmes.Current.Events["ModeEvent"].Invoke();
                        }
                    }
                }
        }

        private static bool IsAfterBurnerEngine(MultiModeEngine engine)
        {
            if (engine == null)
            {
                return false;
            }
            return engine.primaryEngineID == "Dry" && engine.secondaryEngineID == "Wet";
            //presumably there's a reason this is looking specifically for MMEs with "Wet" and "Dry" as the IDs instead of !String.IsNullOrEmpty(engine.primaryEngineID). To permit only properly configured Jets?

        }

        float GravAccel()
        {
            Vector3 geeVector = FlightGlobals.getGeeForceAtPosition(vessel.CoM);
            float gravAccel = geeVector.magnitude * Mathf.Cos(Mathf.Deg2Rad * Vector3.Angle(-geeVector, vessel.velocityD)); // -g.v/|v| ???
            return gravAccel;
        }

        float possibleAccel;

        public float GetPossibleAccel()
        {
            return possibleAccel;
        }
    }

    public class BDLandSpeedControl : MonoBehaviour
    {
        public float targetSpeed;
        public Vessel vessel;
        public bool preventNegativeZeroPoint = false;

        private float lastThrottle;
        public float zeroPoint { get; private set; }

        private const float gain = 0.5f;
        private const float zeroMult = 0.02f;

        public void Activate()
        {
            vessel.OnFlyByWire -= SpeedControl;
            vessel.OnFlyByWire += SpeedControl;
            zeroPoint = 0;
            lastThrottle = 0;
        }

        public void Deactivate()
        {
            vessel.OnFlyByWire -= SpeedControl;
        }

        void SpeedControl(FlightCtrlState s)
        {
            if (!vessel.LandedOrSplashed)
                s.wheelThrottle = 0;
            else if (targetSpeed == 0)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                s.wheelThrottle = 0;
            }
            else
            {
                float throttle = zeroPoint + (targetSpeed - (float)vessel.srfSpeed) * gain;
                lastThrottle = Mathf.Clamp(throttle, -1, 1);
                zeroPoint = (zeroPoint + lastThrottle * zeroMult) * (1 - zeroMult);
                if (preventNegativeZeroPoint && zeroPoint < 0) zeroPoint = 0;
                s.wheelThrottle = lastThrottle;
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, throttle < -5f);
            }
        }
    }

    public class BDVTOLSpeedControl : MonoBehaviour
    {
        public float targetAltitude;
        public Vessel vessel;
        public bool preventNegativeZeroPoint = false;

        private float altIntegral;
        public float zeroPoint { get; private set; }

        private const float Kp = 0.5f;
        private const float Kd = 0.55f;
        private const float Ki = 0.03f;


        public void Activate()
        {
            vessel.OnFlyByWire -= AltitudeControl;
            vessel.OnFlyByWire += AltitudeControl;
            altIntegral = 0;
        }

        public void Deactivate()
        {
            vessel.OnFlyByWire -= AltitudeControl;
        }

        void AltitudeControl(FlightCtrlState s)
        {

            if (targetAltitude == 0)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                s.mainThrottle = 0;
            }
            else
            {
                float altError = (targetAltitude - (float)vessel.radarAltitude);
                float altP = Kp * (targetAltitude - (float)vessel.radarAltitude);
                float altD = Kd * (float)vessel.verticalSpeed;
                altIntegral = Ki * Mathf.Clamp(altIntegral + altError * Time.deltaTime, -1f, 1f);

                float throttle = altP + altIntegral - altD;
                s.mainThrottle = Mathf.Clamp01(throttle);

                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, throttle < -5f);
            }
        }
    }
}
