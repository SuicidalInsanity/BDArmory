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
    /// weapon offset is offset?
    /// </summary>
	public class ModuleDrone : MissileBase
	{
		//Drone specs
		[KSPField]
		double FuelAmount = 5; //flighttime

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
		float minAirspeed = 50;

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
		float MinAlt = 250; //min flight alt

		[KSPField]
		float maxAlt = 5000;

		[KSPField]
		float EvasionTime = 0.2f;

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
        public Vessel targetVessel = null;
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

        bool extending;
        bool extendParametersSet = false;
        float extendDistance;
        float desiredMinAltitude;

        bool requestedExtend;
        Vector3 requestedExtendTpos;
        GameObject vobj;
        Transform velocityTransform
        {
            get
            {
                if (!vobj)
                {
                    vobj = new GameObject("velObject");
                    vobj.transform.position = vessel.ReferenceTransform.position;
                    vobj.transform.parent = vessel.ReferenceTransform;
                }

                return vobj.transform;
            }
        }
        public bool IsExtending
        {
            get { return extending || requestedExtend; }
        }

        public void StopExtending(string reason)
        {
            extending = false;
            if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.DroneAI]: {vessel.vesselName} stopped extending due to {reason}.");
        }

        public void RequestExtend(Vector3 tPosition, Vessel target = null)
        {
            requestedExtend = true;
            requestedExtendTpos = tPosition;
            if (target != null)
                extendTarget = target;
        }

        public Vessel extendTarget = null;

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

        float turnRadius;
        float bodyGravity = (float)PhysicsGlobals.GravitationalAcceleration;
        float dynDynPresGRecorded = 1.0f; 

        public float TurnRadius
        {
            get { return turnRadius; }
            private set { turnRadius = value; }
        }

        float maxLiftAcceleration;

        public float MaxLiftAcceleration
        {
            get { return maxLiftAcceleration; }
            private set { maxLiftAcceleration = value; }
        }
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

        void FixedUpdate()
		{
			//floating origin and velocity offloading corrections
			if (lastTargetPosition != null && (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero()))
			{
				lastTargetPosition -= FloatingOrigin.OffsetNonKrakensbane;
			}
		}
        void CalculateAccelerationAndTurningCircle()
        {
            maxLiftAcceleration = Mathf.Abs((-Vector3.Dot(vessel.acceleration, vessel.ReferenceTransform.forward) / (float)vessel.dynamicPressurekPa)) * (float)vessel.dynamicPressurekPa; //maximum acceleration from lift that the vehicle can provide

            maxLiftAcceleration = Mathf.Clamp(maxLiftAcceleration, bodyGravity, 45 * bodyGravity); //limit it to whichever is smaller, what we can provide or what we can handle. Assume minimum of 1G to avoid extremely high turn radiuses.

            turnRadius = (float)vessel.Velocity().sqrMagnitude / maxLiftAcceleration; //radius that we can turn in assuming constant velocity, assuming simple circular motion (this is a terrible assumption, the AI usually turns on afterboosters!)
        }
		#region FlightAI
		void AutoPilot()
        {
            finalMaxSteer = 1f; // Reset finalMaxSteer, is adjusted in subsequent methods

            if (terrainAlertCoolDown > 0)
                terrainAlertCoolDown -= Time.fixedDeltaTime;

            AdjustThrottle(maxAirspeed);
            Aiming = false;
            //upDirection = -FlightGlobals.getGeeForceAtPosition(transform.position).normalized;
            upDirection = VectorUtils.GetUpDirection(vessel.transform.position);

            CalculateAccelerationAndTurningCircle();

            if ((float)vessel.radarAltitude < MinAlt)
            { belowMinAltitude = true; }

            if (gainAltInhibited && (!belowMinAltitude || !(currentStatus == "Engaging" || currentStatus == "Evading" || currentStatus.StartsWith("Gain Alt"))))
            { // Allow switching between "Engaging", "Evading" and "Gain Alt." while below minimum altitude without disabling the gain altitude inhibitor.
                gainAltInhibited = false;
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.BDDroneAI]: " + vessel.vesselName + " is no longer inhibiting gain alt");
            }

            if (!gainAltInhibited && belowMinAltitude && (currentStatus == "Engaging" || currentStatus == "Evading"))
            { // Vessel went below minimum altitude while "Engaging" or "Evading", enable the gain altitude inhibitor.
                gainAltInhibited = true;
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log("[BDArmory.BDDroneAI]: " + vessel.vesselName + " was " + currentStatus + " and went below min altitude, inhibiting gain alt.");
            }

            if (vessel.srfSpeed < minAirspeed)
            { regainEnergy = true; }
            else if (!belowMinAltitude && vessel.srfSpeed > Mathf.Min(minAirspeed + 20f, maxAirspeed / 2))
            { regainEnergy = false; }


            UpdateVelocityRelativeDirections();
            if (!vessel.LandedOrSplashed && FlyAvoidTerrain())
            { turningTimer = 0; }           
            else
            {
                UpdateAI();
            }
        }

        void UpdateAI()
        {
            currentStatus = "Free";

            CheckExtend(ExtendChecks.RequestsOnly);

            // Calculate threat rating from any threats
            float minimumEvasionTime = EvasionTime;
            threatRating = EvasionDist + 1f; // Don't evade by default
            if (weapon != null)
            {
                if (ThreatClosingTime(incomingMissileVessel) <= 5)
                {
                    threatRating = 0f; // Allow entering evasion code if we're under missile fire
                    minimumEvasionTime = 0f; //  Trying to evade missile threats when they don't exist will result in NREs
                }
                else if (underFire) // If we're ramming, ignore gunfire.
                {
                    if (incomingMissTime >= 0.5f) // If we haven't been under fire long enough, ignore gunfire
                        threatRating = incomingMissDistance;
                }
            }

            debugString.AppendLine($"Threat Rating: {threatRating}");

            // If we're currently evading or a threat is significant and we're not ramming.
            if ((evasiveTimer < minimumEvasionTime && evasiveTimer != 0) || threatRating < EvasionDist)
            {
                if (evasiveTimer < minimumEvasionTime)
                {
                    threatRelativePosition = vessel.Velocity().normalized + vessel.ReferenceTransform.right;

                    Vector3 missileThreat = Vector3.zero;
                    bool missileThreatDetected = false;
                    float closestMissileThreat = float.MaxValue;
                    for (int i = 0; i < rwr.pingsData.Length; i++)
                    {
                        TargetSignatureData threat = rwr.pingsData[i];
                        if (threat.exists && threat.signalStrength == 4)
                        {
                            missileThreatDetected = true;
                            float dist = (rwr.pingWorldPositions[i] - vessel.ReferenceTransform.position).sqrMagnitude;
                            if (dist < closestMissileThreat)
                            {
                                closestMissileThreat = dist;
                                missileThreat = rwr.pingWorldPositions[i];
                            }
                        }
                    }
                    if (missileThreatDetected)
                    {
                        threatRelativePosition = missileThreat - vessel.ReferenceTransform.position;
                        if (extending)
                            StopExtending("missile threat"); // Don't keep trying to extend if under fire from missiles
                    }

                    if (underFire)
                    {
                        threatRelativePosition = incomingThreatPosition - vessel.ReferenceTransform.position;
                    }
                }
                Evasive();
                evasiveTimer += Time.fixedDeltaTime;
                turningTimer = 0;

                if (evasiveTimer >= minimumEvasionTime)
                {
                    evasiveTimer = 0;
                }
            }
            else if (!extending && targetVessel != null && targetVessel.transform != null)
            {
                evasiveTimer = 0;
                if (!targetVessel.LandedOrSplashed)
                {
                    Vector3 targetVesselRelPos = targetVessel.ReferenceTransform.position - vessel.ReferenceTransform.position;
                    if (canExtend && vessel.altitude < maxAlt / 2 && Vector3.Angle(targetVesselRelPos, -upDirection) < 35) // Target is at a steep angle below us and we're below default altitude, extend to get a better angle instead of attacking now.
                    {
                        RequestExtend(targetVessel.ReferenceTransform.position, targetVessel);
                    }

                    if (Vector3.Angle(targetVessel.ReferenceTransform.position - vessel.ReferenceTransform.position, vessel.ReferenceTransform.up) > 35) // If target is outside of 35° cone ahead of us then keep flying straight.
                    {
                        turningTimer += Time.fixedDeltaTime;
                    }
                    else
                    {
                        turningTimer = 0;
                    }

                    debugString.AppendLine($"turningTimer: {turningTimer}");

                    float targetForwardDot = Vector3.Dot(targetVesselRelPos.normalized, vessel.ReferenceTransform.up); // Cosine of angle between us and target (1 if target is in front of us , -1 if target is behind us)
                    float targetVelFrac = (float)(targetVessel.srfSpeed / vessel.srfSpeed);      //this is the ratio of the target vessel's velocity to this vessel's srfSpeed in the forward direction; this allows smart decisions about when to break off the attack

                    float extendTargetDot = Mathf.Cos(extendTargetAngle * Mathf.Deg2Rad);
                    if (canExtend && targetVelFrac < extendTargetVel && targetForwardDot < extendTargetDot && targetVesselRelPos.sqrMagnitude < extendTargetDist * extendTargetDist) // Default values: Target is outside of ~78° cone ahead, closer than 400m and slower than us, so we won't be able to turn to attack it now.
                    {
                        RequestExtend(targetVessel.ReferenceTransform.position - vessel.Velocity(), targetVessel); //we'll set our last target pos based on the enemy vessel and where we were 1 seconds ago
                        targetScanTimer = -100;
                    }
                    if (canExtend && turningTimer > 15)
                    {
                        RequestExtend(targetVessel.ReferenceTransform.position, targetVessel); //extend if turning circles for too long
                        turningTimer = 0;
                        targetScanTimer = -100;
                    }
                }
                else //extend if too close for an air-to-ground attack
                {
                    CheckExtend(ExtendChecks.AirToGroundOnly);
                }

                if (!extending)
                {
                    if (ammoAmount > 0) 
                    {
                        currentStatus = "Engaging";
                        debugString.AppendLine($"Flying to target " + targetVessel.vesselName);
                        FlyToTargetVessel(targetVessel);
                    }
                }
            }
            else
            {
                evasiveTimer = 0;
            }

            if (CheckExtend())
            {
                targetScanTimer = -100;
                evasiveTimer = 0;
                currentStatus = "Extending";
                debugString.AppendLine($"Extending");
                FlyExtend(lastTargetPosition);
            }
        }
        
        void FlyToTargetVessel(Vessel v)
        {
            Vector3 target = AIUtils.PredictPosition(v, TimeWarp.fixedDeltaTime);//v.CoM;
            MissileBase missile = null;
            Vector3 vectorToTarget = v.transform.position - vessel.ReferenceTransform.position;
            float distanceToTarget = vectorToTarget.magnitude;
            float planarDistanceToTarget = Vector3.ProjectOnPlane(vectorToTarget, upDirection).magnitude;
            float angleToTarget = Vector3.Angle(target - vessel.ReferenceTransform.position, vessel.ReferenceTransform.up);
            float strafingDistance = -1f;
            float relativeVelocity = (float)(vessel.srf_velocity - v.srf_velocity).magnitude;            
            if (weapon != null)
            {
                Vector3 leadOffset = weapon.GetLeadOffset();
                float targetAngVel = Vector3.Angle(v.transform.position - vessel.transform.position, v.transform.position + (vessel.Velocity()) - vessel.transform.position);
                float magnifier = Mathf.Clamp(targetAngVel, 1f, 2f);
                magnifier += ((magnifier - 1f) * Mathf.Sin(Time.time * 0.75f));
                target -= magnifier * leadOffset; // The effect of this is to exagerate the lead if the angular velocity is > 1
                angleToTarget = Vector3.Angle(MissileReferenceTransform.forward, target - MissileReferenceTransform.position);
                debugString.AppendLine($"angleToTarget: {angleToTarget:F4}");
                if (distanceToTarget < weaponManager.gunRange && angleToTarget < 20)
                {
                    Aiming = true; //steer to aim
                }                
                debugString.AppendLine($"TargetPos: {target}");
                if (v.LandedOrSplashed)
                {
                    if (distanceToTarget < weapon.engageRangeMax + relativeVelocity) // Distance until starting to strafe plus 1s for changing speed.
                    {
                        strafingDistance = Mathf.Max(0f, distanceToTarget - weapon.engageRangeMax);
                    }
                    if (distanceToTarget > weapon.engageRangeMax)
                    {
                        target = FlightPosition(target, maxAlt / 2);
                    }
                    else
                    {
                        Aiming = true;
                    }
                }
                else if (distanceToTarget > weapon.engageRangeMax * 1.5f || Vector3.Dot(target - vessel.ReferenceTransform.position, vessel.ReferenceTransform.up) < 0) // Target is a long way away or behind us.
                {
                    target = v.CoM; // Don't bother with the off-by-one physics frame correction as this doesn't need to be so accurate here.
                }
            }
            else if (planarDistanceToTarget > weapon.engageRangeMax * 1.25f && (vessel.altitude < targetVessel.altitude || (float)vessel.radarAltitude < maxAlt / 2)) //climb to target vessel's altitude if lower and still too far for guns
            {
                target = vessel.ReferenceTransform.position + GetLimitedClimbDirectionForSpeed(vectorToTarget);
            }

            float targetDot = Vector3.Dot(vessel.ReferenceTransform.up, v.transform.position - vessel.transform.position);

            //manage speed when close to enemy
            float finalmaxAirspeed = maxAirspeed;
            if (targetDot > 0f) // Target is ahead.
            {
                if (strafingDistance < 0f) // target flying, or beyond range of beginning strafing run for landed/splashed targets.
                {
                    if (distanceToTarget > 200) // Adjust target speed based on distance from desired stand-off distance.
                        finalmaxAirspeed = (distanceToTarget - 200) / 8f + (float)v.srfSpeed; // Beyond stand-off distance, approach a little faster.
                    else
                    {
                        //Mathf.Max(finalMaxSpeed = (distanceToTarget - vesselStandoffDistance) / 8f + (float)v.srfSpeed, 0); //for less aggressive braking
                        finalmaxAirspeed = distanceToTarget / 200 * (float)v.srfSpeed; // Within stand-off distance, back off the thottle a bit.
                        debugString.AppendLine($"Getting too close to Enemy. Braking!");
                    }
                }
                else
                {
                    finalmaxAirspeed = minAirspeed + (float)v.srfSpeed;
                }
            }
            finalmaxAirspeed = Mathf.Clamp(finalmaxAirspeed, minAirspeed, maxAirspeed);
            AdjustThrottle(finalmaxAirspeed);

            if ((targetDot < 0 && vessel.srfSpeed > finalmaxAirspeed)
                && distanceToTarget < 300 && vessel.srfSpeed < v.srfSpeed * 1.25f && Vector3.Dot(vessel.Velocity(), v.Velocity()) > 0) //distance is less than 800m
            {
                AdjustThrottle(minAirspeed);
            }
            if (missile != null
                && targetDot > 0
                && distanceToTarget < MissileLaunchParams.GetDynamicLaunchParams(missile, v.Velocity(), v.transform.position).minLaunchRange
                && vessel.srfSpeed > maxAirspeed / 2)
            {
                RequestExtend(targetVessel.transform.position, targetVessel); // Get far enough away to use the missile.
            }

            if (regainEnergy && angleToTarget > 30f)
            {
                RegainEnergy(target - vessel.ReferenceTransform.position);
                return;
            }
            else
            {
                useVelRollTarget = true;
                FlyToPosition(target);
                return;
            }
        }

        void RegainEnergy(Vector3 direction, float throttleOverride = -1f)
        {
            debugString.AppendLine($"Regaining energy");

            Aiming = true;
            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, upDirection);
            float angle = (Mathf.Clamp((float)vessel.radarAltitude - MinAlt, 0, 1500) / 1500) * 90;
            angle = Mathf.Clamp(angle, 0, 55) * Mathf.Deg2Rad;

            Vector3 targetDirection = Vector3.RotateTowards(planarDirection, -upDirection, angle, 0);
            targetDirection = Vector3.RotateTowards(vessel.Velocity(), targetDirection, 15f * Mathf.Deg2Rad, 0).normalized;

            if (throttleOverride >= 0)
                AdjustThrottle(maxAirspeed);
            else
                AdjustThrottle(maxAirspeed);

            FlyToPosition(vessel.ReferenceTransform.position + (targetDirection * 100), true);
        }

        Vector3 prevTargetDir;
        Vector3 debugPos;
        bool useVelRollTarget;

        void FlyToPosition(Vector3 targetPosition, bool overrideThrottle = false)
        {
            if (!belowMinAltitude) // Includes avoidingTerrain
            {
                /*
                if (Time.time - timeBombReleased < 1.5f)
                {
                    targetPosition = vessel.transform.position + vessel.Velocity();
                }
                */
                targetPosition = FlightPosition(targetPosition, MinAlt);
                targetPosition = transform.position + ((targetPosition - transform.position).normalized * 100);
            }

            Vector3d srfVel = vessel.Velocity();
            if (srfVel != Vector3d.zero)
            {
                velocityTransform.rotation = Quaternion.LookRotation(srfVel, -vessel.ReferenceTransform.forward);
            }
            velocityTransform.rotation = Quaternion.AngleAxis(90, velocityTransform.right) * velocityTransform.rotation;

            //ang vel
            Vector3 localAngVel = vessel.angularVelocity;
            //test
            Vector3 currTargetDir = (targetPosition - vessel.ReferenceTransform.position).normalized;
            Vector3 targetAngVel = Vector3.Cross(prevTargetDir, currTargetDir) / Time.fixedDeltaTime;
            Vector3 localTargetAngVel = vessel.ReferenceTransform.InverseTransformVector(targetAngVel);
            prevTargetDir = currTargetDir;
            targetPosition = vessel.transform.position + (currTargetDir * 100);

            flyingToPosition = targetPosition;

            float AoA = Vector3.Angle(vessel.ReferenceTransform.up, vessel.Velocity());
            if (AoA > 30f)
            {
                Aiming = true;
            }

            //slow down for tighter turns
            float velAngleToTarget = Mathf.Clamp(Vector3.Angle(targetPosition - vessel.ReferenceTransform.position, vessel.Velocity()), 0, 90);
            float speedReductionFactor = 1.25f;
            float finalSpeed = Mathf.Min(targetspeed, Mathf.Clamp(maxAirspeed - (speedReductionFactor * velAngleToTarget), maxAirspeed / 2, maxAirspeed));
            debugString.AppendLine($"Final Target Speed: {finalSpeed}");

            if (!overrideThrottle)
            {
                AdjustThrottle(finalSpeed);
            }
            debugPos = vessel.transform.position + (targetPosition - vessel.ReferenceTransform.position) * 5000;

            //roll
            Vector3 currentRoll = -vessel.ReferenceTransform.forward;
            rollTarget = (targetPosition + (10 * upDirection)) - vessel.ReferenceTransform.position;

            //test
            if (!belowMinAltitude)
            {
                angVelRollTarget = -140 * vessel.ReferenceTransform.TransformVector(Quaternion.AngleAxis(90f, Vector3.up) * localTargetAngVel);
                rollTarget += angVelRollTarget;
            }

            bool requiresLowAltitudeRollTargetCorrection = false;
            if (belowMinAltitude)
            {
                if (avoidingTerrain)
                    rollTarget = terrainAlertNormal * 100;
                else
                    rollTarget = vessel.upAxis * 100;
            }
            else if (!avoidingTerrain && vessel.verticalSpeed < 0 && Vector3.Dot(rollTarget, upDirection) < 0 && Vector3.Dot(rollTarget, vessel.Velocity()) < 0) // If we're not avoiding terrain, heading downwards and the roll target is behind us and downwards, check that a circle arc of radius "turn radius" (scaled by twiddle factor minimum) tilted at angle of rollTarget has enough room to avoid hitting the ground.
            {
                var n = Vector3.Cross(vessel.srf_vel_direction, rollTarget).normalized; // Normal of the plane of rollTarget.
                var m = Vector3.Cross(n, upDirection).normalized; // cos(theta) = dot(m,v).
                if (m.magnitude < 0.1f) m = upDirection; // In case n and upDirection are colinear.
                var a = Vector3.Dot(n, upDirection); // sin(phi) = dot(n,up)
                var b = Mathf.Sqrt(1f - a * a); // cos(phi) = sqrt(1-sin(phi)^2)
                var r = turnRadius * 1.25f; // Radius of turning circle.
                var h = r * (1 + Vector3.Dot(m, vessel.srf_vel_direction)) * b; // Required altitude: h = r * (1+cos(theta)) * cos(phi).
                if (vessel.radarAltitude < h) // Too low for this manoeuvre.
                {
                    requiresLowAltitudeRollTargetCorrection = true; // For simplicity, we'll apply the correction after the projections have occurred.
                }
            }
            if (useVelRollTarget && !belowMinAltitude)
            {
                rollTarget = Vector3.ProjectOnPlane(rollTarget, vessel.Velocity());
            }
            else
            {
                rollTarget = Vector3.ProjectOnPlane(rollTarget, vessel.ReferenceTransform.up);
            }
            
            if (requiresLowAltitudeRollTargetCorrection) // Low altitude downwards loop prevention to avoid triggering terrain avoidance.
            {
                rollTarget = Vector3.ProjectOnPlane(rollTarget, upDirection).normalized * 100;
            }
        }

        enum ExtendChecks { All, RequestsOnly, AirToGroundOnly };
        bool CheckExtend(ExtendChecks checkType = ExtendChecks.All)
        {
            // Sanity checks.
            if (weapon == null)
            {
                StopExtending("no weapon manager");
                return false;
            }
            if (!extending) extendParametersSet = false; // Reset this flag for new extends.
            if (requestedExtend)
            {
                requestedExtend = false;
                extending = true;
                lastTargetPosition = requestedExtendTpos;
            }
            if (checkType == ExtendChecks.RequestsOnly) return extending;
            if (extending && extendParametersSet) return true; // Already extending.
            // Ground targets.
            if (targetVessel != null && targetVessel.LandedOrSplashed)
            {
                if (weapon != null && !weapon.engageGround) // Don't extend from ground targets when using a weapon that can't target ground targets.
                {
                    targetScanTimer = -100; // Look for another target instead.
                    return false;
                }
                if (weapon != null) // If using a gun or no weapon is selected, take the extend multiplier into account.
                {
                    extendDistance = Mathf.Clamp(weapon.engageRangeMax - 1800, 500, 4000) * extendMult; // General extending distance.
                    desiredMinAltitude = MinAlt + 0.5f * weapon.engageRangeMax * extendMult; // Desired minimum altitude after extending. (30° attack vector plus min alt.)
                }
                else
                {
                    extendDistance = Mathf.Clamp(weapon.engageRangeMax - 1800, 2500, 4000);
                    desiredMinAltitude = (float)vessel.radarAltitude + (maxAlt / 2 - (float)vessel.radarAltitude) * extendMult; // Desired minimum altitude after extending.
                }
                float srfDist = (GetSurfacePosition(targetVessel.transform.position) - GetSurfacePosition(vessel.transform.position)).sqrMagnitude;
                if (srfDist < extendDistance * extendDistance && Vector3.Angle(vessel.ReferenceTransform.up, targetVessel.transform.position - vessel.transform.position) > 45)
                {
                    extending = true;
                    lastTargetPosition = targetVessel.transform.position;
                    extendTarget = targetVessel;
                    extendParametersSet = true;
                    if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.DroneAI]: {vessel.vesselName} is extending due to a ground target.");
                    return true;
                }
            }
            if (checkType == ExtendChecks.AirToGroundOnly) return false;

            // Air target (from requests, where extendParameters haven't been set yet).
            if (extending && extendTarget != null && !extendTarget.LandedOrSplashed) // We have a flying target, only extend a short distance and don't climb.
            {
                extendDistance = 300 * extendMult; // The effect of this is generally to extend for only 1 frame.
                desiredMinAltitude = MinAlt;
                extendParametersSet = true;
                if (BDArmorySettings.DEBUG_MISSILES) Debug.Log($"[BDArmory.DroneAI]: {vessel.vesselName} is extending due to an air target.");
                return true;
            }

            if (extending) StopExtending("no valid extend reason");
            return false;
        }

        void FlyExtend(Vector3 tPosition)
        {
            Vector3 srfVector = Vector3.ProjectOnPlane(vessel.transform.position - tPosition, upDirection);
            if (srfVector.sqrMagnitude < extendDistance * extendDistance) // Extend from position is closer (horizontally) than the extend distance.
            {
                Vector3 targetDirection = srfVector.normalized * extendDistance;
                Vector3 target = vessel.transform.position + targetDirection; // Target extend position horizontally.
                target = GetTerrainSurfacePosition(target) + (vessel.upAxis * Mathf.Min(maxAlt / 2, MissileGuidance.GetRaycastRadarAltitude(vessel.ReferenceTransform.position))); // Adjust for terrain changes at target extend position.
                target = FlightPosition(target, desiredMinAltitude); // Further adjustments for speed, situation, etc. and desired minimum altitude after extending.
                if (regainEnergy)
                {
                    RegainEnergy(target - vessel.ReferenceTransform.position);
                    return;
                }
                else
                {
                    FlyToPosition(target);
                }
            }
            else // We're far enough away, stop extending.
            {
                StopExtending($"gone far enough (" + srfVector.magnitude + " of " + extendDistance + ")");
            }
        }
        float targetspeed = 0;

        void AdjustThrottle(float targetSpeed)
        {
            //something in here isn't working, returning min thrust values, debug later
            //worst case scenerio, can explore using direct port of speedcontroller code + adding a ModuleEngine to the drone
            targetspeed = targetSpeed;
            double speedDelta = (targetSpeed - vessel.srfSpeed) * 2;
            double gravAccel = FlightGlobals.getGeeForceAtPosition(vessel.CoM).magnitude * Mathf.Cos(Mathf.Deg2Rad * Vector3.Angle(-FlightGlobals.getGeeForceAtPosition(vessel.CoM), vessel.velocityD)); // -g.v/|v| ???
            float dragAccel = 0;

            float estimatedCurrentAccel = (Throttle * thrust) / part.vessel.GetTotalMass() - (float)gravAccel;
            Vector3 vesselAccelProjected = Vector3.Project(vessel.acceleration_immediate, vessel.velocityD.normalized);
            float actualCurrentAccel = vesselAccelProjected.magnitude * Mathf.Sign(Vector3.Dot(vesselAccelProjected, vessel.velocityD.normalized));
            dragAccel = (actualCurrentAccel - estimatedCurrentAccel);

            float engineAccel = thrust / part.vessel.GetTotalMass();

            if (engineAccel == 0)
            {
                Throttle = speedDelta > 0 ? 1 : 0;
                return;
            }
            speedDelta = -gravAccel;
            speedDelta = Mathf.Clamp((float)speedDelta, -engineAccel, engineAccel);

            float requestThrottle = ((float)speedDelta - dragAccel) / engineAccel;
            Throttle = Mathf.Clamp01(requestThrottle);
        }

        Vector3 threatRelativePosition;

        void Evasive()
        {
            if (vessel == null) return;
            if (weapon == null) return;

            currentStatus = "Evading";
            /*
                if (isFlaring)
                {
                    float targetSpeed = minAirspeed;
                    if (weaponManager.isChaffing)
                        targetSpeed = maxAirspeed;
                    AdjustThrottle(targetSpeed);
                }
            */
                //if ((isChaffing || isFlaring) && 
            if(incomingMissileVessel != null) // Missile evasion
                {
                    if ((ThreatClosingTime(incomingMissileVessel) <= 1.5f)) //&& (!isChaffing)) // Missile is about to impact, pull a hard turn
                    {
                        debugString.AppendLine($"Missile about to impact! pull away!");

                        AdjustThrottle(maxAirspeed);

                        Vector3 cross = Vector3.Cross(incomingMissileVessel.transform.position - vessel.ReferenceTransform.position, vessel.Velocity()).normalized;
                        if (Vector3.Dot(cross, -vessel.ReferenceTransform.forward) < 0)
                        {
                            cross = -cross;
                        }
                        FlyToPosition(vessel.ReferenceTransform.position + (50 * vessel.Velocity() / vessel.srfSpeed) + (100 * cross));
                        return;
                    }
                    else // Fly at 90 deg to missile to put max distance between ourselves and dispensed flares/chaff
                    {
                        debugString.AppendLine($"Breaking from missile threat!");

                        // Break off at 90 deg to missile
                        Vector3 threatDirection = incomingMissileVessel.transform.position - vessel.ReferenceTransform.position;
                        threatDirection = Vector3.ProjectOnPlane(threatDirection, upDirection);
                        float sign = Vector3.SignedAngle(threatDirection, Vector3.ProjectOnPlane(vessel.Velocity(), upDirection), upDirection);
                        Vector3 breakDirection = Vector3.ProjectOnPlane(Vector3.Cross(Mathf.Sign(sign) * upDirection, threatDirection), upDirection);

                        // Dive to gain energy and hopefully lead missile into ground
                        float angle = (Mathf.Clamp((float)vessel.radarAltitude - MinAlt, 0, 1500) / 1500) * 90;
                        angle = Mathf.Clamp(angle, 0, 75) * Mathf.Deg2Rad;
                        Vector3 targetDirection = Vector3.RotateTowards(breakDirection, -upDirection, angle, 0);
                        targetDirection = Vector3.RotateTowards(vessel.Velocity(), targetDirection, 15f * Mathf.Deg2Rad, 0).normalized;

                    Aiming = true;
                    AdjustThrottle(maxAirspeed);

                        FlyToPosition(vessel.ReferenceTransform.position + (targetDirection * 100), true);
                        return;
                    }
                }
                else if (underFire)
                {
                    debugString.Append($"Dodging gunfire");
                    float threatDirectionFactor = Vector3.Dot(vessel.ReferenceTransform.up, threatRelativePosition.normalized);
                    //Vector3 axis = -Vector3.Cross(vessel.ReferenceTransform.up, threatRelativePosition);

                    Vector3 breakTarget = threatRelativePosition * 2f;       //for the most part, we want to turn _towards_ the threat in order to increase the rel ang vel and get under its guns

                    if (threatDirectionFactor > 0.9f)     //within 28 degrees in front
                    { // This adds +-500/(threat distance) to the left or right relative to the breakTarget vector, regardless of the size of breakTarget
                        breakTarget += 500f / threatRelativePosition.magnitude * Vector3.Cross(threatRelativePosition.normalized, Mathf.Sign(Mathf.Sin((float)vessel.missionTime / 2)) * vessel.upAxis);
                        debugString.AppendLine($" from directly ahead!");
                    }
                    else if (threatDirectionFactor < -0.9) //within ~28 degrees behind
                    {
                        float threatDistanceSqr = threatRelativePosition.sqrMagnitude;
                        if (threatDistanceSqr > 400 * 400)
                        { // This sets breakTarget 1500m ahead and 500m down, then adds a 1000m offset at 90° to ahead based on missionTime. If the target is kinda close, brakes are also applied.
                            breakTarget = vessel.ReferenceTransform.position + vessel.ReferenceTransform.up * 1500 - 500 * vessel.upAxis;
                            breakTarget += Mathf.Sin((float)vessel.missionTime / 2) * vessel.ReferenceTransform.right * 1000 - Mathf.Cos((float)vessel.missionTime / 2) * vessel.ReferenceTransform.forward * 1000;
                            if (threatDistanceSqr <= 800 * 800)
                            {
                                debugString.AppendLine($" from behind moderate distance; engaging aggressvie barrel roll and braking");
                            Aiming = true;
                            AdjustThrottle(minAirspeed);
                            }
                        }
                        else
                        { // This sets breakTarget to the attackers position, then applies an up to 500m offset to the right or left (relative to the vessel) for the first half of the default evading period, then sets the breakTarget to be 150m right or left of the attacker.
                            breakTarget = threatRelativePosition;
                            if (evasiveTimer < 1.5f)
                                breakTarget += Mathf.Sin((float)vessel.missionTime * 2) * vessel.ReferenceTransform.right * 500;
                            else
                                breakTarget += -Math.Sign(Mathf.Sin((float)vessel.missionTime * 2)) * vessel.ReferenceTransform.right * 150;

                            debugString.AppendLine($" from directly behind and close; breaking hard");
                        Aiming = true;
                        AdjustThrottle(minAirspeed); // Brake to slow down and turn faster while breaking target
                        }
                    }
                    else
                    {
                        float threatDistanceSqr = threatRelativePosition.sqrMagnitude;
                        if (threatDistanceSqr < 400 * 400) // Within 400m to the side.
                        { // This sets breakTarget to be behind the attacker (relative to the evader) with a small offset to the left or right.
                            breakTarget += Mathf.Sin((float)vessel.missionTime * 2) * vessel.ReferenceTransform.right * 100;
                        Aiming = true;
                        debugString.AppendLine($" from near side; turning towards attacker");
                        }
                        else // More than 400m to the side.
                        { // This sets breakTarget to be 1500m ahead, then adds a 1000m offset at 90° to ahead.
                            breakTarget = vessel.ReferenceTransform.position + vessel.ReferenceTransform.up * 1500;
                            breakTarget += Mathf.Sin((float)vessel.missionTime / 2) * vessel.ReferenceTransform.right * 1000 - Mathf.Cos((float)vessel.missionTime / 2) * vessel.ReferenceTransform.forward * 1000;
                            debugString.AppendLine($" from far side; engaging barrel roll");
                        }
                    }

                    float threatAltitudeDiff = Vector3.Dot(threatRelativePosition, vessel.upAxis);
                    if (threatAltitudeDiff > 500)
                        breakTarget += threatAltitudeDiff * vessel.upAxis;      //if it's trying to spike us from below, don't go crazy trying to dive below it
                    else
                        breakTarget += -150 * vessel.upAxis;   //dive a bit to escape

                    float breakTargetVerticalComponent = Vector3.Dot(breakTarget - vessel.transform.position, upDirection);
                    if (belowMinAltitude && breakTargetVerticalComponent < 0) // If we're below minimum altitude, enforce the evade direction to gain altitude.
                    {
                        breakTarget += -2f * breakTargetVerticalComponent * upDirection;
                    }

                    FlyToPosition(breakTarget);
                    return;
                }

            Vector3 target = (vessel.srfSpeed < 200) ? FlightPosition(vessel.transform.position, MinAlt) : vessel.ReferenceTransform.position;
            float angleOff = Mathf.Sin(Time.time * 0.75f) * 180;
            angleOff = Mathf.Clamp(angleOff, -45, 45);
            target += (Quaternion.AngleAxis(angleOff, upDirection) * Vector3.ProjectOnPlane(vessel.ReferenceTransform.up * 500, upDirection));
            //+ (Mathf.Sin (Time.time/3) * upDirection * minAltitude/3);
            debugString.AppendLine($"Evading unknown attacker");
            FlyToPosition(target);
        }

        void UpdateVelocityRelativeDirections() // Vectors that are used in TakeOff and FlyAvoidTerrain.
        {
            relativeVelocityRightDirection = Vector3.Cross(upDirection, vessel.srf_vel_direction).normalized;
            relativeVelocityDownDirection = Vector3.Cross(relativeVelocityRightDirection, vessel.srf_vel_direction).normalized;
        }

        void UpdateTerrainAlertDetectionRadius(Vessel v)
        {
            if (v == vessel)
            {
                terrainAlertDetectionRadius = 2f * vessel.GetRadius();
            }
        }

        bool FlyAvoidTerrain() // Check for terrain ahead.
        {
            bool initialCorrection = !avoidingTerrain;
            float controlLagTime = 1.5f; // Time to fully adjust control surfaces. (Typical values seem to be 0.286s -- 1s for neutral to deployed according to wing lift comparison.) FIXME maybe this could also be a slider.

            ++terrainAlertTicker;
            int terrainAlertTickerThreshold = BDArmorySettings.TERRAIN_ALERT_FREQUENCY * (int)(1 + Mathf.Pow((float)vessel.radarAltitude / 500.0f, 2.0f) / Mathf.Max(1.0f, (float)vessel.srfSpeed / 150.0f)); // Scale with altitude^2 / speed.
            if (terrainAlertTicker >= terrainAlertTickerThreshold)
            {
                terrainAlertTicker = 0;

                // Reset/initialise some variables.
                avoidingTerrain = false; // Reset the alert.
                if (vessel.radarAltitude > MinAlt)
                    belowMinAltitude = false; // Also, reset the belowMinAltitude alert if it's active because of avoiding terrain.
                terrainAlertDistance = -1.0f; // Reset the terrain alert distance.
                float turnRadiusTwiddleFactor = 2; // A twiddle factor based on the orientation of the vessel, since it often takes considerable time to re-orient before avoiding the terrain. Start with the worst value.
                terrainAlertThreatRange = turnRadiusTwiddleFactor * turnRadius + (float)vessel.srfSpeed * controlLagTime; // The distance to the terrain to consider.

                // First, look 45° down, up, left and right from our velocity direction for immediate danger. (This should cover most immediate dangers.)
                Ray rayForwardUp = new Ray(vessel.transform.position, (vessel.srf_vel_direction - relativeVelocityDownDirection).normalized);
                Ray rayForwardDown = new Ray(vessel.transform.position, (vessel.srf_vel_direction + relativeVelocityDownDirection).normalized);
                Ray rayForwardLeft = new Ray(vessel.transform.position, (vessel.srf_vel_direction - relativeVelocityRightDirection).normalized);
                Ray rayForwardRight = new Ray(vessel.transform.position, (vessel.srf_vel_direction + relativeVelocityRightDirection).normalized);
                RaycastHit rayHit;
                if (Physics.Raycast(rayForwardDown, out rayHit, 1.5f * terrainAlertDetectionRadius, 1 << 15)) // sqrt(2) should be sufficient, so 1.5 will cover it.
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction);
                    terrainAlertNormal = rayHit.normal;
                }
                if (Physics.Raycast(rayForwardUp, out rayHit, 1.5f * terrainAlertDetectionRadius, 1 << 15) && (terrainAlertDistance < 0.0f || rayHit.distance < terrainAlertDistance))
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction);
                    terrainAlertNormal = rayHit.normal;
                }
                if (Physics.Raycast(rayForwardLeft, out rayHit, 1.5f * terrainAlertDetectionRadius, 1 << 15) && (terrainAlertDistance < 0.0f || rayHit.distance < terrainAlertDistance))
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction);
                    terrainAlertNormal = rayHit.normal;
                }
                if (Physics.Raycast(rayForwardRight, out rayHit, 1.5f * terrainAlertDetectionRadius, 1 << 15) && (terrainAlertDistance < 0.0f || rayHit.distance < terrainAlertDistance))
                {
                    terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction);
                    terrainAlertNormal = rayHit.normal;
                }
                if (terrainAlertDistance > 0)
                {
                    terrainAlertDirection = Vector3.ProjectOnPlane(vessel.srf_vel_direction, terrainAlertNormal).normalized;
                    avoidingTerrain = true;
                }
                else
                {
                    // Next, cast a sphere forwards to check for upcoming dangers.
                    Ray ray = new Ray(vessel.transform.position, vessel.srf_vel_direction);
                    if (Physics.SphereCast(ray, terrainAlertDetectionRadius, out rayHit, terrainAlertThreatRange, 1 << 15)) // Found something. 
                    {
                        // Check if there's anything directly ahead.
                        ray = new Ray(vessel.transform.position, vessel.srf_vel_direction);
                        terrainAlertDistance = rayHit.distance * -Vector3.Dot(rayHit.normal, vessel.srf_vel_direction); // Distance to terrain along direction of terrain normal.
                        terrainAlertNormal = rayHit.normal;
                        if (!Physics.Raycast(ray, out rayHit, terrainAlertThreatRange, 1 << 15)) // Nothing directly ahead, so we're just barely avoiding terrain.
                        {
                            // Change the terrain normal and direction as we want to just fly over it instead of banking away from it.
                            terrainAlertNormal = upDirection;
                            terrainAlertDirection = vessel.srf_vel_direction;
                        }
                        else
                        { terrainAlertDirection = Vector3.ProjectOnPlane(vessel.srf_vel_direction, terrainAlertNormal).normalized; }
                        float sinTheta = Math.Min(0.0f, Vector3.Dot(vessel.srf_vel_direction, terrainAlertNormal)); // sin(theta) (measured relative to the plane of the surface).
                        float oneMinusCosTheta = 1.0f - Mathf.Sqrt(Math.Max(0.0f, 1.0f - sinTheta * sinTheta));
                        turnRadiusTwiddleFactor = (1.25f + 2) / 2.0f - (2 - 1.25f) / 2.0f * Vector3.Dot(terrainAlertNormal, -vessel.transform.forward); // This would depend on roll rate (i.e., how quickly the vessel can reorient itself to perform the terrain avoidance maneuver) and probably other things.
                        float controlLagCompensation = Mathf.Max(0f, -Vector3.Dot(AIUtils.PredictPosition(vessel, controlLagTime * turnRadiusTwiddleFactor) - vessel.transform.position, terrainAlertNormal)); // Include twiddle factor as more re-orienting requires more control surface movement.
                        terrainAlertThreshold = turnRadiusTwiddleFactor * turnRadius * oneMinusCosTheta + controlLagCompensation;
                        if (terrainAlertDistance < terrainAlertThreshold) // Only do something about it if the estimated turn amount is a problem.
                        {
                            avoidingTerrain = true;

                            // Shoot new ray in direction theta/2 (i.e., the point where we should be parallel to the surface) above velocity direction to check if the terrain slope is increasing.
                            float phi = -Mathf.Asin(sinTheta) / 2f;
                            Vector3 upcoming = Vector3.RotateTowards(vessel.srf_vel_direction, terrainAlertNormal, phi, 0f);
                            ray = new Ray(vessel.transform.position, upcoming);
                            if (Physics.Raycast(ray, out rayHit, terrainAlertThreatRange, 1 << 15))
                            {
                                if (rayHit.distance < terrainAlertDistance / Mathf.Sin(phi)) // Hit terrain closer than expected => terrain slope is increasing relative to our velocity direction.
                                {
                                    terrainAlertNormal = rayHit.normal; // Use the normal of the steeper terrain (relative to our velocity).
                                    terrainAlertDirection = Vector3.ProjectOnPlane(vessel.srf_vel_direction, terrainAlertNormal).normalized;
                                }
                            }
                        }
                    }
                }
                // Finally, check the distance to sea-level as water doesn't act like a collider, so it's getting ignored.
                if (vessel.mainBody.ocean)
                {
                    float sinTheta = Vector3.Dot(vessel.srf_vel_direction, upDirection); // sin(theta) (measured relative to the ocean surface).
                    if (sinTheta < 0f) // Heading downwards
                    {
                        float oneMinusCosTheta = 1.0f - Mathf.Sqrt(Math.Max(0.0f, 1.0f - sinTheta * sinTheta));
                        turnRadiusTwiddleFactor = (3.25f) / 2.0f - (0.75f) / 2.0f * Vector3.Dot(upDirection, -vessel.transform.forward); // This would depend on roll rate (i.e., how quickly the vessel can reorient itself to perform the terrain avoidance maneuver) and probably other things.
                        float controlLagCompensation = Mathf.Max(0f, -Vector3.Dot(AIUtils.PredictPosition(vessel, controlLagTime * turnRadiusTwiddleFactor) - vessel.transform.position, upDirection)); // Include twiddle factor as more re-orienting requires more control surface movement.
                        terrainAlertThreshold = turnRadiusTwiddleFactor * turnRadius * oneMinusCosTheta + controlLagCompensation;

                        if ((float)vessel.altitude < terrainAlertThreshold && (terrainAlertDistance < 0 || (float)vessel.altitude < terrainAlertDistance)) // If the ocean surface is closer than the terrain (if any), then override the terrain alert values.
                        {
                            terrainAlertDistance = (float)vessel.altitude;
                            terrainAlertNormal = upDirection;
                            terrainAlertDirection = Vector3.ProjectOnPlane(vessel.srf_vel_direction, upDirection).normalized;
                            avoidingTerrain = true;
                        }
                    }
                }
            }

            if (avoidingTerrain)
            {
                belowMinAltitude = true; // Inform other parts of the code to behave as if we're below minimum altitude.

                float maxAngle = 70.0f * Mathf.Deg2Rad; // Maximum angle (towards surface normal) to aim.
                if (BDArmorySettings.SPACE_HACKS)
                {
                    maxAngle = 180.0f * Mathf.Deg2Rad;
                }
                float adjustmentFactor = 1f; // Mathf.Clamp(1.0f - Mathf.Pow(terrainAlertDistance / terrainAlertThreatRange, 2.0f), 0.0f, 1.0f); // Don't yank too hard as it kills our speed too much. (This doesn't seem necessary.)
                                             // First, aim up to maxAngle towards the surface normal.
                Vector3 correctionDirection = Vector3.RotateTowards(terrainAlertDirection, terrainAlertNormal, maxAngle * adjustmentFactor, 0.0f);
                // Then, adjust the vertical pitch for our speed (to try to avoid stalling).
                if (!BDArmorySettings.SPACE_HACKS) //no need to worry about stalling in null atmo
                {
                    Vector3 horizontalCorrectionDirection = Vector3.ProjectOnPlane(correctionDirection, upDirection).normalized;
                    correctionDirection = Vector3.RotateTowards(correctionDirection, horizontalCorrectionDirection, Mathf.Max(0.0f, (1.0f - (float)vessel.srfSpeed / 120.0f) * 0.8f * maxAngle) * adjustmentFactor, 0.0f); // Rotate up to 0.8*maxAngle back towards horizontal depending on speed < 120m/s.
                }
                float alpha = Time.fixedDeltaTime * 2f; // 0.04 seems OK.
                float beta = Mathf.Pow(1.0f - alpha, terrainAlertTickerThreshold);
                terrainAlertCorrectionDirection = initialCorrection ? correctionDirection : (beta * terrainAlertCorrectionDirection + (1.0f - beta) * correctionDirection).normalized; // Update our target direction over several frames (if it's not the initial correction) due to changing terrain. (Expansion of N iterations of A = A*(1-a) + B*a. Not exact due to normalisation in the loop, but good enough.)
                FlyToPosition(vessel.transform.position + terrainAlertCorrectionDirection * 100);
                // Update status and book keeping.
                currentStatus = "Terrain (" + (int)terrainAlertDistance + "m)";
                terrainAlertCoolDown = 0.5f; // 0.5s cool down after avoiding terrain or gaining altitude. (Only used for delaying "orbitting" for now.)
                return true;
            }

            // Hurray, we've avoided the terrain!
            avoidingTerrain = false;
            return false;
        }

        Vector3 GetLimitedClimbDirectionForSpeed(Vector3 direction)
        {
            if (Vector3.Dot(direction, upDirection) < 0)
            {
                debugString.AppendLine($"climb limit angle: unlimited");
                return direction; //only use this if climbing
            }

            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, upDirection).normalized * 100;

            float angle = Mathf.Clamp((float)vessel.srfSpeed * 0.13f, 5, 90);

            debugString.AppendLine($"climb limit angle: {angle}");
            return Vector3.RotateTowards(planarDirection, direction, angle * Mathf.Deg2Rad, 0);
        }
      
        Vector3 DefaultAltPosition()
        {
            return (vessel.transform.position + (-(float)vessel.altitude * upDirection) + (maxAlt / 2 * upDirection));
        }

        Vector3 GetSurfacePosition(Vector3 position)
        {
            return position - ((float)FlightGlobals.getAltitudeAtPos(position) * upDirection);
        }

        Vector3 GetTerrainSurfacePosition(Vector3 position)
        {
            return position - (MissileGuidance.GetRaycastRadarAltitude(position) * upDirection);
        }

        Vector3 FlightPosition(Vector3 targetPosition, float minAlt)
        {
            Vector3 forwardDirection = vessel.ReferenceTransform.up;
            Vector3 targetDirection = (targetPosition - vessel.ReferenceTransform.position).normalized;

            float vertFactor = 0;
            vertFactor += (((float)vessel.srfSpeed / minAirspeed) - 2f) * 0.3f;          //speeds greater than 2x minAirspeed encourage going upwards; below encourages downwards
            vertFactor += (((targetPosition - vessel.ReferenceTransform.position).magnitude / 1000f) - 1f) * 0.3f;    //distances greater than 1000m encourage going upwards; closer encourages going downwards
            vertFactor -= Mathf.Clamp01(Vector3.Dot(vessel.ReferenceTransform.position - targetPosition, upDirection) / 1600f - 1f) * 0.5f;       //being higher than 1600m above a target encourages going downwards
            if (targetVessel)
                vertFactor += Vector3.Dot(targetVessel.Velocity() / targetVessel.srfSpeed, (targetVessel.ReferenceTransform.position - vessel.ReferenceTransform.position).normalized) * 0.3f;   //the target moving away from us encourages upward motion, moving towards us encourages downward motion
            else
                vertFactor += 0.4f;
            vertFactor -= (underFire) ? 0.5f : 0;   //being under fire encourages going downwards as well, to gain energy

            float alt = (float)vessel.radarAltitude;

            if (vertFactor > 2)
                vertFactor = 2;
            if (vertFactor < -2)
                vertFactor = -2;

            vertFactor += 0.15f * Mathf.Sin((float)vessel.missionTime * 0.25f);     //some randomness in there

            Vector3 projectedDirection = Vector3.ProjectOnPlane(forwardDirection, upDirection);
            Vector3 projectedTargetDirection = Vector3.ProjectOnPlane(targetDirection, upDirection);
            if (!Aiming)
            {
                float distance = (targetPosition - vessel.ReferenceTransform.position).magnitude;
                if (vertFactor < 0)
                    distance = Math.Min(distance, Math.Abs((alt - minAlt) / vertFactor));

                targetPosition += upDirection * Math.Min(distance, 1000) * vertFactor * Mathf.Clamp01(0.7f - Math.Abs(Vector3.Dot(projectedTargetDirection, projectedDirection)));

                    var targetRadarAlt = BodyUtils.GetRadarAltitudeAtPos(targetPosition);
                    if (targetRadarAlt > maxAlt)
                    {
                        targetPosition -= (targetRadarAlt - maxAlt) * upDirection;
                    }
            }

            if ((float)vessel.radarAltitude > minAlt * 1.1f)
            {
                return targetPosition;
            }

            float pointRadarAlt = MissileGuidance.GetRaycastRadarAltitude(targetPosition);
            if (pointRadarAlt < minAlt)
            {
                float adjustment = (minAlt - pointRadarAlt);
                debugString.AppendLine($"Target position is below minAlt. Adjusting by {adjustment}");
                return targetPosition + (adjustment * upDirection);
            }
            else
            {
                return targetPosition;
            }
        }
