using System;
using System.Collections;
using System.Text;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Damage;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.VesselSpawning;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Weapons
{
    class BDModuleNuke : PartModule
    {
        [KSPField(isPersistant = true, guiActive = true, guiName = "WARNING: Reactor Safeties:", guiActiveEditor = false), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Weapon Name
        public string status = "OFFLINE";

        [KSPField(isPersistant = true, guiActive = true, guiName = "Coolant Remaining", guiActiveEditor = false), UI_Label(scene = UI_Scene.All)]
        public double fuelleft;

        public static string defaultflashModelPath = "BDArmory/Models/explosion/nuke/nukeFlash";
        [KSPField]
        public string flashModelPath = defaultflashModelPath;

        public static string defaultShockModelPath = "BDArmory/Models/explosion/nuke/nukeShock";
        [KSPField]
        public string shockModelPath = defaultShockModelPath;

        public static string defaultBlastModelPath = "BDArmory/Models/explosion/nuke/nukeBlast";
        [KSPField]
        public string blastModelPath = defaultBlastModelPath;

        public static string defaultPlumeModelPath = "BDArmory/Models/explosion/nuke/nukePlume";
        [KSPField]
        public string plumeModelPath = defaultPlumeModelPath;

        public static string defaultDebrisModelPath = "BDArmory/Models/explosion/nuke/nukeScatter";
        [KSPField]
        public string debrisModelPath = defaultDebrisModelPath;

        public static string defaultBlastSoundPath = "BDArmory/Models/explosion/nuke/nukeBoom";
        [KSPField]
        public string blastSoundPath = defaultBlastSoundPath;

        [KSPField(isPersistant = true)]
        public float thermalRadius = 750;

        [KSPField(isPersistant = true)]
        public float yield = 0.05f;

        [KSPField(isPersistant = true)]
        public float fluence = 0.05f;

        [KSPField(isPersistant = true)]
        public bool isEMP = false;

        [KSPField(isPersistant = true)]
        public bool engineCore = false;

        [KSPField(isPersistant = true)]
        public float meltDownDuration = 2.5f;

        private int FuelID;
        private bool hasDetonated = false;
        private bool goingCritical = false;
        public string Sourcevessel;

        [KSPField(isPersistant = true)]
        public string reportingName = "Reactor Containment Failure";

        MissileLauncher missile;
        public MissileLauncher Launcher
        {
            get
            {
                if (missile) return missile;
                missile = part.FindModuleImplementing<MissileLauncher>();
                return missile;
            }
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (engineCore)
                {
                    FuelID = PartResourceLibrary.Instance.GetDefinition("LiquidFuel").id;
                    vessel.GetConnectedResourceTotals(FuelID, out double fuelCurrent, out double fuelMax);
                    fuelleft = fuelCurrent;
                    var engine = part.FindModuleImplementing<ModuleEngines>();
                    if (engine != null)
                    {
                        engine.allowShutdown = false;
                    }
                    part.force_activate();
                }
                else
                {
                    Fields["status"].guiActive = false;
                    Fields["fuelleft"].guiActive = false;
                    Fields["status"].guiActiveEditor = false;
                    Fields["fuelleft"].guiActiveEditor = false;
                }
                Sourcevessel = part.vessel.GetName();

                if (engineCore) part.OnJustAboutToBeDestroyed += Detonate;
                GameEvents.onVesselPartCountChanged.Add(CheckAttached);
                GameEvents.onVesselCreate.Add(CheckAttached);
            }
            base.OnStart(state);
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (BDACompetitionMode.Instance.competitionIsActive) //only begin checking engine state after comp start
                {
                    if (engineCore)
                    {
                        vessel.GetConnectedResourceTotals(FuelID, out double fuelCurrent, out double fuelMax);
                        fuelleft = fuelCurrent;
                        if (fuelleft <= 0)
                        {
                            if (!hasDetonated && !goingCritical)
                            {
                                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.RWPS3R2NukeModule]: nerva on " + Sourcevessel + " is out of fuel.");
                                StartCoroutine(DelayedDetonation(meltDownDuration)); //bingo fuel, detonate
                            }
                        }
                        var engine = part.FindModuleImplementing<ModuleEngines>();
                        if (engine != null)
                        {
                            if (!engine.isEnabled || !engine.EngineIgnited) //so this is getting tripped by multimode engines toggling from wet/dry
                            {
                                if (!hasDetonated)
                                {
                                    if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.RWPS3R2NukeModule]: nerva on " + Sourcevessel + " is Off, detonating");
                                    Detonate(); //nuke engine off after comp start, detonate.
                                }
                            }
                            if (engine.thrustPercentage < 100)
                            {
                                if (part.Modules.GetModule<HitpointTracker>().Hitpoints == part.Modules.GetModule<HitpointTracker>().GetMaxHitpoints())
                                {
                                    if (!hasDetonated)
                                    {
                                        if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.RWPS3R2NukeModule]: nerva on " + Sourcevessel + " is manually thrust limited, detonating");
                                        Detonate(); //nuke engine off after comp start, detonate.
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        void CheckAttached(Vessel v)
        {
            if (v != vessel || hasDetonated || goingCritical || !engineCore) return;
            VesselModuleRegistry.OnVesselModified(v);
            if (VesselModuleRegistry.GetModuleCount<MissileFire>(v) == 0)
            {
                if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.RWPS3R2NukeModule]: Nuclear engine on " + Sourcevessel + " has become detached.");
                goingCritical = true;
                StartCoroutine(DelayedDetonation(0.5f));
            }
        }

        IEnumerator DelayedDetonation(float delay)
        {
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.RWPS3R2NukeModule]: Nuclear engine on " + Sourcevessel + " going critical in " + delay.ToString("0.0") + "s.");
            goingCritical = true;
            yield return new WaitForSecondsFixed(delay);
            if (!hasDetonated && part != null) Detonate();
        }

        public void OnDestroy()
        {
            GameEvents.onVesselPartCountChanged.Remove(CheckAttached);
            GameEvents.onVesselCreate.Remove(CheckAttached);
        }

        public void Detonate()
        {
            if (hasDetonated || FlightGlobals.currentMainBody == null || VesselSpawnerStatus.vesselsSpawning) // Don't trigger on scene changes or during spawning.
            {
                return;
            }
            if (Launcher != null &&
                (Launcher.MissileState == MissileBase.MissileStates.Idle || Launcher.MissileState == MissileBase.MissileStates.Drop))
            {
                return;
            }
            if (BDArmorySettings.DEBUG_OTHER) Debug.Log("[BDArmory.BDModuleNuke]: Running Detonate() on nukeModule in vessel " + Sourcevessel);
            //affect any nearby parts/vessels that aren't the source vessel
            NukeFX.CreateExplosion(part.transform.position, Launcher != null ? ExplosionSourceType.Missile : ExplosionSourceType.BattleDamage, Sourcevessel, reportingName, 0, thermalRadius, yield, fluence, isEMP, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath, "", "");
            hasDetonated = true;
            if (part.vessel != null) // Already in the process of being destroyed.
                part.Destroy();
        }
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            if (engineCore)
            {
                output.Append(Environment.NewLine);
                output.AppendLine($"Reactor Core");
                output.AppendLine($"An unstable reactor core that will detonate if the engine is disabled");
                output.AppendLine($"Yield: {yield}");
                output.AppendLine($"Generates EMP: {isEMP}");
            }
            if (Launcher != null)
            {
                output.AppendLine($"Nuclear Warhead");
                output.AppendLine($"Yield: {yield}");
                output.AppendLine($"Generates EMP: {isEMP}");
            }
            return output.ToString();
        }
    }
}
