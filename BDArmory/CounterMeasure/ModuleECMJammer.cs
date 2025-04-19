using BDArmory.UI;
using BDArmory.VesselSpawning;
using BDArmory.Weapons.Missiles;
using System.Text;
using UnityEngine;

namespace BDArmory.CounterMeasure
{
    public class ModuleECMJammer : PartModule
    {
        [KSPField] public float jammerStrength = 700;

        [KSPField] public float lockBreakerStrength = 500;

        [KSPField] public float rcsReductionFactor = 0.75f;

        [KSPField] public float rcsOverride = -1;

        [KSPField] public double resourceDrain = 5;

        [KSPField] public string resourceName = "ElectricCharge";

        [KSPField] public bool alwaysOn = false;

        [KSPField] public bool signalSpam = true;

        [KSPField] public bool ghostTargets = false; //spoof targets?

        [KSPField] public int ghostTargetCount = 0; //how many

        [KSPField] public float ghostOffset = 0; //spoofed target distance from jammer, meters

        [KSPField] public bool lockBreaker = true;

        [KSPField] public bool rcsReduction = false;

        [KSPField] public float cooldownInterval = -1;

        [KSPField(isPersistant = true, guiActive = true, guiName = "#LOC_BDArmory_Enabled")]//Enabled
        public bool jammerEnabled = false;

        public bool manuallyEnabled = false;

        public bool isMissileECM = false;

        private int resourceID;

        private float cooldownTimer = 0;

        VesselECMJInfo vesselJammer;

        private BDStagingAreaGauge gauge;

        [KSPAction("Enable")]
        public void AGEnable(KSPActionParam param)
        {
            if (!jammerEnabled && !isMissileECM)
            {
                EnableJammer();
            }
        }

        [KSPAction("Disable")]
        public void AGDisable(KSPActionParam param)
        {
            if (jammerEnabled && !isMissileECM)
            {
                DisableJammer();
            }
        }

        [KSPAction("Toggle")]
        public void AGToggle(KSPActionParam param)
        {
            if (!isMissileECM)
                Toggle();
        }

        [KSPEvent(guiActiveEditor = false, guiActive = true, guiName = "#LOC_BDArmory_Toggle")]//Toggle
        public void Toggle()
        {
            if (jammerEnabled)
            {
                DisableJammer();
            }
            else
            {
                EnableJammer();
            }
        }
        void Start()
        {
            resourceID = PartResourceLibrary.Instance.GetDefinition(resourceName).id;
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            part.force_activate();

            if (part.FindModuleImplementing<MissileLauncher>() != null || SpawnUtils.IsModularMissilePart(part))
            {
                isMissileECM = true;
                Events["Toggle"].guiActive = false;
            }
            if (alwaysOn && !isMissileECM) EnableJammer();
            gauge = (BDStagingAreaGauge)part.AddModule("BDStagingAreaGauge");
            GameEvents.onVesselCreate.Add(OnVesselCreate);
        }

        void OnDestroy()
        {
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
        }

        void OnVesselCreate(Vessel v)
        {
            if (v == vessel)
                EnsureVesselJammer();
        }

        public void EnableJammer()
        {
            if (cooldownTimer > 0) return;
            EnsureVesselJammer();
            vesselJammer.AddJammer(this);
            jammerEnabled = true;
        }

        public void DisableJammer()
        {
            EnsureVesselJammer();

            vesselJammer.RemoveJammer(this);
            jammerEnabled = false;
            cooldownTimer = cooldownInterval;
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (BDArmorySetup.GameIsPaused) return;

            if (alwaysOn && !jammerEnabled)
            {
                EnableJammer();
            }

            if (jammerEnabled)
            {
                EnsureVesselJammer();

                DrainElectricity();
            }
            if (cooldownTimer > 0)
            {
                cooldownTimer -= TimeWarp.fixedDeltaTime;
                if (vessel.isActiveVessel)
                {
                    gauge.UpdateHeatMeter(cooldownTimer / cooldownInterval);
                }
            }
        }

        void EnsureVesselJammer()
        {
            /*
            if (vesselJammer == null)
            {
                return;
            }
            if (vesselJammer.vessel == null)
            {
                return;
            }
            if (vessel == null)
            {
                return;
            }
            */

            if (!vesselJammer || vesselJammer.vessel != vessel)
            {
                vesselJammer = vessel.gameObject.GetComponent<VesselECMJInfo>();
                if (!vesselJammer)
                {
                    vesselJammer = vessel.gameObject.AddComponent<VesselECMJInfo>();
                }
            }

            vesselJammer.DelayedCleanJammerList();
        }

        void DrainElectricity()
        {
            if (resourceDrain <= 0)
            {
                return;
            }

            double drainAmount = resourceDrain * TimeWarp.fixedDeltaTime;
            double chargeAvailable = part.RequestResource(resourceID, drainAmount, ResourceFlowMode.ALL_VESSEL);
            if (chargeAvailable < drainAmount * 0.95f)
            {
                DisableJammer();
            }
        }

        // RMB info in editor
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine($"EC/sec: {resourceDrain}");
            output.AppendLine($"Always on: {alwaysOn}");
            output.AppendLine($"RCS reduction: {rcsReduction}");
            if (rcsReduction)
            {
                output.AppendLine($" - factor: {rcsReductionFactor}");
            }
            output.AppendLine($"Lockbreaker: {lockBreaker}");
            if (lockBreaker)
            {
                output.AppendLine($" - strength: {lockBreakerStrength}");
            }
            output.AppendLine($"Signal strength: {jammerStrength}");
            output.AppendLine($"(increases detectability!)");
            if (ghostTargets)
            {
                output.AppendLine($"Generates False Targets");
                output.AppendLine($"(increases detectability!)");
                output.AppendLine($" - False targets: {ghostTargetCount}");
                output.AppendLine($" - Target offset: {ghostOffset}m");
            }

            return output.ToString();
        }
    }
}