#endregion

		public static FloatCurve DefaultLiftCurve = null;
        public static FloatCurve DefaultDragCurve = null;

        public static Vector3 DoAeroForces(ModuleDrone part, Vector3 targetPosition, float liftArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxAoA)
        {
            if (DefaultLiftCurve == null)
            {
                DefaultLiftCurve = new FloatCurve();
                DefaultLiftCurve.Add(0, 0);
                DefaultLiftCurve.Add(8, .35f);
                //	DefaultLiftCurve.Add(19, 1);
                //	DefaultLiftCurve.Add(23, .9f);
                DefaultLiftCurve.Add(30, 1.5f);
                DefaultLiftCurve.Add(65, .6f);
                DefaultLiftCurve.Add(90, .7f);
            }

            if (DefaultDragCurve == null)
            {
                DefaultDragCurve = new FloatCurve();
                DefaultDragCurve.Add(0, 0.00215f);
                DefaultDragCurve.Add(5, .00285f);
                DefaultDragCurve.Add(15, .007f);
                DefaultDragCurve.Add(29, .01f);
                DefaultDragCurve.Add(55, .3f);
                DefaultDragCurve.Add(90, .5f);
            }

            FloatCurve liftCurve = DefaultLiftCurve;
            FloatCurve dragCurve = DefaultDragCurve;

            return DoAeroForces(part, targetPosition, liftArea, steerMult, previousTorque, maxTorque, maxAoA, liftCurve,
                dragCurve);
        }

        public static Vector3 DoAeroForces(ModuleDrone drone, Vector3 targetPosition, float liftArea, float steerMult,
            Vector3 previousTorque, float maxTorque, float maxAoA, FloatCurve liftCurve, FloatCurve dragCurve)
        {
            Rigidbody rb = drone.part.rb;
            if (rb == null || rb.mass == 0) return Vector3.zero;
            double airDensity = drone.vessel.atmDensity;
            double airSpeed = drone.vessel.srfSpeed;
            Vector3d velocity = drone.vessel.Velocity();

            //temp values
            Vector3 CoL = new Vector3(0, 0, -1f);
            float liftMultiplier = BDArmorySettings.GLOBAL_LIFT_MULTIPLIER;
            float dragMultiplier = BDArmorySettings.GLOBAL_DRAG_MULTIPLIER;

            //lift
            float AoA = Mathf.Clamp(Vector3.Angle(drone.transform.forward, velocity.normalized), 0, 90);
            if (AoA > 0)
            {
                double liftForce = 0.5 * airDensity * Math.Pow(airSpeed, 2) * liftArea * liftMultiplier * liftCurve.Evaluate(AoA);
                Vector3 forceDirection = Vector3.ProjectOnPlane(-velocity, drone.transform.forward).normalized;
                rb.AddForceAtPosition((float)liftForce * forceDirection,
                    drone.transform.TransformPoint(drone.part.CoMOffset + CoL));
            }

            //drag
            if (airSpeed > 0)
            {
                double dragForce = 0.5 * airDensity * Math.Pow(airSpeed, 2) * liftArea * dragMultiplier * dragCurve.Evaluate(AoA);
                rb.AddForceAtPosition((float)dragForce * -velocity.normalized,
                    drone.transform.TransformPoint(drone.part.CoMOffset + CoL));
            }

            //guidance
            if (airSpeed > 1 || (drone.vacuumSteerable && drone.hasRCS))
            {
                Vector3 targetDirection;
                float targetAngle;
                if (AoA < maxAoA)
                {
                    targetDirection = (targetPosition - drone.transform.position);
                    targetAngle = Vector3.Angle(velocity.normalized, targetDirection) * 4;
                }
                else
                {
                    targetDirection = velocity.normalized;
                    targetAngle = AoA;
                }

                Vector3 torqueDirection = -Vector3.Cross(targetDirection, velocity.normalized).normalized;
                torqueDirection = drone.transform.InverseTransformDirection(torqueDirection);

                float torque = Mathf.Clamp(targetAngle * steerMult, 0, maxTorque);
                Vector3 finalTorque = Vector3.ProjectOnPlane(Vector3.Lerp(previousTorque, torqueDirection * torque, 1),
                    Vector3.forward);

                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
            else
            {
                Vector3 finalTorque = Vector3.ProjectOnPlane(Vector3.Lerp(previousTorque, Vector3.zero, 0.25f),
                    Vector3.forward);
                rb.AddRelativeTorque(finalTorque);
                return finalTorque;
            }
        }
        #region Launchparams
        public override void FireMissile()
        {
            if (HasFired) return;

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
                    //Debug.Log("[DRONEDEBUG] Ammo found successfully");
                }
                //TARGETING
                TargetPosition = transform.position + (transform.forward * 5000); //set initial target position so if no target update, missileBase will count a miss if it nears this point or is flying post-thrust
                startDirection = transform.forward;
                if (AquisitionMode == AquisitionModes.AtLaunch)
                {
                    targetVessel = weaponManager.currentTarget.Vessel;
                }
                part.crashTolerance = 9999; //to combat stresses of launch, missle generate a lot of G Force
                part.decouple(0);
                part.force_activate();
                part.Unpack();
                vessel.situation = Vessel.Situations.FLYING;
                part.rb.isKinematic = false;
                part.bodyLiftMultiplier = 0;
                part.dragModel = Part.DragModel.NONE;

                //add target info to vessel
                AddTargetInfoToVessel();
                if (AquisitionMode == AquisitionModes.Slaved)
                {
                    if (weaponManager != null)
                    {
                        if (weaponManager.currentTarget != null)
                        {
                            targetVessel = weaponManager.currentTarget.Vessel;
                        }
                    }
                    else //parentVessel WM somehow destroyed moment of launch, SD
                    {
                        if (!SDtriggered) StartCoroutine(SelfDestructRoutine(0));
                        Debug.Log("[DRONEDEBUG] Slaved drone lost connection to parent WM ");
                        SDtriggered = true;
                    }
                }
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
        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight && MissileState == MissileStates.Cruise)
            {
                if (BDArmorySetup.GameIsPaused) return;
                vessel.GetConnectedResourceTotals(AmmoID, out double ammoCurrent, out double ammoMax); //ammo count was originally updating only for active vessel, while reload can be called by any loaded vessel, and needs current ammo count
                ammoAmount = ammoCurrent;

                if (!SDtriggered && (FuelAmount <= 0 || ammoAmount <= 0))
                {
                    StartCoroutine(SelfDestructRoutine(10));
                    Debug.Log("[DRONEDEBUG] Drone Fuel/Ammo depleted");
                }
                if (!SDtriggered && (DependancyMode == DependancyModes.Wingman && SourceVessel == null))
                {
                    StartCoroutine(SelfDestructRoutine(0));
                    Debug.Log("[DRONEDEBUG] Wingman Drone lost connection to mothership");
                }
                if (!SDtriggered && (MissileState != MissileStates.Drop && (DependancyMode == DependancyModes.SingleUse && targetVessel == null)))
                {
                    StartCoroutine(SelfDestructRoutine(10));
                    Debug.Log("[DRONEDEBUG] Single-Target drone lost target");
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
        public override void OnFixedUpdate()
        {
            if (BDArmorySetup.GameIsPaused) return;
            if (!HasFired) return;
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
                if (FuelAmount > 0)
                {
                    UpdateThrustForces();
                }
                else
                {
                    if (!SDtriggered)
                    {
                        SDtriggered = true;
                        StartCoroutine(SelfDestructRoutine(10));
                        Debug.Log("[DRONEDEBUG] Drone fuel/ammo depleted");
                    }
                }
                if (!SDtriggered)
                {
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
                if (targetVessel != null)
                {
                    debugString.Append($"Target Vessel: {targetVessel.GetName()}");
                }
                else
                {
                    debugString.Append("Target Vessel: null");
                }
                debugString.Append(Environment.NewLine);
                UpdateIRTarget();
                debugTarget = flyingToPosition.ToString();
            }

            if (MissileState != MissileStates.Drop) //guidance
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
                            aeroTorque = DoAeroForces(this, flyingToPosition, liftArea, controlAuthority * steerMult, aeroTorque, finalMaxTorque, maxAoA);
                            //part.transform.rotation = Quaternion.RotateTowards(part.transform.rotation, Quaternion.LookRotation(MissileReferenceTransform.forward, rollTarget), turnRateDPS * Time.fixedDeltaTime);
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
            if (!TargetAcquired)
            {
                if (AquisitionMode == AquisitionModes.AtLaunch)//
                {
                    //if (DependancyMode == DependancyModes.SingleUse) //killed its target, begin selfdestruct
                    {
                        guidanceActive = false;
                        targetVessel = null;
                        return;
                    }
                    //have Atlaunch multitarget drones lock current target of sourceVessel
                }
                else if (AquisitionMode == AquisitionModes.Slaved)//
                {
                    if (DependancyMode == DependancyModes.SingleUse) //killed its target, begin selfdestruct
                    {
                        guidanceActive = false;
                        targetVessel = null;
                        return;
                    }
                    else
                    {
                        targetVessel = SourceVessel; //multi-target drone slaved to mothership; return to motherhip
                    }
                }
                else //autonomous target aquisition
                {
                    if (DependancyMode == DependancyModes.SingleUse) //killed its target, begin selfdestruct
                    {
                        guidanceActive = false;
                        targetVessel = null;
                        return;
                    }
                    targetVessel = SourceVessel; //return to mothership until new targets present themself
                }
            }
            AutoPilot();
        }

        void UpdateThrustForces()
        {
            if (Throttle > 0)
            {
                currentThrust = Mathf.Lerp(currentThrust, Throttle * thrust, 0.1f);
                //currentThrust *= (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position),
                //                  FlightGlobals.getExternalTemperature(transform.position));
                part.rb.AddRelativeForce(currentThrust * Vector3.forward);
                if (!CheatOptions.InfinitePropellant) FuelAmount -= (0.03f * Throttle * Time.fixedDeltaTime);
            }
        }

        IEnumerator DroneRoutine()
        {
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
            while (FuelAmount > 0)
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
                yield return null;
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

            if (targetVessel != null)
            {
                using (var wpm = VesselModuleRegistry.GetModules<MissileFire>(targetVessel).GetEnumerator())
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
                    targetVessel = weaponManager.currentTarget.Vessel;
                }
                else
                {
                    if (!SDtriggered) StartCoroutine(SelfDestructRoutine(0));
                    SDtriggered = true;
                    Debug.Log("[DRONEDEBUG] Slaved Drone has no mothership");
                }
                if (targetVessel != null)
                {
                    TargetAcquired = true;
                }
                else TargetAcquired = false;
            }
            if (DependancyMode == DependancyModes.SingleUse)
            {
                if (targetVessel = null)
                {
                    TargetAcquired = false;
                }
            }
            if (targetVessel != null)
            {
                TargetAcquired = true;
                weapon.targetCOM = true;
                weapon.autoFireTimer = Time.time;
                weapon.autoFireLength = 1;
                weapon.visualTargetVessel = targetVessel;
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
            results = RadarUtils.GuardScanInDirection(part.vessel, null, MissileReferenceTransform, 360, 15000, this);
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
                        float targetScore = (target.Current.Vessel == targetVessel ? hysteresis : 1f) * ((bias - 1f) * Mathf.Pow(Mathf.Cos(theta / 2f), 2f) + 1f) / distance;
                        if (finalTarget == null || targetScore > finalTargetScore)
                        {
                            finalTarget = target.Current;
                            finalTargetScore = targetScore;
                        }
                    }
                }
            targetVessel = finalTarget.Vessel;
            if (targetVessel != null)
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
        void OnGUI()
        {
            if (!HasFired) return;
            if (!FlightGlobals.ActiveVessel) return;
            if (BDArmorySettings.DEBUG_MISSILES)
            {
                GUI.Label(new Rect(400, Screen.height - 700, 600, 300), $"{vessel.name}\n{debugString.ToString()}");
            }
        }
    }
}
