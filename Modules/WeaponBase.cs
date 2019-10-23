using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BDArmory.Bullets;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Utils;
using BDArmory.FX;
using BDArmory.Misc;
using BDArmory.Targeting;
using BDArmory.UI;
using KSP.UI.Screens;
using UniLinq;
using UnityEngine;

namespace BDArmory.Modules
{
	public class WeaponBase : EngageableWeapon, IBDWeapon
	{
		#region Declarations

		Coroutine startupRoutine;
		Coroutine shutdownRoutine;

		internal bool finalFire;

		public int rippleIndex = 0;
		public string OriginalShortName { get; private set; }

		// WeaponTypes.Cannon is deprecated.  identical behavior is achieved with WeaponType.Ballistic and bulletInfo.explosive = true.
		public enum WeaponTypes
		{
			Ballistic,
			Rocket, //Cannon's depreciated, lets use this for rocketlaunchers
			Laser
		}

		public enum WeaponStates
		{
			Enabled,
			Disabled,
			PoweringUp,
			PoweringDown
		}

		public enum BulletDragTypes
		{
			None,
			AnalyticEstimate,
			NumericalIntegration
		}

		public WeaponStates weaponState = WeaponStates.Disabled;

		//animations
		internal float fireAnimSpeed = 1;
		//is set when setting up animation so it plays a full animation for each shot (animation speed depends on rate of fire)



		public WeaponTypes eWeaponType;

		public float heat;
		public bool isOverheated;

		internal bool wasFiring;
		//used for knowing when to stop looped audio clip (when you're not shooting, but you were)

		AudioClip reloadCompleteAudioClip;
		internal AudioClip fireSound;
		internal AudioClip overheatSound;
		internal AudioClip chargeSound;
		internal AudioSource audioSource;
		internal AudioSource audioSource2;
		AudioLowPassFilter lowpassFilter;

		private BDStagingAreaGauge gauge;
		internal int AmmoID;

		//AI
		public bool aiControlled = false;
		public bool autoFire;
		public float autoFireLength = 0;
		public float autoFireTimer = 0;

		//used by AI to lead moving targets
		internal float targetDistance;
		internal Vector3 targetPosition;
		internal Vector3 targetVelocity;  // local frame velocity
		internal Vector3 targetAcceleration; // local frame
		internal Vector3 targetVelocityPrevious; // for acceleration calculation
		internal Vector3 targetAccelerationPrevious;
		internal Vector3 relativeVelocity;
		internal Vector3 finalAimTarget;
		internal Vector3 lastFinalAimTarget;
		public Vessel visualTargetVessel;
		bool targetAcquired;

		public Vector3? FiringSolutionVector => finalAimTarget.IsZero() ? (Vector3?)null : (finalAimTarget - fireTransforms[0].position).normalized;

		public bool recentlyFiring //used by guard to know if it should evaid this
		{
			get { return Time.time - timeFired < 1; }
		}

		//used to reduce volume of audio if multiple guns are being fired (needs to be improved/changed)
		//private int numberOfGuns = 0;

		//AI will fire gun if target is within this Cos(angle) of barrel
		public float maxAutoFireCosAngle = 0.9993908f; //corresponds to ~2 degrees

		//aimer textures
		Vector3 pointingAtPosition;
		Vector3 bulletPrediction;
		Vector3 fixedLeadOffset = Vector3.zero;

		float predictedFlightTime = 1;

		//gapless particles
		internal List<BDAGaplessParticleEmitter> gaplessEmitters = new List<BDAGaplessParticleEmitter>();

		//module references
		[KSPField] public int turretID = 0;
		public ModuleTurret turret;
		MissileFire mf;

		public MissileFire weaponManager
		{
			get
			{
				if (mf) return mf;
				List<MissileFire>.Enumerator wm = vessel.FindPartModulesImplementing<MissileFire>().GetEnumerator();
				while (wm.MoveNext())
				{
					if (wm.Current == null) continue;
					mf = wm.Current;
					break;
				}
				wm.Dispose();
				return mf;
			}
		}

		internal bool pointingAtSelf; //true if weapon is pointing at own vessel
		internal bool userFiring;

		public bool slaved;

		public Transform turretBaseTransform
		{
			get
			{
				if (turret)
				{
					return turret.yawTransform.parent;
				}
				else
				{
					return fireTransforms[0];
				}
			}
		}

		public float maxPitch
		{
			get { return turret ? turret.maxPitch : 0; }
		}

		public float minPitch
		{
			get { return turret ? turret.minPitch : 0; }
		}

		public float yawRange
		{
			get { return turret ? turret.yawRange : 0; }
		}

		//weapon interface
		public WeaponClasses GetWeaponClass()
		{
			if (eWeaponType == WeaponTypes.Ballistic)
			{
				return WeaponClasses.Gun;
			}
			else if (eWeaponType == WeaponTypes.Rocket)
			{
				return WeaponClasses.Rocket;
			}
			else
			{
				return WeaponClasses.DefenseLaser;
			}
		}

		public Part GetPart()
		{
			return part;
		}

		public string ammoLeft;

		public string GetSubLabel()
		{
			return ammoLeft;
		}

		public string GetMissileType()
		{
			return string.Empty;
		}

#if DEBUG
		Vector3 relVelAdj;
		Vector3 accAdj;
		Vector3 gravAdj;
#endif

		#endregion Declarations

		#region KSPFields

		[KSPField(isPersistant = true, guiActive = true, guiName = "Weapon Name ", guiActiveEditor = true), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]
		public string WeaponName;

		[KSPField]
		public string fireTransformName = "fireTransform";
		public Transform[] fireTransforms;

		[KSPField]
		public bool hasDeployAnim = false;

		[KSPField]
		public string deployAnimName = "deployAnim";
		AnimationState deployState;

		[KSPField]
		public bool hasFireAnimation = false;

		[KSPField]
		public string fireAnimName = "fireAnim";
		internal AnimationState fireState;

		[KSPField]
		public bool spinDownAnimation = false;
		internal bool spinningDown;

		//weapon specifications
		[KSPField]
		public float maxTargetingRange = 2000; //max range for raycasting and sighting

