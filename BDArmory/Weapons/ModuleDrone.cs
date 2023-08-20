using BDArmory.Control;
using BDArmory.Extensions;
using BDArmory.FX;
using BDArmory.Guidances;
using BDArmory.Radar;
using BDArmory.Settings;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BDArmory.Weapons
{
    /// <summary>
    /// Need to add targetGet/firing solution/weaponSelection logic to MissileFire
    /// test drones will need to be AquisitionMode 'Active'
    /// TODOs:
    /// Additional guidance modes besides IR, flesh out IR tracking
    /// support for missile-armed drones via MMLs
    /// support for weapon selection if drone has both MML and a gun
    /// low-Alt gain-alt behavior
    /// terrain avoidance?
    /// </summary>
	public class ModuleDrone : MissileBase
	{
		//Drone specs
		[KSPField]
		float flightTime = 60; 

		double ammoAmount; //internal var to track onboard ammo qty.
        private int AmmoID;

        [KSPField]
		public string DroneCapability = "SingleUse"; // SingleUse | Wingman | Autonomous

        public enum DependancyModes { SingleUse, Wingman, Autonomous}

        public DependancyModes DependancyMode;

        [KSPField]
		public string TargetAquisition = "Slaved"; //Slaved - attacks parent's target; AtLaunch - locks a target at launch; Active - auto target aquisition

        public enum AquisitionModes { Slaved, AtLaunch, Active }

        public AquisitionModes AquisitionMode;

        [KSPField]
		string SensorClass = "ImageRecognition"; // IR is visual target; could add Heat and Radar detection later

        [KSPField]
		float maxAirspeed = 300;

        [KSPField]
        public float maxTorque = 90;

        [KSPField]
        public float thrust = 30;

        [KSPField]
        public float liftArea = 0.015f;

        [KSPField]
        public float steerMult = 0.5f;

        [KSPField]
        public float torqueRampUp = 30f;
        Vector3 aeroTorque = Vector3.zero;
        float controlAuthority;
        float finalMaxTorque;

        [KSPField]
        public float maxTurnRateDPS = 20;

        [KSPField]
        public float aeroSteerDamping = 0;

        private bool SDtriggered = false;

        [KSPField]
        public string audioClipPath = string.Empty;

        AudioClip thrustAudio;

        [KSPField]
        public string deployAnimationName = "";

        [KSPField]
        public float deployedDrag = 0.02f;

        [KSPField]
        public float deployTime = 0.2f;

        [KSPField]
        public string flightAnimationName = "";

        [KSPField]
        public bool OneShotAnim = true;

        [KSPField]
        public bool useSimpleDrag = false;

        [KSPField]
        public float simpleDrag = 0.02f;

        [KSPField]
        public float simpleStableTorque = 5;

        [KSPField]
        public Vector3 simpleCoD = new Vector3(0, 0, -1);

        float currentThrust;

        public bool deployed = false;
        //public float deployedTime;

        AnimationState[] deployStates;

        AnimationState[] animStates;

        bool hasPlayedFlyby;

        float debugTurnRate;

        Transform vesselReferenceTransform;

        [KSPField]
        public bool vacuumSteerable = false;

        float[] rcsFiredTimes;
        KSPParticleEmitter[] rcsTransforms;

        //Inherited fields
        //public MissileTurret missileTurret = null;
        //public BDRotaryRail rotaryRail = null;
        //public BDDeployableRail deployRail = null;

        [KSPField]
        public float maxAoA = 35;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_Direction"),//Direction: 
            UI_Toggle(disabledText = "#LOC_BDArmory_Direction_disabledText", enabledText = "#LOC_BDArmory_Direction_enabledText")]//Lateral--Forward
        public bool decoupleForward = false;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_DecoupleSpeed"),//Decouple Speed
                  UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.1f, scene = UI_Scene.Editor)]
        public float decoupleSpeed = 0;

        [KSPField]
        public float clearanceRadius = 0.14f;

        public override float ClearanceRadius => clearanceRadius;

        [KSPField]
        public float clearanceLength = 0.14f;

        public override float ClearanceLength => clearanceLength;

        [KSPField]
        public string exhaustPrefabPath;

        [KSPField]
        public bool hasRCS = false;

        [KSPField]
        public float rcsThrust = 1;
        float rcsRVelThreshold = 0.13f;
        KSPParticleEmitter upRCS;
        KSPParticleEmitter downRCS;
        KSPParticleEmitter leftRCS;
        KSPParticleEmitter rightRCS;
        KSPParticleEmitter forwardRCS;
        float rcsAudioMinInterval = 0.2f;

        private AudioSource audioSource;
        public AudioSource sfAudioSource;
        List<KSPParticleEmitter> pEmitters;
        List<BDAGaplessParticleEmitter> gaplessEmitters;

        //AI tunings
        [KSPField]
		float MinAlt = 150; //min flight alt

        [KSPField]
        public string rotationTransformName = string.Empty;
        Transform rotationTransform;

        [KSPField]
        float EvasionTime = 1;

		[KSPField]
		float EvasionDist = 10;

		[KSPField]
		public bool canExtend = true;

		[KSPField]
		public float extendMult = 1f;

		[KSPField]
		public float extendTargetVel = 0.8f;

		[KSPField]
		public float extendTargetAngle = 78f;

		[KSPField]
		public float extendTargetDist = 300f;

		//AI internal stuff
        public Vessel vesselTarget = null;
		ModuleWeapon weapon;

        public MissileFire weaponManager;

        private Vector3 upDirection;

		Vector3 lastTargetPosition;
		Vector3 flyingToPosition;
        Vector3 rollTarget;
        Vector3 angVelRollTarget;
        float turningTimer;
        float evasiveTimer;
        float threatRating;

        bool regainEnergy = false;

        float desiredMinAltitude;

        // Terrain avoidance and below minimum altitude globals.
        int terrainAlertTicker = 0; // A ticker to reduce the frequency of terrain alert checks.
		bool belowMinAltitude; // True when below minAltitude or avoiding terrain.
		bool gainAltInhibited = false; // Inhibit gain altitude to minimum altitude when chasing or evading someone as long as we're pointing upwards.
		bool avoidingTerrain = false; // True when avoiding terrain.
		float terrainAlertDetectionRadius = 30.0f; // Sphere radius that the vessel occupies. Should cover most vessels. FIXME This could be based on the vessel's maximum width/height.
		float terrainAlertThreatRange; // The distance to the terrain to consider (based on turn radius).
		float terrainAlertThreshold; // The current threshold for triggering terrain avoidance based on various factors.
		float terrainAlertDistance; // Distance to the terrain (in the direction of the terrain normal).
		Vector3 terrainAlertNormal; // Approximate surface normal at the terrain intercept.
		Vector3 terrainAlertDirection; // Terrain slope in the direction of the velocity at the terrain intercept.
		Vector3 terrainAlertCorrectionDirection; // The direction to go to avoid the terrain.
		float terrainAlertCoolDown = 0; // Cool down period before allowing other special modes to take effect (currently just "orbitting").
		Vector3 relativeVelocityRightDirection; // Right relative to current velocity and upDirection.
		Vector3 relativeVelocityDownDirection; // Down relative to current velocity and upDirection.
        //really, the smart thing to do would be to spin off the shared PilotAI/Drone code into an AIUtils class instead of duping...


        bool Aiming = false;
        float finalMaxSteer = 1;
        string currentStatus = "Free";

        public new string GetMissileType()
        {
            return droneType;
        }

        [KSPField]
        public string droneType = "drone";

        [KSPAction("Launch Drone")]
        public void AGFire(KSPActionParam param)
        {
            if (BDArmorySetup.Instance.ActiveWeaponManager != null && BDArmorySetup.Instance.ActiveWeaponManager.vessel == vessel) BDArmorySetup.Instance.ActiveWeaponManager.SendTargetDataToMissile(this);
            //if (missileTurret)
            //{
            //    missileTurret.FireMissile(this);
            //}
            //else if (rotaryRail)
            //{
            //    rotaryRail.FireMissile(this);
            //}
            //else if (deployRail)
            //{
            //    deployRail.FireMissile(this);
            //}
            //else
            {
                FireMissile();
            }
            if (BDArmorySetup.Instance.ActiveWeaponManager != null) BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }

        [KSPEvent(guiActive = true, guiName = "#LOC_BDArmory_LaunchDrone", active = true)]//Fire Missile
        public void GuiLaunch()
        {
            if (BDArmorySetup.Instance.ActiveWeaponManager != null && BDArmorySetup.Instance.ActiveWeaponManager.vessel == vessel)
            {
                BDArmorySetup.Instance.ActiveWeaponManager.SendTargetDataToMissile(this);
                weaponManager = BDArmorySetup.Instance.ActiveWeaponManager;
            }
            //if (missileTurret)
            //{
            //    missileTurret.FireMissile(this);
            //}
            //else if (rotaryRail)
            //{
            //    rotaryRail.FireMissile(this);
            //}
            //else if (deployRail)
            //{
            //    deployRail.FireMissile(this);
            //}
            //else
            {
                FireMissile();
            }
            if (BDArmorySetup.Instance.ActiveWeaponManager != null) BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, active = true, guiName = "#LOC_BDArmory_Jettison")]//Jettison
        public override void Jettison()
        {
            //if (missileTurret) return;

            part.decouple(0);
            if (BDArmorySetup.Instance.ActiveWeaponManager != null) BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
        }

        [KSPAction("Jettison")]
        public void AGJettsion(KSPActionParam param)
        {
            Jettison();
        }

        void ParseWeaponClass()
        {
            weaponClass = WeaponClasses.Drone;
        }

        public override void OnStart(StartState state)
        {
            //base.OnStart(state);
            ParseWeaponClass();
            if (shortName == string.Empty)
            {
                shortName = part.partInfo.title;
            }
            HasFired = false;
            gaplessEmitters = new List<BDAGaplessParticleEmitter>();
            pEmitters = new List<KSPParticleEmitter>();

            Fields["maxOffBoresight"].guiActive = false;
            Fields["maxOffBoresight"].guiActiveEditor = false;
            Fields["maxStaticLaunchRange"].guiActive = false;
            Fields["maxStaticLaunchRange"].guiActiveEditor = false;
            Fields["minStaticLaunchRange"].guiActive = false;
            Fields["minStaticLaunchRange"].guiActiveEditor = false;

            Fields["detonationTime"].guiActive = false;
            Fields["detonationTime"].guiActiveEditor = false;
            Fields["DetonationDistance"].guiActive = false;
            Fields["DetonationDistance"].guiActiveEditor = false;            
            Fields["DetonateAtMinimumDistance"].guiActive = false;
            Fields["DetonateAtMinimumDistance"].guiActiveEditor = false;

            ParseModes();
            // extension for feature_engagementenvelope
            InitializeEngagementRange(minStaticLaunchRange, maxStaticLaunchRange);

            using (var pEemitter = part.FindModelComponents<KSPParticleEmitter>().GetEnumerator())
                while (pEemitter.MoveNext())
                {
                    if (pEemitter.Current == null) continue;
                    if (pEemitter.Current.gameObject.name == "muzzleTransform") continue;
                    EffectBehaviour.AddParticleEmitter(pEemitter.Current);
                    pEemitter.Current.emit = false;
                }

            Debug.Log("[DRONEDEBUG] Checking DeployAnim");
            if (deployAnimationName != "")
            {
                deployAnimationName.Trim(' ');
                Debug.Log("[DRONEDEBUG] DeployAnim = " + deployAnimationName);
                deployStates = GUIUtils.SetUpAnimation(deployAnimationName, part);
            }
            else
            {
                deployedDrag = simpleDrag;
            }

            if (flightAnimationName != "")
            {
                flightAnimationName.Trim(' ');
                animStates = GUIUtils.SetUpAnimation(flightAnimationName, part);
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                SetupAudio();
                Debug.Log("[DRONEDEBUG] Audio found successfully");

                MissileReferenceTransform = part.FindModelTransform("ReferenceTransform");
                if (!MissileReferenceTransform)
                {
                    MissileReferenceTransform = part.partTransform;
                }
                if (rotationTransformName != string.Empty)
                {
                    rotationTransform = part.FindModelTransform(rotationTransformName);
                }
                if (!string.IsNullOrEmpty(exhaustPrefabPath))
                {
                    using (var t = part.FindModelTransforms("exhaustTransform").AsEnumerable().GetEnumerator())
                        while (t.MoveNext())
                        {
                            if (t.Current == null) continue;
                            AttachExhaustPrefab(exhaustPrefabPath, this, t.Current);
                        }
                }

                using (var pEmitter = part.partTransform.Find("model").GetComponentsInChildren<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                    while (pEmitter.MoveNext())
                    {
                        if (pEmitter.Current == null) continue;
                        if (pEmitter.Current.gameObject.name == "muzzleTransform") continue;
                        if (pEmitter.Current.GetComponent<BDAGaplessParticleEmitter>())
                        {
                            continue;
                        }

                        if (pEmitter.Current.useWorldSpace)
                        {
                            BDAGaplessParticleEmitter gaplessEmitter = pEmitter.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                            gaplessEmitter.part = part;
                            gaplessEmitters.Add(gaplessEmitter);
                        }
                        else
                        {
                            pEmitters.Add(pEmitter.Current);
                            EffectBehaviour.AddParticleEmitter(pEmitter.Current);
                        }
                    }
                Debug.Log("[DRONEDEBUG] Pemitters found successfully");
                part.force_activate();
                if (hasRCS)
                {
                    using (var pe = pEmitters.GetEnumerator())
                        while (pe.MoveNext())
                        {
                            if (pe.Current == null) continue;
                            if (pe.Current.gameObject.name == "rcsUp") upRCS = pe.Current;
                            else if (pe.Current.gameObject.name == "rcsDown") downRCS = pe.Current;
                            else if (pe.Current.gameObject.name == "rcsLeft") leftRCS = pe.Current;
                            else if (pe.Current.gameObject.name == "rcsRight") rightRCS = pe.Current;
                            else if (pe.Current.gameObject.name == "rcsForward") forwardRCS = pe.Current;
                            //if (!pe.Current.gameObject.name.Contains("rcs") && !pe.Current.useWorldSpace)
                            //{
                            //    pe.Current.sizeGrow = 99999;
                            //}
                        }
                    SetupRCS();
                    KillRCS();
                    vacuumSteerable = true;
                }

                GameEvents.onPartDie.Add(OnPartDie);
                Debug.Log("[DRONEDEBUG] partDie Added successfully");
            }

            Fields["CruiseAltitude"].guiActive = false;
            Fields["CruiseAltitude"].guiActiveEditor = false;
            Fields["CruiseSpeed"].guiActive = false;
            Fields["CruiseSpeed"].guiActiveEditor = false;
            Events["CruiseAltitudeRange"].guiActive = false;
            Events["CruiseAltitudeRange"].guiActiveEditor = false;
            Fields["CruisePredictionTime"].guiActiveEditor = false;
            Fields["BallisticOverShootFactor"].guiActive = false;
            Fields["BallisticOverShootFactor"].guiActiveEditor = false;
            Fields["BallisticAngle"].guiActive = false;
            Fields["BallisticAngle"].guiActiveEditor = false;

            Fields["dropTime"].guiActive = false;
            Fields["dropTime"].guiActiveEditor = false;          
        }

        protected virtual void OnPartDie(Part p)
        {
            if (part == p)
            {
                Destroy(this); // Force this module to be removed from the gameObject as something is holding onto part references and causing a memory leak.
            }
        }

        void SetupAudio() //find out why this is throwing an error
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.minDistance = 1;
                audioSource.maxDistance = 1000;
                audioSource.loop = true;
                audioSource.pitch = 1f;
                audioSource.priority = 255;
                audioSource.spatialBlend = 1;
            }

            if (audioClipPath != string.Empty)
            {
                audioSource.clip = GameDatabase.Instance.GetAudioClip(audioClipPath);
            }

            if (sfAudioSource == null)
            {
                sfAudioSource = gameObject.AddComponent<AudioSource>();
                sfAudioSource.minDistance = 1;
                sfAudioSource.maxDistance = 2000;
                sfAudioSource.dopplerLevel = 0;
                sfAudioSource.priority = 230;
                sfAudioSource.spatialBlend = 1;
            }

            if (audioClipPath != string.Empty)
            {
                thrustAudio = GameDatabase.Instance.GetAudioClip(audioClipPath);
            }

            UpdateVolume();
            BDArmorySetup.OnVolumeChange -= UpdateVolume; // Remove it if it's already there. (Doesn't matter if it isn't.)
            BDArmorySetup.OnVolumeChange += UpdateVolume;
        }

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
            if (sfAudioSource)
            {
                sfAudioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
        }

        void OnDestroy()
        {
            DetachExhaustPrefabs();
            KillRCS();
            if (upRCS) EffectBehaviour.RemoveParticleEmitter(upRCS);
            if (downRCS) EffectBehaviour.RemoveParticleEmitter(downRCS);
            if (leftRCS) EffectBehaviour.RemoveParticleEmitter(leftRCS); 
            if (rightRCS) EffectBehaviour.RemoveParticleEmitter(rightRCS);
            if (pEmitters != null)
                foreach (var pe in pEmitters)
                    if (pe) EffectBehaviour.RemoveParticleEmitter(pe);
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            GameEvents.onPartDie.Remove(PartDie);
            if (vesselReferenceTransform != null && vesselReferenceTransform.gameObject != null)
            {
                Destroy(vesselReferenceTransform.gameObject);
            }
        }

        #region Launchparams
        public override void FireMissile() //Launch Drone
        {
            if (HasFired || launched) return;

            try // FIXME Remove this once the fix is sufficiently tested.
            {
                HasFired = true;

                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.DroneLauncher]: Drone launched! " + vessel.vesselName);

                GameEvents.onPartDie.Add(PartDie);
                BDATargetManager.FiredDrones.Add(this);

                if (GetComponentInChildren<KSPParticleEmitter>())
                {
                    BDArmorySetup.numberOfParticleEmitters++;
                }
                SourceVessel = vessel;
                weaponManager = VesselModuleRegistry.GetModule<MissileFire>(SourceVessel);
                if (weaponManager != null) Team = weaponManager.Team;

                if (sfAudioSource == null) SetupAudio();
                sfAudioSource.PlayOneShot(GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/deployClick"));
                weapon = part.FindModuleImplementing<ModuleWeapon>();
                if (weapon != null)
                {
                    AmmoID = PartResourceLibrary.Instance.GetDefinition(weapon.ammoName).id;
                }
                //TARGETING
                TargetPosition = transform.position + (transform.forward * 5000); //set initial target position so if no target update, missileBase will count a miss if it nears this point or is flying post-thrust
                startDirection = transform.forward;
                //if (AquisitionMode == AquisitionModes.AtLaunch)
                //{
                 //   vesselTarget = weaponManager.currentTarget.Vessel; //this should be already covered via SendtargetingDataToMissile()
                //}
                part.crashTolerance = 9999; //to combat stresses of launch, missle generate a lot of G Force
                part.decouple(0);
                part.force_activate();
                part.Unpack();
                vessel.situation = Vessel.Situations.FLYING;
                part.rb.isKinematic = false;
                part.bodyLiftMultiplier = 0;
                part.dragModel = Part.DragModel.NONE;

                TargetInfo info = vessel.gameObject.AddComponent<TargetInfo>();
                info.Team = Team;
                info.isMissile = false;
                info.MissileBaseModule = this;
                /* //this should be in the update node, not launch
                if (AquisitionMode == AquisitionModes.Slaved)
                {
                    if (weaponManager != null)
                    {
                        if (weaponManager.currentTarget != null)
                        {
                            vesselTarget = weaponManager.currentTarget.Vessel;
                        }
                    }
                    else //parentVessel WM somehow destroyed moment of launch, SD
                    {
                        if (!SDtriggered) StartCoroutine(SelfDestructRoutine(0));
                        Debug.Log("[DRONEDEBUG] Slaved drone lost connection to parent WM ");
                        SDtriggered = true;
                    }
                }
                */
                StartCoroutine(DecoupleRoutine());

                vessel.vesselName = GetShortName();
                vessel.vesselType = VesselType.Probe;

                TimeFired = Time.time;

                //setting ref transform for navball
                GameObject refObject = new GameObject();
                refObject.transform.rotation = Quaternion.LookRotation(-transform.up, transform.forward);
                refObject.transform.parent = transform;
                part.SetReferenceTransform(refObject.transform);
                vessel.SetReferenceTransform(part);
                vesselReferenceTransform = refObject.transform;
                MissileState = MissileStates.Drop;
                if (weapon != null) weapon.EnableWeapon();
                StartCoroutine(DroneRoutine());
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.DroneLauncher]: FireMissile() DEBUG " + e.Message);
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null part?: " + (part == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG part: " + e2.Message); }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null part.rb?: " + (part.rb == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG part.rb: " + e2.Message); }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null BDATargetManager.FiredMissiles?: " + (BDATargetManager.FiredMissiles == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG BDATargetManager.FiredMissiles: " + e2.Message); }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null vessel?: " + (vessel == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG vessel: " + e2.Message); }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null sfAudioSource?: " + (sfAudioSource == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG sfAudioSource: " + e2.Message); }
                throw; // Re-throw the exception so behaviour is unchanged so we see it.
            }
        }

        IEnumerator DecoupleRoutine()
        {
            yield return new WaitForFixedUpdate();

            if (decoupleForward)
            {
                part.rb.velocity += decoupleSpeed * part.transform.forward;
            }
            else
            {
                part.rb.velocity += decoupleSpeed * -part.transform.up;
            }
        }

        public override void OnFixedUpdate()
        {
            if (BDArmorySetup.GameIsPaused) return;
            if (!HighLogic.LoadedSceneIsFlight) return;

            FloatingOriginCorrection();

            debugString.Length = 0;
            if (HasFired && !HasExploded && part != null)
            {
                part.rb.isKinematic = false;
                AntiSpin();

                //simpleDrag
                if (useSimpleDrag)
                {
                    SimpleDrag();
                }
                
                //flybyaudio
                float mCamDistanceSqr = (FlightCamera.fetch.mainCamera.transform.position - transform.position).sqrMagnitude;
                float mCamRelVSqr = (float)(FlightGlobals.ActiveVessel.Velocity() - vessel.Velocity()).sqrMagnitude;
                if (!hasPlayedFlyby
                   && FlightGlobals.ActiveVessel != vessel
                   && FlightGlobals.ActiveVessel != SourceVessel
                   && mCamDistanceSqr < 400 * 400 && mCamRelVSqr > 300 * 300
                   && mCamRelVSqr < 800 * 800
                   && Vector3.Angle(vessel.Velocity(), FlightGlobals.ActiveVessel.transform.position - transform.position) < 60)
                {
                    if (sfAudioSource == null) SetupAudio();
                    sfAudioSource.PlayOneShot(GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/missileFlyby"));
                    hasPlayedFlyby = true;
                }

                if (vessel.isActiveVessel)
                {
                    audioSource.dopplerLevel = 0;
                }
                else
                {
                    audioSource.dopplerLevel = 1f;
                }
                
                if (TimeIndex > 0.5f)
                {
                    part.crashTolerance = 1;
                }
                UpdateThrustForces();

                if (!SDtriggered)
                {
                    if (weapon != null)
                    {
                        vessel.GetConnectedResourceTotals(AmmoID, out double ammoCurrent, out double ammoMax);
                        ammoAmount = ammoCurrent;
                        if (ammoAmount <= 0)
                        {
                            StartCoroutine(SelfDestructRoutine(10));
                            Debug.Log("[DRONEDEBUG] Drone Fuel/Ammo depleted");
                        }
                    }
                    if ((DependancyMode == DependancyModes.Wingman && SourceVessel == null))
                    {
                        StartCoroutine(SelfDestructRoutine(0));
                        Debug.Log("[DRONEDEBUG] Wingman Drone lost connection to mothership");
                    }
                    if (((DependancyMode == DependancyModes.SingleUse && vesselTarget == null)))
                    {
                        StartCoroutine(SelfDestructRoutine(10));
                        Debug.Log("[DRONEDEBUG] Single-Target drone lost target");
                    }
                    if (weaponManager.guardMode)
                    {
                        GuardMode();
                    }
                    else
                    {
                        targetScanTimer = -100;
                    }
                    UpdateGuidance();
                }
                if (weaponManager != null)
                {
                    if (weaponManager.Team != Team)
                    {
                        Team = weaponManager.Team;
                    }
                }
                else
                {
                    if (DependancyMode == DependancyModes.Wingman)
                    {
                        if (!SDtriggered) StartCoroutine(SelfDestructRoutine(10));
                        Debug.Log("[DRONEDEBUG] Wingman Drone lost connection to mothership");
                        SDtriggered = true;
                    }
                }
            }
        }

        void UpdateGuidance()
        {
            string debugTarget = "none";
            if (guidanceActive)
            {
                /*
                if (TargetingMode == TargetingModes.Heat)
                {
                    UpdateHeatTarget();
                    if (heatTarget.vessel)
                        debugTarget = heatTarget.vessel.GetDisplayName() + " " + heatTarget.signalStrength.ToString();
                    else if (heatTarget.signalStrength > 0)
                        debugTarget = "Flare " + heatTarget.signalStrength.ToString();
                }
                else if (TargetingMode == TargetingModes.Radar)
                {
                    UpdateRadarTarget();
                    if (radarTarget.vessel)
                        debugTarget = radarTarget.vessel.GetDisplayName() + " " + radarTarget.signalStrength.ToString();
                    else if (radarTarget.signalStrength > 0)
                        debugTarget = "Chaff " + radarTarget.signalStrength.ToString();
                }    
                */
                //Guidancetype = ImageRecongnition
                if (vesselTarget != null)
                {
                    debugString.Append($"Target Vessel: {vesselTarget.GetName()}");
                }
                else
                {
                    debugString.Append("Target Vessel: null");
                }
                debugString.Append(Environment.NewLine);
                UpdateIRTarget();
                debugTarget = flyingToPosition.ToString();
            }

            if (MissileState != MissileStates.Idle && MissileState != MissileStates.Drop) //guidance
            {
                //guidance and attitude stabilisation scales to atmospheric density. //use part.atmDensity
                float atmosMultiplier = Mathf.Clamp01(2.5f * (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position), FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody));

                if (vessel.srfSpeed < maxAirspeed)
                {
                    float optimumSpeedFactor = (float)vessel.srfSpeed / (2 * maxAirspeed);
                    controlAuthority = Mathf.Clamp01(atmosMultiplier * (-Mathf.Abs(2 * optimumSpeedFactor - 1) + 1));
                }
                else
                {
                    controlAuthority = Mathf.Clamp01(atmosMultiplier);
                }

                if (vacuumSteerable)
                {
                    controlAuthority = 1;
                }

                debugString.Append($"controlAuthority: {controlAuthority}");
                debugString.Append(Environment.NewLine);

                if (guidanceActive)// && timeIndex - dropTime > 0.5f)
                {
                    //modify TurnRate based on speed
                    float turnRateDPS = Mathf.Clamp(((float)vessel.srfSpeed / maxAirspeed) * maxTurnRateDPS, 0, maxTurnRateDPS);
                    if (!hasRCS)
                    {
                        turnRateDPS *= controlAuthority;
                    }

                    //decrease turn rate after thrust cuts out
                    if (TimeIndex > dropTime + flightTime)
                    {
                        var clampedTurnRate = Mathf.Clamp(maxTurnRateDPS - ((TimeIndex - dropTime - flightTime) * 0.45f),
                            1, maxTurnRateDPS);
                        turnRateDPS = clampedTurnRate;

                        if (!vacuumSteerable)
                        {
                            turnRateDPS *= atmosMultiplier;
                        }

                        if (hasRCS)
                        {
                            turnRateDPS = 0;
                        }
                    }

                    if (hasRCS)
                    {
                        if (turnRateDPS > 0)
                        {
                            DoRCS();
                        }
                        else
                        {
                            KillRCS();
                        }
                    }
                    debugTurnRate = turnRateDPS;
                    finalMaxTorque = maxTorque; //ramp up torque
                    if (GuidanceMode != GuidanceModes.RCS)
                    {
                        if (TimeIndex > dropTime + 0.25f)
                        {
                            DroneGuidance();
                        }
                    }
                    else
                    {
                        part.transform.rotation = Quaternion.RotateTowards(part.transform.rotation, Quaternion.LookRotation(flyingToPosition - part.transform.position, part.transform.up), turnRateDPS * Time.fixedDeltaTime);
                    }
                }
                else //no targets/guidance inactive
                {
                    //start self-destruct timer
                    if (!SDtriggered)
                    {
                        SDtriggered = true;
                        StartCoroutine(SelfDestructRoutine(30));
                        Debug.Log("[DRONEDEBUG] Drone guidance lost");
                    }
                }

                if (aeroSteerDamping > 0)
                {
                    part.rb.AddRelativeTorque(-aeroSteerDamping * part.transform.InverseTransformVector(part.rb.angularVelocity));
                }

                if (hasRCS && !guidanceActive)
                {
                    KillRCS();
                }
            }
            debugString.Append("Drone target=" + debugTarget);
            debugString.Append(Environment.NewLine);
            debugString.Append(weapon.WeaponStatusdebug());
        }

        void UpdateIRTarget()
        {
            if (!TargetAcquired) //boresight considerations for cameraFoV?
            {
                if (AquisitionMode == AquisitionModes.AtLaunch)//
                {
                    //if (DependancyMode == DependancyModes.SingleUse) //killed its target, begin selfdestruct
                    {
                        guidanceActive = false;
                        vesselTarget = null;
                        return;
                    }
                    //have Atlaunch multitarget drones lock current target of sourceVessel
                }
                else if (AquisitionMode == AquisitionModes.Slaved)//
                {
                    if (DependancyMode == DependancyModes.SingleUse) //killed its target, begin selfdestruct
                    {
                        guidanceActive = false;
                        vesselTarget = null;
                        return;
                    }
                    else
                    {
                        vesselTarget = SourceVessel; //multi-target drone slaved to mothership; return to motherhip
                    }
                }
                else //autonomous target aquisition
                {
                    if (DependancyMode == DependancyModes.SingleUse) //killed its target, begin selfdestruct
                    {
                        guidanceActive = false;
                        vesselTarget = null;
                        return;
                    }
                    vesselTarget = SourceVessel; //return to mothership until new targets present themself
                }
            }
        }

        void DroneGuidance()
        {
            Vector3 aamTarget;
            if (TargetAcquired)
            {
                if (weapon != null) //Lead target for gun aiming
                {
                    TargetPosition += weapon.GetLeadOffset();
                }
                DrawDebugLine(transform.position + (part.rb.velocity * Time.fixedDeltaTime), TargetPosition);

                float timeToImpact;
                aamTarget = MissileGuidance.GetAirToAirTarget(TargetPosition, TargetVelocity, TargetAcceleration, vessel, out timeToImpact, maxAirspeed);

                if (Vector3.Angle(aamTarget - transform.position, transform.forward) > maxOffBoresight * 0.75f)
                {
                    aamTarget = TargetPosition;
                }
            }
            else
            {
                aamTarget = transform.position + (20 * vessel.Velocity().normalized);
            }
            Vector3 upDirection = VectorUtils.GetUpDirection(transform.position);

            //axial rotation
            if (rotationTransform)
            {
                Quaternion originalRotation = transform.rotation;
                Quaternion originalRTrotation = rotationTransform.rotation;
                transform.rotation = Quaternion.LookRotation(transform.forward, upDirection);
                rotationTransform.rotation = originalRTrotation;
                Vector3 lookUpDirection = Vector3.ProjectOnPlane(aamTarget - transform.position, transform.forward) * 100;
                lookUpDirection = transform.InverseTransformPoint(lookUpDirection + transform.position);

                lookUpDirection = new Vector3(lookUpDirection.x, 0, 0);
                lookUpDirection += 10 * Vector3.up;

                rotationTransform.localRotation = Quaternion.Lerp(rotationTransform.localRotation, Quaternion.LookRotation(Vector3.forward, lookUpDirection), 0.04f);
                Quaternion finalRotation = rotationTransform.rotation;
                transform.rotation = originalRotation;
                rotationTransform.rotation = finalRotation;

                vesselReferenceTransform.rotation = Quaternion.LookRotation(-rotationTransform.up, rotationTransform.forward);
            }
            if (TimeIndex > dropTime + 0.25f)
            {
                DoAero(aamTarget);
            }
        }
        void DoAero(Vector3 targetPosition)
        {
            aeroTorque = MissileGuidance.DoAeroForces(this, targetPosition, liftArea, controlAuthority * steerMult, aeroTorque, finalMaxTorque, maxAoA);
        }
        void UpdateThrustForces()
        {
            if (MissileState == MissileStates.PostThrust) return;
            currentThrust = Mathf.Lerp(currentThrust, Throttle * thrust, 0.1f);
            //currentThrust *= (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position),
            //                  FlightGlobals.getExternalTemperature(transform.position));
            part.rb.AddRelativeForce(currentThrust * Vector3.forward);
        }

        IEnumerator DroneRoutine()
        {
            MissileState = MissileStates.Drop;
            StartCoroutine(DeployAnimRoutine());
            yield return new WaitForSeconds(dropTime);
            StartCoroutine(FlightAnimRoutine());
            yield return StartCoroutine(FlightRoutine());
        }

        IEnumerator DeployAnimRoutine()
        {
            yield return new WaitForSeconds(deployTime);
            if (deployStates == null)
            {
                //if (BDArmorySettings.DRAW_DEBUG_LABELS) 
                    Debug.LogWarning("[BDArmory.Drone]: deployStates was null, aborting AnimRoutine.");
                yield break;
            }

            if (!string.IsNullOrEmpty(deployAnimationName))
            {
                deployed = true;
                using (var anim = deployStates.AsEnumerable().GetEnumerator())
                    while (anim.MoveNext())
                    {
                        if (anim.Current == null) continue;
                        anim.Current.speed = 1;
                    }
            }
        }
        IEnumerator FlightAnimRoutine()
        {
            if (animStates == null)
            {
                if (BDArmorySettings.DEBUG_MISSILES) Debug.LogWarning("[BDArmory.Drone]: animStates was null, aborting AnimRoutine.");
                yield break;
            }

            if (!string.IsNullOrEmpty(flightAnimationName))
            {
                using (var anim = animStates.AsEnumerable().GetEnumerator())
                    while (anim.MoveNext())
                    {
                        if (anim.Current == null) continue;
                        if (!OneShotAnim)
                        {
                            anim.Current.wrapMode = WrapMode.Loop;
                        }
                        anim.Current.speed = 1;
                    }
            }
        }

        IEnumerator FlightRoutine()
        {
            StartCruise();
            var wait = new WaitForFixedUpdate();
            float flightStartTime = Time.time;
            while (Time.time - flightStartTime < flightTime)
            {
                if (!BDArmorySetup.GameIsPaused)
                {
                    if (!audioSource.isPlaying || audioSource.clip != thrustAudio)
                    {
                        if (thrustAudio)
                        {
                            audioSource.clip = thrustAudio;
                        }
                        audioSource.Play();
                    }
                }
                else if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
                audioSource.volume = Throttle;

                //particleFx
                using (var emitter = pEmitters.GetEnumerator())
                    while (emitter.MoveNext())
                    {
                        if (emitter.Current == null) continue;
                        if (!hasRCS)
                        {
                            if (emitter.Current.gameObject.name == "muzzleTransform") continue;
                            emitter.Current.sizeGrow = Mathf.Lerp(emitter.Current.sizeGrow, 0, 20 * Time.deltaTime);
                        }

                        emitter.Current.maxSize = Mathf.Clamp01(Throttle / Mathf.Clamp((float)vessel.atmDensity, 0.2f, 1f));
                        emitter.Current.emit = true;
                    }

                using (var gpe = gaplessEmitters.GetEnumerator())
                    while (gpe.MoveNext())
                    {
                        if (gpe.Current == null) continue;

                        gpe.Current.pEmitter.maxSize = Mathf.Clamp01(Throttle / Mathf.Clamp((float)vessel.atmDensity, 0.2f, 1f));
                        gpe.Current.emit = true;
                        gpe.Current.pEmitter.worldVelocity = 2 * ParticleTurbulence.flareTurbulence;
                    }
                yield return wait;
            }
            EndCruise();
        }

        void StartCruise()
        {
            MissileState = MissileStates.Cruise;
            if (audioSource == null) SetupAudio();
            if (thrustAudio)
            {
                audioSource.clip = thrustAudio;
            }

            using (var pEmitter = pEmitters.GetEnumerator())
                while (pEmitter.MoveNext())
                {
                    if (pEmitter.Current == null) continue;
                    EffectBehaviour.AddParticleEmitter(pEmitter.Current);
                    pEmitter.Current.emit = true;
                }

            using (var gEmitter = gaplessEmitters.GetEnumerator())
                while (gEmitter.MoveNext())
                {
                    if (gEmitter.Current == null) continue;
                    EffectBehaviour.AddParticleEmitter(gEmitter.Current.pEmitter);
                    gEmitter.Current.emit = true;
                }

            if (!hasRCS) return;
            forwardRCS.emit = false;
            audioSource.Stop();
        }

        void EndCruise()
        {
            MissileState = MissileStates.PostThrust;
            IEnumerator<Light> light = gameObject.GetComponentsInChildren<Light>().AsEnumerable().GetEnumerator();
            while (light.MoveNext())
            {
                if (light.Current == null) continue;
                light.Current.intensity = 0;
            }
            light.Dispose();

            StartCoroutine(FadeOutAudio());
            StartCoroutine(FadeOutEmitters());
            StartCoroutine(SelfDestructRoutine(20));

            Debug.Log("[DRONEDEBUG] Drone lifetime exceeded");
            StartCoroutine(SelfDestructRoutine(10));

        }

        IEnumerator FadeOutAudio()
        {
            if (thrustAudio && audioSource.isPlaying)
            {
                while (audioSource.volume > 0 || audioSource.pitch > 0)
                {
                    audioSource.volume = Mathf.Lerp(audioSource.volume, 0, 5 * Time.deltaTime);
                    audioSource.pitch = Mathf.Lerp(audioSource.pitch, 0, 5 * Time.deltaTime);
                    yield return null;
                }
            }
        }

        IEnumerator FadeOutEmitters()
        {
            float fadeoutStartTime = Time.time;
            while (Time.time - fadeoutStartTime < 5)
            {
                using (var pe = pEmitters.GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        pe.Current.maxEmission = Mathf.FloorToInt(pe.Current.maxEmission * 0.8f);
                        pe.Current.minEmission = Mathf.FloorToInt(pe.Current.minEmission * 0.8f);
                    }

                using (var gpe = gaplessEmitters.GetEnumerator())
                    while (gpe.MoveNext())
                    {
                        if (gpe.Current == null) continue;
                        gpe.Current.pEmitter.maxSize = Mathf.MoveTowards(gpe.Current.pEmitter.maxSize, 0, 0.005f);
                        gpe.Current.pEmitter.minSize = Mathf.MoveTowards(gpe.Current.pEmitter.minSize, 0, 0.008f);
                        gpe.Current.pEmitter.worldVelocity = ParticleTurbulence.Turbulence;
                    }
                yield return new WaitForFixedUpdate();
            }

            using (var pe2 = pEmitters.GetEnumerator())
                while (pe2.MoveNext())
                {
                    if (pe2.Current == null) continue;
                    pe2.Current.emit = false;
                }

            using (var gpe2 = gaplessEmitters.GetEnumerator())
                while (gpe2.MoveNext())
                {
                    if (gpe2.Current == null) continue;
                    gpe2.Current.emit = false;
                }
        }

        IEnumerator SelfDestructRoutine(float time)
        {
            SDtriggered = true;
            HasMissed = true;
            yield return new WaitForSeconds(time);
            Detonate();
        }

        public override void Detonate()
        {
            if (HasExploded || !HasFired) return;

            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.ModuleDrone]: Detonate Triggered");

            BDArmorySetup.numberOfParticleEmitters--;
            HasExploded = true;

            if (vesselTarget != null)
            {
                using (var wpm = VesselModuleRegistry.GetModules<MissileFire>(vesselTarget).GetEnumerator())
                    while (wpm.MoveNext())
                    {
                        if (wpm.Current == null) continue;
                        wpm.Current.missileIsIncoming = false;
                    }
            }

            List<BDAGaplessParticleEmitter>.Enumerator e = gaplessEmitters.GetEnumerator();
            while (e.MoveNext())
            {
                if (e.Current == null) continue;
                e.Current.gameObject.AddComponent<BDAParticleSelfDestruct>();
                e.Current.transform.parent = null;
                if (e.Current.GetComponent<Light>())
                {
                    e.Current.GetComponent<Light>().enabled = false;
                }
            }
            e.Dispose();

            if (part != null)
            {
                part.Destroy();
                part.explode();
            }
        }

        public override Vector3 GetForwardTransform()
        {
            if (weapon != null)
            {
                return weapon.fireTransforms[0].forward;
            }
            return MissileReferenceTransform.forward;
        }

        protected override void PartDie(Part p)
        {
            if (p == part)
            {
                Detonate();
                BDATargetManager.FiredDrones.Remove(this);
                GameEvents.onPartDie.Remove(PartDie);
            }
        }

        void SetupRCS()
        {
            rcsFiredTimes = new float[] { 0, 0, 0, 0 };
            rcsTransforms = new KSPParticleEmitter[] { upRCS, leftRCS, rightRCS, downRCS };
        }

        void DoRCS()
        {
            try
            {
                Vector3 relV = TargetVelocity - vessel.obt_velocity;

                for (int i = 0; i < 4; i++)
                {
                    //float giveThrust = Mathf.Clamp(-localRelV.z, 0, rcsThrust);
                    float giveThrust = Mathf.Clamp(Vector3.Project(relV, rcsTransforms[i].transform.forward).magnitude * -Mathf.Sign(Vector3.Dot(rcsTransforms[i].transform.forward, relV)), 0, rcsThrust);
                    part.rb.AddForce(-giveThrust * rcsTransforms[i].transform.forward);

                    if (giveThrust > rcsRVelThreshold)
                    {
                        rcsAudioMinInterval = UnityEngine.Random.Range(0.15f, 0.25f);
                        if (Time.time - rcsFiredTimes[i] > rcsAudioMinInterval)
                        {
                            if (sfAudioSource == null) SetupAudio();
                            sfAudioSource.PlayOneShot(GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/popThrust"));
                            rcsTransforms[i].emit = true;
                            rcsFiredTimes[i] = Time.time;
                        }
                    }
                    else
                    {
                        rcsTransforms[i].emit = false;
                    }

                    //turn off emit
                    if (Time.time - rcsFiredTimes[i] > rcsAudioMinInterval * 0.75f)
                    {
                        rcsTransforms[i].emit = false;
                    }
                }
            }
            catch (Exception e)
            {

                Debug.LogError("[BDArmory.DroneLauncher]: DoRCSDEBUG " + e.Message);
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null part?: " + (part == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG part: " + e2.Message); }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null part.rb?: " + (part.rb == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG part.rb: " + e2.Message); }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null vessel?: " + (vessel == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG vessel: " + e2.Message); }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null sfAudioSource?: " + (sfAudioSource == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: sfAudioSource: " + e2.Message); }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null rcsTransforms?: " + (rcsTransforms == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG rcsTransforms: " + e2.Message); }
                if (rcsTransforms != null)
                {
                    for (int i = 0; i < 4; ++i)
                        try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null rcsTransforms[" + i + "]?: " + (rcsTransforms[i] == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG rcsTransforms[" + i + "]: " + e2.Message); }
                }
                try { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG null rcsFiredTimes?: " + (rcsFiredTimes == null)); } catch (Exception e2) { Debug.LogError("[BDArmory.DroneLauncher]: DEBUG rcsFiredTimes: " + e2.Message); }
                throw; // Re-throw the exception so behaviour is unchanged so we see it.
            }
        }

        public void KillRCS()
        {
            if (upRCS) upRCS.emit = false;
            if (downRCS) downRCS.emit = false;
            if (leftRCS) leftRCS.emit = false;
            if (rightRCS) rightRCS.emit = false;
        }

        void AntiSpin()
        {
            part.rb.angularDrag = 0;
            part.angularDrag = 0;
            Vector3 spin = Vector3.Project(part.rb.angularVelocity, part.rb.transform.forward);// * 8 * Time.fixedDeltaTime;
            part.rb.angularVelocity -= spin;
            part.rb.angularVelocity -= 0.6f * part.rb.angularVelocity;
        }

        void SimpleDrag()
        {
            part.dragModel = Part.DragModel.NONE;
            if (part.rb == null || part.rb.mass == 0) return;
            //float simSpeedSquared = (float)vessel.Velocity.sqrMagnitude;
            float simSpeedSquared = (part.rb.GetPointVelocity(part.transform.TransformPoint(simpleCoD)) + (Vector3)Krakensbane.GetFrameVelocity()).sqrMagnitude;
            Vector3 currPos = transform.position;
            float drag = deployed ? deployedDrag : simpleDrag;
            float dragMagnitude = (0.008f * part.rb.mass) * drag * 0.5f * simSpeedSquared * (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currPos), FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody);
            Vector3 dragForce = dragMagnitude * vessel.Velocity().normalized;
            part.rb.AddForceAtPosition(-dragForce, transform.TransformPoint(simpleCoD));

            Vector3 torqueAxis = -Vector3.Cross(vessel.Velocity(), part.transform.forward).normalized;
            float AoA = Vector3.Angle(part.transform.forward, vessel.Velocity());
            AoA /= 20;
            part.rb.AddTorque(AoA * simpleStableTorque * dragMagnitude * torqueAxis);
        }

        void ParseModes()
        {
            TargetAquisition = TargetAquisition.ToLower();
            switch (TargetAquisition)
            {
                case "slaved":
                    AquisitionMode = AquisitionModes.Slaved;
                    break;
                case "atlaunch":
                    AquisitionMode = AquisitionModes.AtLaunch;
                    break;
                case "active":
                    AquisitionMode = AquisitionModes.Active;
                    break;
                default:
                    AquisitionMode = AquisitionModes.Slaved;
                    break;
            }

            DroneCapability = DroneCapability.ToLower();
            switch (DroneCapability)
            {
                case "singleuse":
                    DependancyMode = DependancyModes.SingleUse;
                    break;
                case "wingman":
                    DependancyMode = DependancyModes.Wingman;
                    break;
                case "autonomous":
                    DependancyMode = DependancyModes.Autonomous;
                    break;
                default:
                    DependancyMode = DependancyModes.SingleUse;
                    break;
            }
            SensorClass = SensorClass.ToLower();
            switch (SensorClass)
            {
				case "imagerecognition":
                    TargetingMode = TargetingModes.Image;
                    GuidanceMode = GuidanceModes.Drone;
                    break;
                case "heat":
                    TargetingMode = TargetingModes.Heat;
                    GuidanceMode = GuidanceModes.Drone;
                    break;
                case "radar":
                    TargetingMode = TargetingModes.Radar;
                    GuidanceMode = GuidanceModes.Drone;
                    break;
                default:
                    TargetingMode = TargetingModes.Image;
                    GuidanceMode = GuidanceModes.Drone;
                    break;
            }
            Debug.Log("[DRONEDEBUG] stats parsing done. AquisitionMode: " + TargetAquisition + "; parsed:" + AquisitionMode + "; DependancyMode: " + DroneCapability + "; parsed: " + DependancyMode);
        }
		/*
        private string GetBrevityCode()
        {
            if (TargetingMode == TargetingModes.Radar)
            {
                //radar: determine subtype
                if (activeRadarRange <= 0)
                    return "SARH";
                if (activeRadarRange > 0 && activeRadarRange < maxStaticLaunchRange)
                    return "Mixed SARH/F&F";
                if (activeRadarRange >= maxStaticLaunchRange)
                    return "Fire&Forget";
            }

            if (TargetingMode == TargetingModes.Heat)
                return "Fire&Forget";

            // default:
            return "AIRH";
        }

        // RMB info in editor /
        
        public override string GetInfo()
        {            
            ParseModes();

            StringBuilder output = new StringBuilder();
            output.AppendLine($"{missileType.ToUpper()} - {GetBrevityCode()}");
            output.Append(Environment.NewLine);
            output.AppendLine($"Targeting Type: {targetingType.ToLower()}");
            output.AppendLine($"Guidance Mode: {homingType.ToLower()}");
            if (missileRadarCrossSection != RadarUtils.RCS_MISSILES)
            {
                output.AppendLine($"Detectable cross section: {missileRadarCrossSection} m^2");
            }
            output.AppendLine($"Min Range: {minStaticLaunchRange} m");
            output.AppendLine($"Max Range: {maxStaticLaunchRange} m");

            if (TargetingMode == TargetingModes.Radar)
            {
                if (activeRadarRange > 0)
                {
                    output.AppendLine($"Active Radar Range: {activeRadarRange} m");
                    if (activeRadarLockTrackCurve.maxTime > 0)
                        output.AppendLine($"- Lock/Track: {activeRadarLockTrackCurve.Evaluate(activeRadarLockTrackCurve.maxTime)} m^2 @ {activeRadarLockTrackCurve.maxTime} km");
                    else
                        output.AppendLine($"- Lock/Track: {RadarUtils.MISSILE_DEFAULT_LOCKABLE_RCS} m^2 @ {activeRadarRange / 1000} km");
                    output.AppendLine($"- LOAL: {radarLOAL}");
                }
                output.AppendLine($"Max Offborsight: {maxOffBoresight}");
                output.AppendLine($"Locked FOV: {lockedSensorFOV}");
            }

            if (TargetingMode == TargetingModes.Heat)
            {
                output.AppendLine($"All Aspect: {allAspect}");
                output.AppendLine($"Min Heat threshold: {heatThreshold}");
                output.AppendLine($"Max Offborsight: {maxOffBoresight}");
                output.AppendLine($"Locked FOV: {lockedSensorFOV}");
            }

            return output.ToString();            
        }*/
		#endregion
		#region ExhaustPrefabPooling
		static Dictionary<string, ObjectPool> exhaustPrefabPool = new Dictionary<string, ObjectPool>();
        List<GameObject> exhaustPrefabs = new List<GameObject>();

        static void AttachExhaustPrefab(string prefabPath, ModuleDrone Launcher, Transform exhaustTransform)
        {
            CreateExhaustPool(prefabPath);
            var exhaustPrefab = exhaustPrefabPool[prefabPath].GetPooledObject();
            exhaustPrefab.SetActive(true);
            using (var emitter = exhaustPrefab.GetComponentsInChildren<KSPParticleEmitter>().AsEnumerable().GetEnumerator())
                while (emitter.MoveNext())
                {
                    if (emitter.Current == null) continue;
                    emitter.Current.emit = false;
                }
            exhaustPrefab.transform.parent = exhaustTransform;
            exhaustPrefab.transform.localPosition = Vector3.zero;
            exhaustPrefab.transform.localRotation = Quaternion.identity;
            Launcher.exhaustPrefabs.Add(exhaustPrefab);
            Launcher.part.OnJustAboutToDie += Launcher.DetachExhaustPrefabs;
            Launcher.part.OnJustAboutToBeDestroyed += Launcher.DetachExhaustPrefabs;
            //if (BDArmorySettings.DRAW_DEBUG_LABELS) 
            Debug.Log("[BDArmory.DroneLauncher]: Exhaust prefab " + exhaustPrefab.name + " added to " + Launcher.shortName + " on " + (Launcher.vessel != null ? Launcher.vessel.vesselName : "unknown"));
        }

        static void CreateExhaustPool(string prefabPath)
        {
            if (exhaustPrefabPool == null)
            { exhaustPrefabPool = new Dictionary<string, ObjectPool>(); }
            if (!exhaustPrefabPool.ContainsKey(prefabPath) || exhaustPrefabPool[prefabPath] == null || exhaustPrefabPool[prefabPath].poolObject == null)
            {
                var exhaustPrefabTemplate = GameDatabase.Instance.GetModel(prefabPath);
                if (exhaustPrefabTemplate == null)
                {
                    Debug.LogError("[BDArmory.DroneLauncher]: " + prefabPath + " was not found, using the default prefab instead. Please fix your model.");
                    exhaustPrefabTemplate = GameDatabase.Instance.GetModel("BDArmory/Models/exhaust/smallExhaust");
                }
                exhaustPrefabTemplate.SetActive(false);
                exhaustPrefabPool[prefabPath] = ObjectPool.CreateObjectPool(exhaustPrefabTemplate, 1, true, true);
            }
        }

        void DetachExhaustPrefabs()
        {
            if (part != null)
            {
                part.OnJustAboutToDie -= DetachExhaustPrefabs;
                part.OnJustAboutToBeDestroyed -= DetachExhaustPrefabs;
            }
            foreach (var exhaustPrefab in exhaustPrefabs)
            {
                if (exhaustPrefab == null) continue;
                exhaustPrefab.transform.parent = null;
                exhaustPrefab.SetActive(false);
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.DroneLauncher]: Exhaust prefab " + exhaustPrefab.name + " removed from " + shortName + " on " + (vessel != null ? vessel.vesselName : "unknown"));
            }
            exhaustPrefabs.Clear();
        }
		#endregion
		public override float GetBlastRadius()
        {
            return 0;
        }

        #region targeting Stuff
        public bool underAttack;
        public bool underFire;

        public Vector3 incomingThreatPosition;
        public Vessel incomingThreatVessel;
        public float incomingMissDistance;
        public float incomingMissTime;
        public Vessel priorGunThreatVessel = null;
        private ViewScanResults results;

        public bool debilitated = false;

        public bool guardFiringMissile;
        public bool laserPointDetected;

        public bool missileIsIncoming;
        public float incomingMissileLastDetected = 0;
        public float incomingMissileDistance = float.MaxValue;
        public Vessel incomingMissileVessel;
        float targetScanTimer;

        void GuardMode()
        {
            if (!gameObject.activeInHierarchy) return;
            if (BDArmorySettings.PEACE_MODE) return;
            if (!weaponManager.guardMode) return;
            UpdateGuardViewScan();

            //if (missilesAway < 0)
            //    missilesAway = 0;

            //scan and acquire new target
            if (AquisitionMode == AquisitionModes.Active && Time.time - targetScanTimer > 0.5)
            {
                targetScanTimer = Time.time;
                FindTarget();
            }
            if (AquisitionMode == AquisitionModes.Slaved)
            {
                if (weaponManager != null)
                {
                    vesselTarget = weaponManager.currentTarget.Vessel;
                }
                else
                {
                    if (!SDtriggered) StartCoroutine(SelfDestructRoutine(0));
                    SDtriggered = true;
                    Debug.Log("[DRONEDEBUG] Slaved Drone has no mothership");
                }
                if (vesselTarget != null)
                {
                    TargetAcquired = true;
                }
                else TargetAcquired = false;
            }
            if (DependancyMode == DependancyModes.SingleUse)
            {
                if (vesselTarget = null)
                {
                    TargetAcquired = false;
                }
            }
            if (vesselTarget != null)
            {
                TargetAcquired = true;
                weapon.targetCOM = true;
                weapon.autoFireTimer = Time.time;
                weapon.autoFireLength = 1;
                weapon.visualTargetVessel = vesselTarget;
            }
            else
            {
                TargetAcquired = false;
                weapon.visualTargetVessel = null;
                weapon.autoFire = false;
                weapon.autofireShotCount = 0;
                weapon.visualTargetPart = null;
            }
        }

        void UpdateGuardViewScan()
        {
            results = RadarUtils.GuardScanInDirection(null, MissileReferenceTransform, 360, 15000, null, this);
            incomingThreatVessel = null;

            if (results.foundMissile)
            {
                if (BDArmorySettings.DEBUG_MISSILES && !missileIsIncoming)
                {
                    foreach (var incomingMissile in results.incomingMissiles)
                        Debug.Log("[BDArmory.Dronelauncher]: " + vessel.vesselName + " incoming missile (" + incomingMissile.vessel.vesselName + " of type " + incomingMissile.guidanceType + " from " + (incomingMissile.weaponManager != null && incomingMissile.weaponManager.vessel != null ? incomingMissile.weaponManager.vessel.vesselName : "unknown") + ") found at distance " + incomingMissile.distance + "m");
                }
                missileIsIncoming = true;
                incomingMissileLastDetected = Time.time;
                // Assign the closest missile as the main threat. FIXME In the future, we could do something more complex to handle all the incoming missiles.
                incomingMissileDistance = results.incomingMissiles[0].distance;
                incomingThreatPosition = results.incomingMissiles[0].position;
                incomingThreatVessel = results.incomingMissiles[0].vessel;
                incomingMissileVessel = results.incomingMissiles[0].vessel;

                if (results.foundHeatMissile)
                {
                    //FireFlares();
                }
                //Add some sort of evasion routine?
                if (results.foundRadarMissile)
                {
                    //FireChaff();
                    //FireECM();
                }
            }
            else
            {
                incomingMissileDistance = float.MaxValue;
                incomingMissileVessel = null;
            }

            if (results.firingAtMe)
            {
                if (!missileIsIncoming) // Don't override incoming missile threats. FIXME In the future, we could do something more complex to handle all incoming threats.
                {
                    incomingThreatPosition = results.threatPosition;
                    incomingThreatVessel = results.threatVessel;
                }
                if (priorGunThreatVessel == results.threatVessel)
                {
                    incomingMissTime += Time.fixedDeltaTime;
                }
                else
                {
                    priorGunThreatVessel = results.threatVessel;
                    incomingMissTime = 0f;
                }
                if (results.threatWeaponManager != null)
                {
                    incomingMissDistance = results.missDistance;
                }
            }
            else
            {
                incomingMissTime = 0f; // Reset incoming fire time
            }
        }
        public float ThreatClosingTime(Vessel threat)
        {
            float closureTime = 3600f; // Default closure time of one hour
            if (threat) // If we weren't passed a null
            {
                float targetDistance = Vector3.Distance(threat.transform.position, vessel.transform.position);
                Vector3 currVel = (float)vessel.srfSpeed * vessel.Velocity().normalized;
                closureTime = Mathf.Clamp((float)(1 / ((threat.Velocity() - currVel).magnitude / targetDistance)), 0f, closureTime);
                // Debug.Log("[BDArmory.MissileFire]: Threat from " + threat.GetDisplayName() + " is " + closureTime.ToString("0.0") + " seconds away!");
            }
            return closureTime;
        }
        void FindTarget()
        {
            TargetInfo finalTarget = null;
            float finalTargetScore = 0f;
            float hysteresis = 1.1f; // 10% hysteresis
            float bias = 2f; // bias for targets ahead vs behind
            using (var target = BDATargetManager.TargetList(Team).GetEnumerator())
                while (target.MoveNext())
                {
                    if (target.Current == null || target.Current.Vessel == null) continue;
                    if (!target.Current.isMissile && target.Current.isThreat)
                    {
                        float theta = Vector3.Angle(part.vessel.srf_vel_direction, target.Current.transform.position - part.vessel.CoM);
                        float distance = (part.vessel.CoM - target.Current.position).magnitude;
                        float targetScore = (target.Current.Vessel == vesselTarget ? hysteresis : 1f) * ((bias - 1f) * Mathf.Pow(Mathf.Cos(theta / 2f), 2f) + 1f) / distance;
                        if (finalTarget == null || targetScore > finalTargetScore)
                        {
                            finalTarget = target.Current;
                            finalTargetScore = targetScore;
                        }
                    }
                }
            vesselTarget = finalTarget.Vessel;
            if (vesselTarget != null)
            {
                guidanceActive = true;
                TargetAcquired = true;
            }
        }
        private RadarWarningReceiver radarWarn;

        RadarWarningReceiver rwr
        {
            get
            {
                if (!radarWarn || radarWarn.vessel != vessel)
                {
                    return null;
                }
                return radarWarn;
            }
            set { radarWarn = value; }
        }
        #endregion
        protected override void OnGUI()
        {
            base.OnGUI();
            if (!HasFired) return;
            if (!FlightGlobals.ActiveVessel) return;
            if (BDArmorySettings.DEBUG_MISSILES)
            {
                GUI.Label(new Rect(400, Screen.height - 700, 600, 300), $"{vessel.name}\n{debugString.ToString()}");
            }
        }
    }
}