		[KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Rate of Fire"),
		UI_FloatRange(minValue = 100f, maxValue = 1500, stepIncrement = 25f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
		public float roundsPerMinute; //rocket RoF slider
		[KSPField]
		public float maxDeviation = 1; //inaccuracy two standard deviations in degrees (two because backwards compatibility :)

		[KSPField]
		public float maxEffectiveDistance = 2500; //used by AI to select appropriate weapon

		

		[KSPField]
		public float ECPerShot = 0; //EC to use per shot for weapons like railguns

		

		[KSPField]
		public string ammoName = "50CalAmmo"; //resource usage

		[KSPField]
		public float requestResourceAmount = 1; //amount of resource/ammo to deplete per shot

		[KSPField]
		public float shellScale = 0.66f; //scale of shell to eject

		
		[KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Fire Limits"),
		 UI_Toggle(disabledText = "None", enabledText = "In range")]
		public bool onlyFireInRange = true;
		//prevent firing when gun's turret is trying to exceed gimbal limits

		

		[KSPField]
		public string weaponType = "ballistic";
		//ballistic, rocket or laser

		//TODO: deprectated, moved to bullet config
		

		

		//Rocket info; 
		
		//projectile graphics
		[KSPField]
		public string projectileColor = "255, 130, 0, 255"; //final color of projectile
		internal Color projectileColorC;

		[KSPField]
		public bool fadeColor = false;

		[KSPField]
		public string startColor = "255, 160, 0, 200";
		//if fade color is true, projectile starts at this color

		internal Color startColorC;

		[KSPField]
		public float tracerStartWidth = 0.25f;

		[KSPField]
		public float tracerEndWidth = 0.2f;

		

		

		[KSPField]
		public bool oneShotWorldParticles = false;

		//heat
		[KSPField]
		public float maxHeat = 3600;

		[KSPField]
		public float heatPerShot = 75;

		[KSPField]
		public float heatLoss = 250;

		//canon explosion effects
		[KSPField]
		public string explModelPath = "BDArmory/Models/explosion/explosion";

		[KSPField]
		public string explSoundPath = "BDArmory/Sounds/explode1";

		

		//audioclip paths
		[KSPField]
		public string fireSoundPath = "BDArmory/Parts/50CalTurret/sounds/shot";

		[KSPField]
		public string overheatSoundPath = "BDArmory/Parts/50CalTurret/sounds/turretOverheat";

		[KSPField]
		public string chargeSoundPath = "BDArmory/Parts/laserTest/sounds/charge";

		//audio
		[KSPField]
		public bool oneShotSound = true;
		//play audioclip on every shot, instead of playing looping audio while firing

		[KSPField]
		public float soundRepeatTime = 1;
		//looped audio will loop back to this time (used for not playing the opening bit, eg the ramp up in pitch of gatling guns)

		[KSPField]
		public string reloadAudioPath = string.Empty;
		AudioClip reloadAudioClip;

		[KSPField]
		public string reloadCompletePath = string.Empty;

		[KSPField]
		public bool showReloadMeter = false; //used for cannons or guns with extremely low rate of fire

		//Air Detonating Rounds
		[KSPField]
		public bool airDetonation = false;

		[KSPField]
		public bool proximityDetonation = false;

		[KSPField(isPersistant = true, guiActive = true, guiName = "Fuzed Detonation Range ", guiActiveEditor = false)]
		public float defaultDetonationRange = 3500; // maxairDetrange works for altitude fuzing, use this for VT fuzing

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Proximity Fuze Radius"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.All)]
		public float detonationRange = -1f; // give ability to set proximity range
		
		[KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Max Detonation Range"),
		 UI_FloatRange(minValue = 500, maxValue = 8000f, stepIncrement = 5f, scene = UI_Scene.All)]
		public float maxAirDetonationRange = 3500; // could probably get rid of this entirely, max engagement range more or less already does this

		[KSPField]
		public bool airDetonationTiming = true;

		//auto proximity tracking
		[KSPField]
		public float autoProxyTrackRange = 0;
		bool atprAcquired;
		int aptrTicker;

		internal float timeFired;
		public float initialFireDelay = 0; //used to ripple fire multiple weapons of this type

		[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Barrage")]
		public bool
			useRippleFire = true;

		[KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "Toggle Barrage")]
		public void ToggleRipple()
		{
			List<Part>.Enumerator craftPart = EditorLogic.fetch.ship.parts.GetEnumerator();
			while (craftPart.MoveNext())
			{
				if (craftPart.Current == null) continue;
				if (craftPart.Current.name != part.name) continue;
				List<WeaponBase>.Enumerator weapon = craftPart.Current.FindModulesImplementing<WeaponBase>().GetEnumerator();
				while (weapon.MoveNext())
				{
					if (weapon.Current == null) continue;
					weapon.Current.useRippleFire = !weapon.Current.useRippleFire;
				}
				weapon.Dispose();
			}
			craftPart.Dispose();
		}

		internal IEnumerator IncrementRippleIndex(float delay)
		{
			if (delay > 0)
			{
				yield return new WaitForSeconds(delay);
			}
			weaponManager.gunRippleIndex = weaponManager.gunRippleIndex + 1;

			//Debug.Log("incrementing ripple index to: " + weaponManager.gunRippleIndex);
		}

		#endregion KSPFields

		#region KSPActions

		[KSPAction("Toggle Weapon")]
		public void AGToggle(KSPActionParam param)
		{
			Toggle();
		}

		[KSPField(guiActive = true, guiActiveEditor = false, guiName = "Status")]
		public string guiStatusString =
			"Disabled";

		//PartWindow buttons
		[KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Toggle")]
		public void Toggle()
		{
			if (weaponState == WeaponStates.Disabled || weaponState == WeaponStates.PoweringDown)
			{
				EnableWeapon();
			}
			else
			{
				DisableWeapon();
			}
		}

		 internal bool agHoldFiring;

		[KSPAction("Fire (Toggle)")]
		public void AGFireToggle(KSPActionParam param)
		{
			agHoldFiring = (param.type == KSPActionType.Activate);
		}

		[KSPAction("Fire (Hold)")]
		public void AGFireHold(KSPActionParam param)
		{
			StartCoroutine(FireHoldRoutine(param.group));
		}

		

		IEnumerator FireHoldRoutine(KSPActionGroup group)
		{
			KeyBinding key = Misc.Misc.AGEnumToKeybinding(group);
			if (key == null)
			{
				yield break;
			}

			while (key.GetKey())
			{
				agHoldFiring = true;
				yield return null;
			}

			agHoldFiring = false;
			yield break;
		}

		#endregion KSPActions

		#region KSP Events

		public override void OnAwake()
		{
			base.OnAwake();

			part.stagingIconAlwaysShown = true;
			this.part.stackIconGrouping = StackIconGrouping.SAME_TYPE;
		}

		public void Start()
		{
			part.stagingIconAlwaysShown = true;
			this.part.stackIconGrouping = StackIconGrouping.SAME_TYPE;

			Events["HideUI"].active = false;
			Events["ShowUI"].active = true;

			// extension for feature_engagementenvelope
			InitializeEngagementRange(0, maxEffectiveDistance);
			if (string.IsNullOrEmpty(GetShortName()))
			{
				shortName = part.partInfo.title;
			}
			OriginalShortName = shortName;
			WeaponName = shortName;
			IEnumerator<KSPParticleEmitter> emitter = part.FindModelComponents<KSPParticleEmitter>().AsEnumerable().GetEnumerator();
			while (emitter.MoveNext())
			{
				if (emitter.Current == null) continue;
				emitter.Current.emit = false;
				EffectBehaviour.AddParticleEmitter(emitter.Current);
			}
			emitter.Dispose();

			if (roundsPerMinute >= 1500)
			{
				Events["ToggleRipple"].guiActiveEditor = false;
				Fields["useRippleFire"].guiActiveEditor = false;
			}

			vessel.Velocity();	

			if (HighLogic.LoadedSceneIsFlight)
			{
				//setup transforms
				fireTransforms = part.FindModelTransforms(fireTransformName);

				//setup emitters
				IEnumerator<KSPParticleEmitter> pe = part.FindModelComponents<KSPParticleEmitter>().AsEnumerable().GetEnumerator();
				while (pe.MoveNext())
				{
					if (pe.Current == null) continue;
					pe.Current.maxSize *= part.rescaleFactor;
					pe.Current.minSize *= part.rescaleFactor;
					pe.Current.shape3D *= part.rescaleFactor;
					pe.Current.shape2D *= part.rescaleFactor;
					pe.Current.shape1D *= part.rescaleFactor;

					if (pe.Current.useWorldSpace && !oneShotWorldParticles)
					{
						BDAGaplessParticleEmitter gpe = pe.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
						gpe.part = part;
						gaplessEmitters.Add(gpe);
					}
					else
					{
						EffectBehaviour.AddParticleEmitter(pe.Current);
					}
				}
				pe.Dispose();

				//setup projectile colors
				projectileColorC = Misc.Misc.ParseColor255(projectileColor);
				startColorC = Misc.Misc.ParseColor255(startColor);

				//init and zero points
				targetPosition = Vector3.zero;
				pointingAtPosition = Vector3.zero;
				bulletPrediction = Vector3.zero;

				//setup audio
				SetupAudio();

				// Setup gauges
				gauge = (BDStagingAreaGauge)part.AddModule("BDStagingAreaGauge");
				gauge.AmmoName = ammoName;
				gauge.AudioSource = audioSource;
				gauge.ReloadAudioClip = reloadAudioClip;
				gauge.ReloadCompleteAudioClip = reloadCompleteAudioClip;
	
				AmmoID = PartResourceLibrary.Instance.GetDefinition(ammoName).id;
			}
			else if (HighLogic.LoadedSceneIsEditor)
			{
				fireTransforms = part.FindModelTransforms(fireTransformName);
				WeaponNameWindow.OnActionGroupEditorOpened.Add(OnActionGroupEditorOpened);
				WeaponNameWindow.OnActionGroupEditorClosed.Add(OnActionGroupEditorClosed);
			}
			//turret setup
			List<ModuleTurret>.Enumerator turr = part.FindModulesImplementing<ModuleTurret>().GetEnumerator();
			while (turr.MoveNext())
			{
				if (turr.Current == null) continue;
				if (turr.Current.turretID != turretID) continue;
				turret = turr.Current;
				turret.SetReferenceTransform(fireTransforms[0]);
				break;
			}
			turr.Dispose();

			if (!turret)
			{
				Fields["onlyFireInRange"].guiActive = false;
				Fields["onlyFireInRange"].guiActiveEditor = false;
			}
			//setup animation
			if (hasDeployAnim)
			{
				deployState = Misc.Misc.SetUpSingleAnimation(deployAnimName, part);
				deployState.normalizedTime = 0;
				deployState.speed = 0;
				deployState.enabled = true;
			}
			if (hasFireAnimation)
			{
				fireState = Misc.Misc.SetUpSingleAnimation(fireAnimName, part);
				fireState.enabled = false;
			}
			BDArmorySetup.OnVolumeChange += UpdateVolume;
		}

		void OnDestroy()
		{
			BDArmorySetup.OnVolumeChange -= UpdateVolume;
			WeaponNameWindow.OnActionGroupEditorOpened.Remove(OnActionGroupEditorOpened);
			WeaponNameWindow.OnActionGroupEditorClosed.Remove(OnActionGroupEditorClosed);
		}

		internal void Update()
		{
			if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && vessel.IsControllable)
			{
				if (lowpassFilter)
				{
					if (InternalCamera.Instance && InternalCamera.Instance.isActive)
					{
						lowpassFilter.enabled = true;
					}
					else
					{
						lowpassFilter.enabled = false;
					}
				}
				if (weaponState == WeaponStates.Enabled &&
					(TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
				{
					userFiring = (BDInputUtils.GetKey(BDInputSettingsFields.WEAP_FIRE_KEY) &&
								  (vessel.isActiveVessel || BDArmorySettings.REMOTE_SHOOTING) && !MapView.MapIsEnabled &&
								  !aiControlled);
					if ((userFiring || autoFire || agHoldFiring) &&
						(yawRange == 0 || (maxPitch - minPitch) == 0 ||
						 turret.TargetInRange(finalAimTarget, 10, float.MaxValue)))
					{
						if (useRippleFire && ((pointingAtSelf || isOverheated) || (aiControlled && engageRangeMax < targetDistance)))// is weapon within set max range?
						{
							StartCoroutine(IncrementRippleIndex(0));
							finalFire = false;
						}
						else
						{
							finalFire = true;
						}
					}
					else
					{
						if (spinDownAnimation) spinningDown = true;
						audioSource.Stop();
					}
				}
				else
				{
					audioSource.Stop();
					autoFire = false;
				}

				if (spinningDown && spinDownAnimation && hasFireAnimation)
				{
					if (fireState.normalizedTime > 1) fireState.normalizedTime = 0;
					fireState.speed = fireAnimSpeed;
					fireAnimSpeed = Mathf.Lerp(fireAnimSpeed, 0, 0.04f);
				}
				// Draw gauges
				if (vessel.isActiveVessel)
				{
					vessel.GetConnectedResourceTotals(AmmoID, out double ammoCurrent, out double ammoMax);
					gauge.UpdateAmmoMeter((float)(ammoCurrent / ammoMax));
					ammoLeft = "Ammo Left: " + ammoCurrent;
					if (showReloadMeter)
					{
						gauge.UpdateReloadMeter((Time.time - timeFired) * roundsPerMinute / 60);
					}
					else
					{
						gauge.UpdateHeatMeter(heat / maxHeat);
					}
				}
			}
		}

		internal void FixedUpdate()
		{
			if (HighLogic.LoadedSceneIsFlight && !vessel.packed)
			{
				if (!vessel.IsControllable)
				{
					if (weaponState != WeaponStates.PoweringDown || weaponState != WeaponStates.Disabled)
					{
						DisableWeapon();
					}
					return;
				}

				UpdateHeat();
			}
			lastFinalAimTarget = finalAimTarget;
		}

		private void UpdateMenus(bool visible)
		{
			Events["HideUI"].active = visible;
			Events["ShowUI"].active = !visible;
		}

		private void OnActionGroupEditorOpened()
		{
			Events["HideUI"].active = false;
			Events["ShowUI"].active = false;
		}

		private void OnActionGroupEditorClosed()
		{
			Events["HideUI"].active = false;
			Events["ShowUI"].active = true;
		}

		[KSPEvent(guiActiveEditor = true, guiName = "Hide Weapon Group UI", active = false)]
		public void HideUI()
		{
			WeaponGroupWindow.HideGUI();
			UpdateMenus(false);
		}

		[KSPEvent(guiActiveEditor = true, guiName = "Set Weapon Group UI", active = false)]
		public void ShowUI()
		{
			WeaponGroupWindow.ShowGUI(this);
			UpdateMenus(true);
		}

		void OnGUI()
		{
			if (weaponState == WeaponStates.Enabled && vessel && !vessel.packed && vessel.isActiveVessel &&
				BDArmorySettings.DRAW_AIMERS && !aiControlled && !MapView.MapIsEnabled && !pointingAtSelf)
			{
				float size = 30;

				Vector3 reticlePosition;
				if (BDArmorySettings.AIM_ASSIST)
				{
					if (targetAcquired && (slaved || yawRange < 1 || maxPitch - minPitch < 1))
					{
						reticlePosition = pointingAtPosition + fixedLeadOffset;

						if (!slaved)
						{
							BDGUIUtils.DrawLineBetweenWorldPositions(pointingAtPosition, reticlePosition, 2,
								new Color(0, 1, 0, 0.6f));
						}

						BDGUIUtils.DrawTextureOnWorldPos(pointingAtPosition, BDArmorySetup.Instance.greenDotTexture,
							new Vector2(6, 6), 0);

						if (atprAcquired)
						{
							BDGUIUtils.DrawTextureOnWorldPos(targetPosition, BDArmorySetup.Instance.openGreenSquare,
								new Vector2(20, 20), 0);
						}
					}
					else
					{
						reticlePosition = bulletPrediction;
					}
				}
				else
				{
					reticlePosition = pointingAtPosition;
				}

				Texture2D texture;
				if (Vector3.Angle(pointingAtPosition - transform.position, finalAimTarget - transform.position) < 1f)
				{
					texture = BDArmorySetup.Instance.greenSpikedPointCircleTexture;
				}
				else
				{
					texture = BDArmorySetup.Instance.greenPointCircleTexture;
				}
				BDGUIUtils.DrawTextureOnWorldPos(reticlePosition, texture, new Vector2(size, size), 0);

				if (BDArmorySettings.DRAW_DEBUG_LINES)
				{
					if (targetAcquired)
					{
						BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, targetPosition, 2,
							Color.blue);
					}
				}
			}

			if (HighLogic.LoadedSceneIsEditor && BDArmorySetup.showWeaponAlignment)
			{
				DrawAlignmentIndicator();
			}

#if DEBUG
			if (BDArmorySettings.DRAW_DEBUG_LINES && weaponState == WeaponStates.Enabled && vessel && !vessel.packed && !MapView.MapIsEnabled)
			{
				BDGUIUtils.MarkPosition(targetPosition, transform, Color.cyan);
				BDGUIUtils.DrawLineBetweenWorldPositions(targetPosition, targetPosition + relVelAdj, 2, Color.green);
				BDGUIUtils.DrawLineBetweenWorldPositions(targetPosition + relVelAdj, targetPosition + relVelAdj + accAdj, 2, Color.magenta);
				BDGUIUtils.DrawLineBetweenWorldPositions(targetPosition + relVelAdj + accAdj, targetPosition + relVelAdj + accAdj + gravAdj, 2, Color.yellow);
				BDGUIUtils.MarkPosition(finalAimTarget, transform, Color.cyan, size: 4);
			}
#endif
		}

		#endregion KSP Events

		#region Fire 

		internal bool CanFire()
		{
			if (ECPerShot != 0)
			{
				double chargeAvailable = part.RequestResource("ElectricCharge", ECPerShot, ResourceFlowMode.ALL_VESSEL);
				if (chargeAvailable < ECPerShot * 0.95f)
				{
					ScreenMessages.PostScreenMessage("Weapon Requires EC", 5.0f, ScreenMessageStyle.UPPER_CENTER);
					return false;
				}
			}

			if (BDArmorySettings.INFINITE_AMMO)
			{
				return true;
			}
			else if (part.RequestResource(ammoName, requestResourceAmount) > 0)
			{
				return true;
			}

			return false;
		}

		internal void DrainECPerShot()
		{
			if (ECPerShot == 0) return;
			//double drainAmount = ECPerShot * TimeWarp.fixedDeltaTime;
			double drainAmount = ECPerShot;
			double chargeAvailable = part.RequestResource("ElectricCharge", drainAmount, ResourceFlowMode.ALL_VESSEL);
		}

		#endregion

		#region Weapon Setup

		public void EnableWeapon()
		{
			if (weaponState == WeaponStates.Enabled || weaponState == WeaponStates.PoweringUp)
			{
				return;
			}

			StopShutdownStartupRoutines();

			startupRoutine = StartCoroutine(StartupRoutine());
		}

		public void DisableWeapon()
		{
			if (weaponState == WeaponStates.Disabled || weaponState == WeaponStates.PoweringDown)
			{
				return;
			}

			StopShutdownStartupRoutines();

			shutdownRoutine = StartCoroutine(ShutdownRoutine());
		}

		#endregion

		#region Audio

		void UpdateVolume()
		{
			if (audioSource)
			{
				audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
			}
			if (audioSource2)
			{
				audioSource2.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
			}
			if (lowpassFilter)
			{
				lowpassFilter.cutoffFrequency = BDArmorySettings.IVA_LOWPASS_FREQ;
			}
		}

		void SetupAudio()
		{
			fireSound = GameDatabase.Instance.GetAudioClip(fireSoundPath);
			overheatSound = GameDatabase.Instance.GetAudioClip(overheatSoundPath);
			if (!audioSource)
			{
				audioSource = gameObject.AddComponent<AudioSource>();
				audioSource.bypassListenerEffects = true;
				audioSource.minDistance = .3f;
				audioSource.maxDistance = 1000;
				audioSource.priority = 10;
				audioSource.dopplerLevel = 0;
				audioSource.spatialBlend = 1;
			}

			if (!audioSource2)
			{
				audioSource2 = gameObject.AddComponent<AudioSource>();
				audioSource2.bypassListenerEffects = true;
				audioSource2.minDistance = .3f;
				audioSource2.maxDistance = 1000;
				audioSource2.dopplerLevel = 0;
				audioSource2.priority = 10;
				audioSource2.spatialBlend = 1;
			}

			if (reloadAudioPath != string.Empty)
			{
				reloadAudioClip = (AudioClip)GameDatabase.Instance.GetAudioClip(reloadAudioPath);
			}
			if (reloadCompletePath != string.Empty)
			{
				reloadCompleteAudioClip = (AudioClip)GameDatabase.Instance.GetAudioClip(reloadCompletePath);
			}

			if (!lowpassFilter && gameObject.GetComponents<AudioLowPassFilter>().Length == 0)
			{
				lowpassFilter = gameObject.AddComponent<AudioLowPassFilter>();
				lowpassFilter.cutoffFrequency = BDArmorySettings.IVA_LOWPASS_FREQ;
				lowpassFilter.lowpassResonanceQ = 1f;
			}

			UpdateVolume();
		}

		#endregion Audio

		#region Targeting

		internal void Aim(float bulletVelocity, float thrust, float rocketMass, bool bulletDrop)
		{
			//AI control
			if (aiControlled && !slaved)
			{
				if (!targetAcquired)
				{
					autoFire = false;
					return;
				}
			}

			if (!slaved && !aiControlled && (yawRange > 0 || maxPitch - minPitch > 0))
			{
				//MouseControl
				Vector3 mouseAim = new Vector3(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height,
					0);
				Ray ray = FlightCamera.fetch.mainCamera.ViewportPointToRay(mouseAim);
				RaycastHit hit;

				if (Physics.Raycast(ray, out hit, maxTargetingRange, 9076737))
				{
					targetPosition = hit.point;

					//aim through self vessel if occluding mouseray

					KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
					Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();

					if (p && p.vessel && p.vessel == vessel)
					{
						targetPosition = ray.direction * maxTargetingRange +
										 FlightCamera.fetch.mainCamera.transform.position;
					}
				}
				else
				{
					targetPosition = (ray.direction * (maxTargetingRange + (FlightCamera.fetch.Distance * 0.75f))) +
									 FlightCamera.fetch.mainCamera.transform.position;
					if (visualTargetVessel != null && visualTargetVessel.loaded)
					{
						targetPosition = ray.direction *
										 Vector3.Distance(visualTargetVessel.transform.position,
											 FlightCamera.fetch.mainCamera.transform.position) +
										 FlightCamera.fetch.mainCamera.transform.position;
					}
				}
			}

			//aim assist
			Vector3 finalTarget = targetPosition;
			Vector3 originalTarget = targetPosition;
			Vector3 pointingDirection = fireTransforms[0].forward;
			targetDistance = Vector3.Distance(finalTarget, fireTransforms[0].position);

			if ((BDArmorySettings.AIM_ASSIST || aiControlled) && eWeaponType != WeaponTypes.Laser)
			{
				float effectiveVelocity = bulletVelocity;
				Vector3 relativeVelocity = targetVelocity - part.rb.velocity;
				Quaternion.FromToRotation(targetAccelerationPrevious, targetAcceleration).ToAngleAxis(out float accelDAngle, out Vector3 accelDAxis);
				Vector3 leadTarget = targetPosition;

				Vector3 RocketVelocity = part.rb.velocity + Krakensbane.GetFrameVelocityV3f();

				int iterations = 6;
				while (--iterations >= 0)
				{
					finalTarget = targetPosition;
					float time;
					if (eWeaponType == WeaponTypes.Ballistic)
					{
						time = (leadTarget - fireTransforms[0].position).magnitude / effectiveVelocity - (Time.fixedDeltaTime * 1.5f);
					}
					else
					{
						float a = thrust / rocketMass;
						float d = (leadTarget - fireTransforms[0].position).magnitude;
						// rocket vel is linear accel, so we dont have vel or time, so time is how long it would take to accel to target dist
						// velocity = t*a+(t*a*(1/2t-0.5)); have a(ccel), so
						//time = (-a+(sqrt a(a+8v))) / 2a
						time = ((float)Math.Sqrt(a * (a + (8 * d))) - a) / (2 * a) - (Time.fixedDeltaTime * 1.5f);
						RocketVelocity += (thrust / rocketMass) * time * pointingDirection;
					}
									   					 				  
					if (targetAcquired)
					{
						finalTarget += relativeVelocity * time;
#if DEBUG
						relVelAdj = relativeVelocity * time;
						var vc = finalTarget;
#endif
						var accelDExtAngle = accelDAngle * time / 3;
						var extrapolatedAcceleration =
							Quaternion.AngleAxis(accelDExtAngle, accelDAxis)
							* targetAcceleration
							* Mathf.Cos(accelDExtAngle * Mathf.Deg2Rad * 2.222f);
						finalTarget += 0.5f * extrapolatedAcceleration * time * time;
#if DEBUG
						accAdj = (finalTarget - vc);
#endif
					}
					else if (Misc.Misc.GetRadarAltitudeAtPos(targetPosition) < 2000)
					{
						//this vessel velocity compensation against stationary
						finalTarget += (-(part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * time);
					}

					leadTarget = finalTarget;

					if (bulletDrop || eWeaponType == WeaponTypes.Rocket)
					{
#if DEBUG
						var vc = finalTarget;
#endif
						Vector3 up = (VectorUtils.GetUpDirection(finalTarget) + 2 * VectorUtils.GetUpDirection(fireTransforms[0].position)).normalized;
						float gAccel = ((float)FlightGlobals.getGeeForceAtPosition(finalTarget).magnitude
							+ (float)FlightGlobals.getGeeForceAtPosition(fireTransforms[0].position).magnitude * 2) / 3;
						Vector3 intermediateTarget = finalTarget + (0.5f * gAccel * time * time * up);

						var avGrav = (FlightGlobals.getGeeForceAtPosition(finalTarget) + 2 * FlightGlobals.getGeeForceAtPosition(fireTransforms[0].position)) / 3;
						effectiveVelocity = bulletVelocity
							* (float)Vector3d.Dot((intermediateTarget - fireTransforms[0].position).normalized, (finalTarget - fireTransforms[0].position).normalized)
							+ Vector3.Project(avGrav, finalTarget - fireTransforms[0].position).magnitude * time / 2 * (Vector3.Dot(avGrav, finalTarget - fireTransforms[0].position) < 0 ? -1 : 1);
						if (eWeaponType == WeaponTypes.Rocket)
						{
							effectiveVelocity = RocketVelocity.magnitude
														* (float)Vector3d.Dot((intermediateTarget - fireTransforms[0].position).normalized, (finalTarget - fireTransforms[0].position).normalized)
														+ Vector3.Project(avGrav, finalTarget - fireTransforms[0].position).magnitude * time / 2 * (Vector3.Dot(avGrav, finalTarget - fireTransforms[0].position) < 0 ? -1 : 1);
						}
						finalTarget = intermediateTarget;
#if DEBUG
						gravAdj = (finalTarget - vc);
#endif
					}
				}
				targetDistance = Vector3.Distance(finalTarget, fireTransforms[0].position);
				fixedLeadOffset = originalTarget - finalTarget; //for aiming fixed guns to moving target

				  //airdetonation
				if (airDetonation)
				{
					if (targetAcquired && airDetonationTiming)
					{
						//detonationRange = BlastPhysicsUtils.CalculateBlastRange(bulletInfo.tntMass); //this returns 0, use detonationRange GUI tweakable instead
						defaultDetonationRange = targetDistance;// adds variable time fuze if/when proximity fuzes fail
					}
					else
					{
						//detonationRange = defaultDetonationRange;
						defaultDetonationRange = maxAirDetonationRange; //airburst at max range
					}
				}
			}//removed the detonationange += UnityEngine.random, that gets called every frame and just causes the prox fuze range to wander
			finalAimTarget = finalTarget;

			//final turret aiming
			if (slaved && !targetAcquired) return;
			if (turret)
			{
				bool origSmooth = turret.smoothRotation;
				if (aiControlled || slaved)
				{
					turret.smoothRotation = false;
				}
				turret.AimToTarget(finalTarget);
				turret.smoothRotation = origSmooth;
			}
		}

		internal void RunTrajectorySimulation(float bulletVelocity, float thrust, float rocketMass, float thrustTime, bool bulletDrop)
		{
			if (((BDArmorySettings.AIM_ASSIST || aiControlled) && eWeaponType == WeaponTypes.Rocket) ||
				BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS && 
				(BDArmorySettings.DRAW_DEBUG_LINES || (vessel && vessel.isActiveVessel && !aiControlled && !MapView.MapIsEnabled && !pointingAtSelf && eWeaponType != WeaponTypes.Rocket)))
			{
				Transform fireTransform = fireTransforms[0];

				if (eWeaponType == WeaponTypes.Laser &&
					BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS)
				{
					Ray ray = new Ray(fireTransform.position, fireTransform.forward);
					RaycastHit rayHit;
					if (Physics.Raycast(ray, out rayHit, maxTargetingRange, 9076737))
					{
						bulletPrediction = rayHit.point;
					}
					else
					{
						bulletPrediction = ray.GetPoint(maxTargetingRange);
					}
					pointingAtPosition = ray.GetPoint(maxTargetingRange);
				}
				else if (eWeaponType == WeaponTypes.Rocket || (eWeaponType == WeaponTypes.Ballistic && BDArmorySettings.AIM_ASSIST && BDArmorySettings.DRAW_AIMERS))
				{
					float simTime = 0;
					Vector3 pointingDirection = fireTransform.forward;
					float simDeltaTime = 0.155f;
					Vector3 simVelocity = part.rb.velocity + Krakensbane.GetFrameVelocityV3f() + (bulletVelocity * fireTransform.forward);
					Vector3 simCurrPos = fireTransform.position + ((part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime);
					Vector3 simPrevPos = simCurrPos;
					Vector3 simStartPos = simCurrPos;
					if (eWeaponType == WeaponTypes.Rocket)
					{
						simVelocity = part.rb.velocity + Krakensbane.GetFrameVelocityV3f();
						simCurrPos = fireTransform.position + ((part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime);
						simPrevPos = fireTransform.position + ((part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime);
						simStartPos = fireTransform.position + ((part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime);
					}
					bool simulating = true;

					List<Vector3> pointPositions = new List<Vector3>();
					pointPositions.Add(simCurrPos);

					float atmosMultiplier = Mathf.Clamp01(2.5f * (float)FlightGlobals.getAtmDensity(vessel.staticPressurekPa, vessel.externalTemperature, vessel.mainBody));

					while (simulating)
					{
						RaycastHit hit;

						if (eWeaponType == WeaponTypes.Rocket)
						{
							if (simTime > thrustTime)
							{
								simDeltaTime = 0.1f;
							}

							if (simTime > 0.04f)
							{
								simDeltaTime = 0.02f;
								if (simTime < thrustTime)
								{
									simVelocity += thrust / rocketMass * simDeltaTime * pointingDirection;
								}

								//rotation (aero stabilize)
								pointingDirection = Vector3.RotateTowards(pointingDirection,
									simVelocity + Krakensbane.GetFrameVelocity(),
									atmosMultiplier * (0.5f * (simTime)) * 50 * simDeltaTime * Mathf.Deg2Rad, 0);
							}
						}

						if (bulletDrop || eWeaponType == WeaponTypes.Rocket) simVelocity += FlightGlobals.getGeeForceAtPosition(simCurrPos) * simDeltaTime;
						simCurrPos += simVelocity * simDeltaTime;
						pointPositions.Add(simCurrPos);

						if (Physics.Raycast(simPrevPos, simCurrPos - simPrevPos, out hit,
							Vector3.Distance(simPrevPos, simCurrPos), 9076737))
						{
							Vessel hitVessel = null;
							try
							{
								KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
								hitVessel = (eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>()).vessel;
							}
							catch (NullReferenceException)
							{
							}

							if (hitVessel == null || (hitVessel != null && hitVessel != vessel))
							{
								bulletPrediction = hit.point;
								simulating = false;
							}
						}

						simPrevPos = simCurrPos;
						if (visualTargetVessel != null && visualTargetVessel.loaded && !visualTargetVessel.Landed &&
							(simStartPos - simCurrPos).sqrMagnitude > targetDistance * targetDistance)
						{
							bulletPrediction = simStartPos + (simCurrPos - simStartPos).normalized * targetDistance;
							simulating = false;
						}

						if ((simStartPos - simCurrPos).sqrMagnitude > maxTargetingRange * maxTargetingRange)
						{
							bulletPrediction = simStartPos + ((simCurrPos - simStartPos).normalized * maxTargetingRange);
							simulating = false;
						}
						simTime += simDeltaTime;
					}
					predictedFlightTime = simTime;

					if (BDArmorySettings.DRAW_DEBUG_LINES && BDArmorySettings.DRAW_AIMERS)
					{
						Vector3[] pointsArray = pointPositions.ToArray();
						if (gameObject.GetComponent<LineRenderer>() == null)
						{
							LineRenderer lr = gameObject.AddComponent<LineRenderer>();
							lr.startWidth = .1f;
							lr.endWidth = .1f;
							lr.positionCount = pointsArray.Length;
							for (int i = 0; i < pointsArray.Length; i++)
							{
								lr.SetPosition(i, pointsArray[i]);
							}
						}
						else
						{
							LineRenderer lr = gameObject.GetComponent<LineRenderer>();
							lr.enabled = true;
							lr.positionCount = pointsArray.Length;
							for (int i = 0; i < pointsArray.Length; i++)
							{
								lr.SetPosition(i, pointsArray[i]);
							}
						}
					}
				}
			}
		}

		internal IEnumerator AimAndFireAtEndOfFrame()
		{
			yield return new WaitForEndOfFrame();

			UpdateTargetVessel();
			updateAcceleration(targetVelocity, targetPosition);
			relativeVelocity = targetVelocity - vessel.rb_velocity;

			CheckWeaponSafety();
			CheckAIAutofire();

			if (finalFire)
			{

				if (useRippleFire && weaponManager.gunRippleIndex != rippleIndex)
				{
					//timeFired = Time.time + (initialFireDelay - (60f / roundsPerMinute)) * TimeWarp.CurrentRate;
					finalFire = false;
				}
				else
				{
					finalFire = true;
				}
				if (finalFire)
					Fire();
				finalFire = false;
			}

			yield break;
		}

		internal void CheckAIAutofire()
		{
			//autofiring with AI
			if (targetAcquired && aiControlled)
			{
				Transform fireTransform = fireTransforms[0];

				Vector3 targetRelPos = (finalAimTarget) - fireTransform.position;
				Vector3 aimDirection = fireTransform.forward;
				float targetCosAngle = Vector3.Dot(aimDirection, targetRelPos.normalized);

				Vector3 targetDiffVec = finalAimTarget - lastFinalAimTarget;
				Vector3 projectedTargetPos = targetDiffVec;
				//projectedTargetPos /= TimeWarp.fixedDeltaTime;
				//projectedTargetPos *= TimeWarp.fixedDeltaTime;
				projectedTargetPos *= 2; //project where the target will be in 2 timesteps
				projectedTargetPos += finalAimTarget;

				targetDiffVec.Normalize();
				Vector3 lastTargetRelPos = (lastFinalAimTarget) - fireTransform.position;

				if (BDATargetManager.CheckSafeToFireGuns(weaponManager, aimDirection, 1000, 0.999848f) //~1 degree of unsafe angle
					&& targetCosAngle >= maxAutoFireCosAngle) //check if directly on target
				{
					autoFire = true;
				}
				else
				{
					autoFire = false;
				}
			}
			else
			{
				autoFire = false;
			}

			//disable autofire after burst length
			if (autoFire && Time.time - autoFireTimer > autoFireLength)
			{
				autoFire = false;
				visualTargetVessel = null;
			}
		}

		public Vector3 GetLeadOffset()
		{
			return fixedLeadOffset;
		}

		internal bool WMgrAuthorized()
		{
			MissileFire manager = BDArmorySetup.Instance.ActiveWeaponManager;
			if (manager != null && manager.vessel == vessel)
			{
				if (manager.hasSingleFired) return false;
				else return true;
			}
			else
			{
				return true;
			}
		}

		internal void CheckWeaponSafety()
		{
			pointingAtSelf = false;

			// While I'm not saying vessels larger than 500m are impossible, let's be practical here
			const float maxCheckRange = 500f;
			float checkRange = Mathf.Min(targetAcquired ? targetDistance : maxTargetingRange, maxCheckRange);

			for (int i = 0; i < fireTransforms.Length; i++)
			{
				Ray ray = new Ray(fireTransforms[i].position, fireTransforms[i].forward);
				RaycastHit hit;

				if (Physics.Raycast(ray, out hit, maxTargetingRange, 9076737))
				{
					KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
					Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
					if (p && p.vessel && p.vessel == vessel)
					{
						pointingAtSelf = true;
						break;
					}
				}

				pointingAtPosition = fireTransforms[i].position + (ray.direction * targetDistance);
			}
		}

		void DrawAlignmentIndicator()
		{
			if (fireTransforms == null || fireTransforms[0] == null) return;

			Transform refTransform = EditorLogic.RootPart.GetReferenceTransform();

			if (!refTransform) return;

			Vector3 fwdPos = fireTransforms[0].position + (5 * fireTransforms[0].forward);
			BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, fwdPos, 4, Color.green);

			Vector3 referenceDirection = refTransform.up;
			Vector3 refUp = -refTransform.forward;
			Vector3 refRight = refTransform.right;

			Vector3 refFwdPos = fireTransforms[0].position + (5 * referenceDirection);
			BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position, refFwdPos, 2, Color.white);

			BDGUIUtils.DrawLineBetweenWorldPositions(fwdPos, refFwdPos, 2, XKCDColors.Orange);

			Vector2 guiPos;
			if (BDGUIUtils.WorldToGUIPos(fwdPos, out guiPos))
			{
				Rect angleRect = new Rect(guiPos.x, guiPos.y, 100, 200);

				Vector3 pitchVector = (5 * Vector3.ProjectOnPlane(fireTransforms[0].forward, refRight));
				Vector3 yawVector = (5 * Vector3.ProjectOnPlane(fireTransforms[0].forward, refUp));

				BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position + pitchVector, fwdPos, 3,
					Color.white);
				BDGUIUtils.DrawLineBetweenWorldPositions(fireTransforms[0].position + yawVector, fwdPos, 3, Color.white);

				float pitch = Vector3.Angle(pitchVector, referenceDirection);
				float yaw = Vector3.Angle(yawVector, referenceDirection);

				string convergeDistance;

				Vector3 projAxis = Vector3.Project(refTransform.position - fireTransforms[0].transform.position,
					refRight);
				float xDist = projAxis.magnitude;
				float convergeAngle = 90 - Vector3.Angle(yawVector, refTransform.up);
				if (Vector3.Dot(fireTransforms[0].forward, projAxis) > 0)
				{
					convergeDistance = "Converge: " +
									   Mathf.Round((xDist * Mathf.Tan(convergeAngle * Mathf.Deg2Rad))).ToString() + "m";
				}
				else
				{
					convergeDistance = "Diverging";
				}

				string xAngle = "X: " + Vector3.Angle(fireTransforms[0].forward, pitchVector).ToString("0.00");
				string yAngle = "Y: " + Vector3.Angle(fireTransforms[0].forward, yawVector).ToString("0.00");

				GUI.Label(angleRect, xAngle + "\n" + yAngle + "\n" + convergeDistance);
			}
		}

		#endregion Targeting

		#region Updates

		void UpdateHeat()
		{
			heat = Mathf.Clamp(heat - heatLoss * TimeWarp.fixedDeltaTime, 0, Mathf.Infinity);
			if (heat > maxHeat && !isOverheated)
			{
				isOverheated = true;
				autoFire = false;
				audioSource.Stop();
				wasFiring = false;
				audioSource2.PlayOneShot(overheatSound);
				weaponManager.ResetGuardInterval();
			}
			if (heat < maxHeat / 3 && isOverheated) //reset on cooldown
			{
				isOverheated = false;
			}
		}

		internal void UpdateTargetVessel()
		{
			targetAcquired = false;
			slaved = false;
			bool atprWasAcquired = atprAcquired;
			atprAcquired = false;

			if (weaponManager)
			{
				//legacy or visual range guard targeting
				if (aiControlled && weaponManager && visualTargetVessel &&
					(visualTargetVessel.transform.position - transform.position).sqrMagnitude < weaponManager.guardRange * weaponManager.guardRange)
				{
					targetPosition = visualTargetVessel.CoM;
					targetVelocity = visualTargetVessel.rb_velocity;
					targetAcquired = true;
					return;
				}

				if (weaponManager.slavingTurrets && turret)
				{
					slaved = true;
					targetPosition = weaponManager.slavedPosition;
					targetVelocity = weaponManager.slavedTarget.vessel?.rb_velocity ?? (weaponManager.slavedVelocity - Krakensbane.GetFrameVelocityV3f());
					targetAcquired = true;
					return;
				}

				if (weaponManager.vesselRadarData && weaponManager.vesselRadarData.locked)
				{
					TargetSignatureData targetData = weaponManager.vesselRadarData.lockedTargetData.targetData;
					targetVelocity = targetData.velocity - Krakensbane.GetFrameVelocityV3f();
					targetPosition = targetData.predictedPosition;
					targetAcceleration = targetData.acceleration;
					if (targetData.vessel)
					{
						targetVelocity = targetData.vessel?.rb_velocity ?? targetVelocity;
						targetPosition = targetData.vessel.CoM;
					}
					targetAcquired = true;
					return;
				}

				//auto proxy tracking
				if (vessel.isActiveVessel && autoProxyTrackRange > 0)
				{
					if (aptrTicker < 20)
					{
						aptrTicker++;

						if (atprWasAcquired)
						{
							targetAcquired = true;
							atprAcquired = true;
						}
					}
					else
					{
						aptrTicker = 0;
						Vessel tgt = null;
						float closestSqrDist = autoProxyTrackRange * autoProxyTrackRange;
						List<Vessel>.Enumerator v = BDATargetManager.LoadedVessels.GetEnumerator();
						while (v.MoveNext())
						{
							if (v.Current == null || !v.Current.loaded) continue;
							if (!v.Current.IsControllable) continue;
							if (v.Current == vessel) continue;
							Vector3 targetVector = v.Current.transform.position - part.transform.position;
							if (Vector3.Dot(targetVector, fireTransforms[0].forward) < 0) continue;
							float sqrDist = (v.Current.transform.position - part.transform.position).sqrMagnitude;
							if (sqrDist > closestSqrDist) continue;
							if (Vector3.Angle(targetVector, fireTransforms[0].forward) > 20) continue;
							tgt = v.Current;
							closestSqrDist = sqrDist;
						}
						v.Dispose();

						if (tgt == null) return;
						targetAcquired = true;
						atprAcquired = true;
						targetPosition = tgt.CoM;
						targetVelocity = tgt.rb_velocity;
					}
				}
			}
		}

		/// <summary>
		/// Update target acceleration based on previous velocity.
		/// Position is used to clamp acceleration for splashed targets, as ksp produces excessive bobbing.
		/// </summary>
		internal void updateAcceleration(Vector3 target_rb_velocity, Vector3 position)
		{
			targetAccelerationPrevious = targetAcceleration;
			targetAcceleration = (target_rb_velocity - Krakensbane.GetLastCorrection() - targetVelocityPrevious) / Time.fixedDeltaTime;
			float altitude = (float)FlightGlobals.currentMainBody.GetAltitude(position);
			if (altitude < 12 && altitude > -10)
				targetAcceleration = Vector3.ProjectOnPlane(targetAcceleration, VectorUtils.GetUpDirection(position));
			targetVelocityPrevious = target_rb_velocity;
		}

		void UpdateGUIWeaponState()
		{
			guiStatusString = weaponState.ToString();
		}

		IEnumerator StartupRoutine()
		{
			weaponState = WeaponStates.PoweringUp;
			UpdateGUIWeaponState();

			if (hasDeployAnim && deployState)
			{
				deployState.enabled = true;
				deployState.speed = 1;
				while (deployState.normalizedTime < 1) //wait for animation here
				{
					yield return null;
				}
				deployState.normalizedTime = 1;
				deployState.speed = 0;
				deployState.enabled = false;
			}

			weaponState = WeaponStates.Enabled;
			UpdateGUIWeaponState();
			BDArmorySetup.Instance.UpdateCursorState();
		}

		IEnumerator ShutdownRoutine()
		{
			weaponState = WeaponStates.PoweringDown;
			UpdateGUIWeaponState();
			BDArmorySetup.Instance.UpdateCursorState();
			if (turret)
			{
				yield return new WaitForSeconds(0.2f);

				while (!turret.ReturnTurret()) //wait till turret has returned
				{
					yield return new WaitForFixedUpdate();
				}
			}

			if (hasDeployAnim)
			{
				deployState.enabled = true;
				deployState.speed = -1;
				while (deployState.normalizedTime > 0)
				{
					yield return null;
				}
				deployState.normalizedTime = 0;
				deployState.speed = 0;
				deployState.enabled = false;
			}

			weaponState = WeaponStates.Disabled;
			UpdateGUIWeaponState();
		}

		void StopShutdownStartupRoutines()
		{
			if (shutdownRoutine != null)
			{
				StopCoroutine(shutdownRoutine);
				shutdownRoutine = null;
			}

			if (startupRoutine != null)
			{
				StopCoroutine(startupRoutine);
				startupRoutine = null;
			}
		}

		#endregion Updates

		#region Bullets

		protected void SetInitialDetonationDistance(float tntMass, float blastForce, float detonationRange)
		{
			if (detonationRange == -1)
			{
				if (proximityDetonation || airDetonation)
				{
					if (tntMass != 0)
					{
						detonationRange = (BlastPhysicsUtils.CalculateBlastRange(tntMass) * 0.66f);
					}
					else if (blastForce != 0)
					{
						detonationRange = (BlastPhysicsUtils.CalculateBlastRange(blastForce) * 0.66f);
						//should really update rockets to use tntmass instead.
					}
					else
					{
						detonationRange = 0f;
						proximityDetonation = false;
					}
				}
			}
			if (BDArmorySettings.DRAW_DEBUG_LABELS)
			{
				Debug.Log("[BDArmory]: DetonationDistance = : " + detonationRange);
			}
		}
		#endregion Bullets

	}

	#region UI //borrowing code from ModularMissile GUI

	[KSPAddon(KSPAddon.Startup.EditorAny, false)]
	public class WeaponGroupWindow : MonoBehaviour
	{
		internal static EventVoid OnActionGroupEditorOpened = new EventVoid("OnActionGroupEditorOpened");
		internal static EventVoid OnActionGroupEditorClosed = new EventVoid("OnActionGroupEditorClosed");

		private static GUIStyle unchanged;
		private static GUIStyle changed;
		private static GUIStyle greyed;
		private static GUIStyle overfull;

		private static WeaponGroupWindow instance;
		private static Vector3 mousePos = Vector3.zero;

		private bool ActionGroupMode;

		private Rect guiWindowRect = new Rect(0, 0, 0, 0);

		private WeaponBase WPNmodule;

		[KSPField] public int offsetGUIPos = -1;

		private Vector2 scrollPos;

		[KSPField(isPersistant = false, guiActiveEditor = true, guiActive = false, guiName = "Show Group Editor"), UI_Toggle(enabledText = "close Group GUI", disabledText = "open Group GUI")] [NonSerialized] public bool showRFGUI;

		private bool styleSetup;

		private string txtName = string.Empty;

		public static void HideGUI()
		{
			if (instance != null && instance.WPNmodule != null)
			{
				instance.WPNmodule.WeaponName = instance.WPNmodule.shortName;
				instance.WPNmodule = null;
				instance.UpdateGUIState();
			}
			EditorLogic editor = EditorLogic.fetch;
			if (editor != null)
				editor.Unlock("BD_MN_GUILock");
		}

		public static void ShowGUI(WeaponBase WPNmodule)
		{
			if (instance != null)
			{
				instance.WPNmodule = WPNmodule;
				instance.UpdateGUIState();
			}
		}

		private void UpdateGUIState()
		{
			enabled = WPNmodule != null;
			EditorLogic editor = EditorLogic.fetch;
			if (!enabled && editor != null)
				editor.Unlock("BD_MN_GUILock");
		}

		private IEnumerator<YieldInstruction> CheckActionGroupEditor()
		{
			while (EditorLogic.fetch == null)
			{
				yield return null;
			}
			EditorLogic editor = EditorLogic.fetch;
			while (EditorLogic.fetch != null)
			{
				if (editor.editorScreen == EditorScreen.Actions)
				{
					if (!ActionGroupMode)
					{
						HideGUI();
						OnActionGroupEditorOpened.Fire();
					}
					EditorActionGroups age = EditorActionGroups.Instance;
					if (WPNmodule && !age.GetSelectedParts().Contains(WPNmodule.part))
					{
						HideGUI();
					}
					ActionGroupMode = true;
				}
				else
				{
					if (ActionGroupMode)
					{
						HideGUI();
						OnActionGroupEditorClosed.Fire();
					}
					ActionGroupMode = false;
				}
				yield return null;
			}
		}

		private void Awake()
		{
			enabled = false;
			instance = this;
		}

		private void OnDestroy()
		{
			instance = null;
		}

		public void OnGUI()
		{
			if (!styleSetup)
			{
				styleSetup = true;
				Styles.InitStyles();
			}

			EditorLogic editor = EditorLogic.fetch;
			if (!HighLogic.LoadedSceneIsEditor || !editor)
			{
				return;
			}
			bool cursorInGUI = false; // nicked the locking code from Ferram
			mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
			mousePos.y = Screen.height - mousePos.y;

			int posMult = 0;
			if (offsetGUIPos != -1)
			{
				posMult = offsetGUIPos;
			}
			if (ActionGroupMode)
			{
				if (guiWindowRect.width == 0)
				{
					guiWindowRect = new Rect(430 * posMult, 365, 438, 50);
				}
				new Rect(guiWindowRect.xMin + 440, mousePos.y - 5, 300, 20);
			}
			else
			{
				if (guiWindowRect.width == 0)
				{
					//guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, (Screen.height - 365));
					guiWindowRect = new Rect(Screen.width - 8 - 430 * (posMult + 1), 365, 438, 50);
				}
				new Rect(guiWindowRect.xMin - (230 - 8), mousePos.y - 5, 220, 20);
			}
			cursorInGUI = guiWindowRect.Contains(mousePos);
			if (cursorInGUI)
			{
				editor.Lock(false, false, false, "BD_MN_GUILock");
				//if (EditorTooltip.Instance != null)
				//    EditorTooltip.Instance.HideToolTip();
			}
			else
			{
				editor.Unlock("BD_MN_GUILock");
			}
			guiWindowRect = GUILayout.Window(GetInstanceID(), guiWindowRect, GUIWindow, "Weapon Group GUI", Styles.styleEditorPanel);
		}

		public void GUIWindow(int windowID)
		{
			InitializeStyles();

			GUILayout.BeginVertical();
			GUILayout.Space(20);

			GUILayout.BeginHorizontal();

			GUILayout.Label("Add to Weapon Group: ");

			txtName = GUILayout.TextField(txtName);

			if (GUILayout.Button("Save & Close"))
			{
				string newName = string.IsNullOrEmpty(txtName.Trim()) ? WPNmodule.OriginalShortName : txtName.Trim();

				WPNmodule.WeaponName = newName;
				WPNmodule.shortName = newName;
				instance.WPNmodule.HideUI();
			}

			GUILayout.EndHorizontal();

			scrollPos = GUILayout.BeginScrollView(scrollPos);

			GUILayout.EndScrollView();

			GUILayout.EndVertical();

			GUI.DragWindow();
			BDGUIUtils.RepositionWindow(ref guiWindowRect);
		}

		private static void InitializeStyles()
		{
			if (unchanged == null)
			{
				if (GUI.skin == null)
				{
					unchanged = new GUIStyle();
					changed = new GUIStyle();
					greyed = new GUIStyle();
					overfull = new GUIStyle();
				}
				else
				{
					unchanged = new GUIStyle(GUI.skin.textField);
					changed = new GUIStyle(GUI.skin.textField);
					greyed = new GUIStyle(GUI.skin.textField);
					overfull = new GUIStyle(GUI.skin.label);
				}

				unchanged.normal.textColor = Color.white;
				unchanged.active.textColor = Color.white;
				unchanged.focused.textColor = Color.white;
				unchanged.hover.textColor = Color.white;

				changed.normal.textColor = Color.yellow;
				changed.active.textColor = Color.yellow;
				changed.focused.textColor = Color.yellow;
				changed.hover.textColor = Color.yellow;

				greyed.normal.textColor = Color.gray;

				overfull.normal.textColor = Color.red;
			}
		}
	}

	#endregion UI //borrowing code from ModularMissile GUI
}
