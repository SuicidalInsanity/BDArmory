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
	class ModuleWeapon : WeaponBase
	{
		public static ObjectPool bulletPool;
		public static ObjectPool shellPool;

		public float bulletBallisticCoefficient;

		//muzzleflash emitters
		List<KSPParticleEmitter> muzzleFlashEmitters;

		[KSPField]
		public string shellEjectTransformName = "shellEject";
		public Transform[] shellEjectTransforms;

		[KSPField] public float bulletMass = 0.3880f; //mass in KG - used for damage and recoil and drag

		[KSPField] public float caliber = 30; //caliber in mm, used for penetration calcs

		[KSPField] public float bulletDmgMult = 1; //Used for heat damage modifier for non-explosive bullets

		[KSPField] public float bulletVelocity = 1030; //velocity in meters/second

		[KSPField] public string bulletDragTypeName = "AnalyticEstimate";
		public BulletDragTypes bulletDragType;

		//drag area of the bullet in m^2; equal to Cd * A with A being the frontal area of the bullet; as a first approximation, take Cd to be 0.3
		//bullet mass / bullet drag area.  Used in analytic estimate to speed up code
		[KSPField] public float bulletDragArea = 1.209675e-5f;

		private BulletInfo bulletInfo;

		[KSPField] public string bulletType = "def";

		[KSPField]
		public bool hasRecoil = true;

		[KSPField]
		public float recoilReduction = 1; //for reducing recoil on large guns with built in compensation

		[KSPField]
		public bool bulletDrop = true; //projectiles are affected by gravity

		[KSPField]
		public float cannonShellRadius = 30; //max radius of explosion forces/damage
		[KSPField]
		public float cannonShellPower = 8; //explosion's impulse force
		[KSPField]
		public float cannonShellHeat = -1; //if non-negative, heat damage

		[KSPField]
		public float tracerLength = 0;
		//if set to zero, tracer will be the length of the distance covered by the projectile in one physics timestep

		[KSPField]
		public float tracerDeltaFactor = 2.65f;

		[KSPField]
		public float nonTracerWidth = 0.01f;

		[KSPField]
		public int tracerInterval = 0;

		[KSPField]
		public float tracerLuminance = 1.75f;
		int tracerIntervalCounter;

		[KSPField]
		public string bulletTexturePath = "BDArmory/Textures/bullet";

		public new  void Start()
		{
			base.Start();

			eWeaponType = WeaponTypes.Ballistic;

			Fields["roundsPerMinute"].guiActiveEditor = false;

			if (airDetonation)
			{
				UI_FloatRange detRange = (UI_FloatRange)Fields["maxAirDetonationRange"].uiControlEditor;
				detRange.maxValue = maxEffectiveDistance; //altitude fuzing clamped to max range
			}
			else //disable fuze GUI elements on un-fuzed munitions
			{
				Fields["maxAirDetonationRange"].guiActive = false;
				Fields["maxAirDetonationRange"].guiActiveEditor = false;
				Fields["defaultDetonationRange"].guiActive = false;
				Fields["defaultDetonationRange"].guiActiveEditor = false;
				Fields["detonationRange"].guiActive = false;
				Fields["detonationRange"].guiActiveEditor = false;
			}
			muzzleFlashEmitters = new List<KSPParticleEmitter>();
			IEnumerator<Transform> mtf = part.FindModelTransforms("muzzleTransform").AsEnumerable().GetEnumerator();
			while (mtf.MoveNext())
			{
				if (mtf.Current == null) continue;
				KSPParticleEmitter kpe = mtf.Current.GetComponent<KSPParticleEmitter>();
				EffectBehaviour.AddParticleEmitter(kpe);
				muzzleFlashEmitters.Add(kpe);
				kpe.emit = false;
			}
			mtf.Dispose();
			if (HighLogic.LoadedSceneIsFlight)
			{
				if (bulletPool == null)
				{
					SetupBulletPool();
				}
				if (shellPool == null)
				{
					SetupShellPool();
				}
				shellEjectTransforms = part.FindModelTransforms(shellEjectTransformName);
			}
			if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
			{
				Events["Jettison"].guiActive = false;
			}
			SetupBullet();
			if (bulletInfo == null)
			{
				if (BDArmorySettings.DRAW_DEBUG_LABELS)
					Debug.Log("[BDArmory]: Failed To load bullet : " + bulletType);
			}
			else
			{
				if (BDArmorySettings.DRAW_DEBUG_LABELS)
					Debug.Log("[BDArmory]: BulletType Loaded : " + bulletType);
			}
			SetInitialDetonationDistance(bulletInfo.tntMass, 0, detonationRange);
		}
		
		new void FixedUpdate()
		{
			base.FixedUpdate();
			if (HighLogic.LoadedSceneIsFlight && !vessel.packed)
			{
				if (weaponState == WeaponStates.Enabled &&
					(TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
				{
					StartCoroutine(AimAndFireAtEndOfFrame());
					RunTrajectorySimulation(bulletVelocity, 0, 0, 0, bulletDrop);
					Aim(bulletVelocity, 0, 0, bulletDrop);
				}
			}
		}

		void Fire()
		{
			if (BDArmorySetup.GameIsPaused)
			{
				if (audioSource.isPlaying)
				{
					audioSource.Stop();
				}
				return;
			}

			float timeGap = (60 / roundsPerMinute) * TimeWarp.CurrentRate;
			if (Time.time - timeFired > timeGap
				&& !isOverheated
				&& !pointingAtSelf
				&& (aiControlled || !Misc.Misc.CheckMouseIsOnGui())
				&& WMgrAuthorized())
			{
				bool effectsShot = false;
				//Transform[] fireTransforms = part.FindModelTransforms("fireTransform");
				for (float iTime = Mathf.Min(Time.time - timeFired - timeGap, TimeWarp.fixedDeltaTime); iTime >= 0; iTime -= timeGap)
					for (int i = 0; i < fireTransforms.Length; i++)
					{
						//if ((BDArmorySettings.INFINITE_AMMO || part.RequestResource(ammoName, requestResourceAmount) > 0))
						if (CanFire())
						{
							Transform fireTransform = fireTransforms[i];
							spinningDown = false;

							//recoil
							if (hasRecoil)
							{
								part.rb.AddForceAtPosition((-fireTransform.forward) * (bulletVelocity * bulletMass / 1000 * BDArmorySettings.RECOIL_FACTOR * recoilReduction),
									fireTransform.position, ForceMode.Impulse);
							}

							if (!effectsShot)
							{
								//sound
								if (oneShotSound)
								{
									audioSource.Stop();
									audioSource.PlayOneShot(fireSound);
								}
								else
								{
									wasFiring = true;
									if (!audioSource.isPlaying)
									{
										audioSource.clip = fireSound;
										audioSource.loop = false;
										audioSource.time = 0;
										audioSource.Play();
									}
									else
									{
										if (audioSource.time >= fireSound.length)
										{
											audioSource.time = soundRepeatTime;
										}
									}
								}

								//animation
								if (hasFireAnimation)
								{
									float unclampedSpeed = (roundsPerMinute * fireState.length) / 60f;
									float lowFramerateFix = 1;
									if (roundsPerMinute > 500f)
									{
										lowFramerateFix = (0.02f / Time.deltaTime);
									}
									fireAnimSpeed = Mathf.Clamp(unclampedSpeed, 1f * lowFramerateFix, 20f * lowFramerateFix);
									fireState.enabled = true;
									if (unclampedSpeed == fireAnimSpeed || fireState.normalizedTime > 1)
									{
										fireState.normalizedTime = 0;
									}
									fireState.speed = fireAnimSpeed;
									fireState.normalizedTime = Mathf.Repeat(fireState.normalizedTime, 1);

									//Debug.Log("fireAnim time: " + fireState.normalizedTime + ", speed; " + fireState.speed);
								}

								//muzzle flash
								List<KSPParticleEmitter>.Enumerator pEmitter = muzzleFlashEmitters.GetEnumerator();
								while (pEmitter.MoveNext())
								{
									if (pEmitter.Current == null) continue;
									//KSPParticleEmitter pEmitter = mtf.gameObject.GetComponent<KSPParticleEmitter>();
									if (pEmitter.Current.useWorldSpace && !oneShotWorldParticles) continue;
									if (pEmitter.Current.maxEnergy < 0.5f)
									{
										float twoFrameTime = Mathf.Clamp(Time.deltaTime * 2f, 0.02f, 0.499f);
										pEmitter.Current.maxEnergy = twoFrameTime;
										pEmitter.Current.minEnergy = twoFrameTime / 3f;
									}
									pEmitter.Current.Emit();
								}
								pEmitter.Dispose();

								List<BDAGaplessParticleEmitter>.Enumerator gpe = gaplessEmitters.GetEnumerator();
								while (gpe.MoveNext())
								{
									if (gpe.Current == null) continue;
									gpe.Current.EmitParticles();
								}
								gpe.Dispose();

								//shell ejection
								if (BDArmorySettings.EJECT_SHELLS)
								{
									IEnumerator<Transform> sTf = shellEjectTransforms.AsEnumerable().GetEnumerator();
									while (sTf.MoveNext())
									{
										if (sTf.Current == null) continue;
										GameObject ejectedShell = shellPool.GetPooledObject();
										ejectedShell.transform.position = sTf.Current.position;
										//+(part.rb.velocity*TimeWarp.fixedDeltaTime);
										ejectedShell.transform.rotation = sTf.Current.rotation;
										ejectedShell.transform.localScale = Vector3.one * shellScale;
										ShellCasing shellComponent = ejectedShell.GetComponent<ShellCasing>();
										shellComponent.initialV = part.rb.velocity;
										ejectedShell.SetActive(true);
									}
									sTf.Dispose();
								}
								effectsShot = true;
							}

							//firing bullet
							GameObject firedBullet = bulletPool.GetPooledObject();
							PooledBullet pBullet = firedBullet.GetComponent<PooledBullet>();

							firedBullet.transform.position = fireTransform.position;

							pBullet.caliber = bulletInfo.caliber;
							pBullet.bulletVelocity = bulletInfo.bulletVelocity;
							pBullet.bulletMass = bulletInfo.bulletMass;
							pBullet.explosive = bulletInfo.explosive;
							pBullet.apBulletMod = bulletInfo.apBulletMod;
							pBullet.bulletDmgMult = bulletDmgMult;

							//A = π x (Ø / 2)^2
							bulletDragArea = Mathf.PI * Mathf.Pow(caliber / 2f, 2f);

							//Bc = m/Cd * A
							bulletBallisticCoefficient = bulletMass / ((bulletDragArea / 1000000f) * 0.295f); // mm^2 to m^2

							//Bc = m/d^2 * i where i = 0.484
							//bulletBallisticCoefficient = bulletMass / Mathf.Pow(caliber / 1000, 2f) * 0.484f;

							pBullet.ballisticCoefficient = bulletBallisticCoefficient;

							pBullet.flightTimeElapsed = iTime;
							// measure bullet lifetime in time rather than in distance, because distances get very relative in orbit
							pBullet.timeToLiveUntil = Mathf.Max(maxTargetingRange, maxEffectiveDistance) / bulletVelocity * 1.1f + Time.time;

							timeFired = Time.time - iTime;

							Vector3 firedVelocity =
								VectorUtils.GaussianDirectionDeviation(fireTransform.forward, maxDeviation / 4) * bulletVelocity;

							pBullet.currentVelocity = (part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) + firedVelocity; // use the real velocity, w/o offloading
							firedBullet.transform.position += (part.rb.velocity + Krakensbane.GetFrameVelocityV3f()) * Time.fixedDeltaTime
																+ pBullet.currentVelocity * iTime;

							pBullet.sourceVessel = vessel;
							pBullet.bulletTexturePath = bulletTexturePath;
							pBullet.projectileColor = projectileColorC;
							pBullet.startColor = startColorC;
							pBullet.fadeColor = fadeColor;
							tracerIntervalCounter++;
							if (tracerIntervalCounter > tracerInterval)
							{
								tracerIntervalCounter = 0;
								pBullet.tracerStartWidth = tracerStartWidth;
								pBullet.tracerEndWidth = tracerEndWidth;
								pBullet.tracerLength = tracerLength;
							}
							else
							{
								pBullet.tracerStartWidth = nonTracerWidth;
								pBullet.tracerEndWidth = nonTracerWidth;
								pBullet.startColor.a *= 0.25f;
								pBullet.projectileColor.a *= 0.25f;
								pBullet.tracerLength = tracerLength * 0.4f;
							}
							pBullet.tracerLuminance = tracerLuminance;
							pBullet.tracerDeltaFactor = tracerDeltaFactor;
							pBullet.bulletDrop = bulletDrop;

							if (eWeaponType == WeaponTypes.Ballistic && bulletInfo.explosive) //WeaponTypes.Cannon is deprecated
							{
								if (bulletType == "def")
								{
									//legacy model, per weapon config
									pBullet.bulletType = PooledBullet.PooledBulletTypes.Explosive;
									pBullet.explModelPath = explModelPath;
									pBullet.explSoundPath = explSoundPath;
									pBullet.blastPower = cannonShellPower;
									pBullet.blastHeat = cannonShellHeat;
									pBullet.radius = cannonShellRadius;
									pBullet.airDetonation = airDetonation;
									pBullet.detonationRange = detonationRange;
									pBullet.maxAirDetonationRange = maxAirDetonationRange;
									pBullet.defaultDetonationRange = defaultDetonationRange;
									pBullet.proximityDetonation = proximityDetonation;
								}
								else
								{
									//use values from bullets.cfg
									pBullet.bulletType = PooledBullet.PooledBulletTypes.Explosive;
									pBullet.explModelPath = explModelPath;
									pBullet.explSoundPath = explSoundPath;

									pBullet.tntMass = bulletInfo.tntMass;
									pBullet.blastPower = bulletInfo.blastPower;
									pBullet.blastHeat = bulletInfo.blastHeat;
									pBullet.radius = bulletInfo.blastRadius;

									pBullet.airDetonation = airDetonation;
									pBullet.detonationRange = detonationRange;
									pBullet.maxAirDetonationRange = maxAirDetonationRange;
									pBullet.defaultDetonationRange = defaultDetonationRange;
									pBullet.proximityDetonation = proximityDetonation;
								}
							}
							else
							{
								pBullet.bulletType = PooledBullet.PooledBulletTypes.Standard;
								pBullet.airDetonation = false;
							}
							switch (bulletDragType)
							{
								case BulletDragTypes.None:
									pBullet.dragType = PooledBullet.BulletDragTypes.None;
									break;

								case BulletDragTypes.AnalyticEstimate:
									pBullet.dragType = PooledBullet.BulletDragTypes.AnalyticEstimate;
									break;

								case BulletDragTypes.NumericalIntegration:
									pBullet.dragType = PooledBullet.BulletDragTypes.NumericalIntegration;
									break;
							}

							pBullet.bullet = BulletInfo.bullets[bulletType];
							pBullet.gameObject.SetActive(true);

							//heat
							heat += heatPerShot;
							//EC
							DrainECPerShot();
						}
						else
						{
							spinningDown = true;
							if (!oneShotSound && wasFiring)
							{
								audioSource.Stop();
								wasFiring = false;
								audioSource2.PlayOneShot(overheatSound);
							}
						}
					}

				if (useRippleFire)
				{
					StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
				}
			}
			else
			{
				spinningDown = true;
			}
		}
		void ParseBulletDragType()
		{
			bulletDragTypeName = bulletDragTypeName.ToLower();

			switch (bulletDragTypeName)
			{
				case "none":
					bulletDragType = BulletDragTypes.None;
					break;

				case "numericalintegration":
					bulletDragType = BulletDragTypes.NumericalIntegration;
					break;

				case "analyticestimate":
					bulletDragType = BulletDragTypes.AnalyticEstimate;
					break;
			}
		}

		void SetupBulletPool()
		{
			GameObject templateBullet = new GameObject("Bullet");
			templateBullet.AddComponent<PooledBullet>();
			templateBullet.SetActive(false);
			bulletPool = ObjectPool.CreateObjectPool(templateBullet, 100, true, true);
		}

		void SetupShellPool()
		{
			GameObject templateShell =
				(GameObject)Instantiate(GameDatabase.Instance.GetModel("BDArmory/Models/shell/model"));
			templateShell.SetActive(false);
			templateShell.AddComponent<ShellCasing>();
			shellPool = ObjectPool.CreateObjectPool(templateShell, 50, true, true);
		}

		void SetupBullet()
		{
			bulletInfo = BulletInfo.bullets[bulletType];
			if (bulletType != "def")
			{
				//use values from bullets.cfg if not the Part Module defaults are used
				caliber = bulletInfo.caliber;
				bulletVelocity = bulletInfo.bulletVelocity;
				bulletMass = bulletInfo.bulletMass;
				bulletDragTypeName = bulletInfo.bulletDragTypeName;
				cannonShellHeat = bulletInfo.blastHeat;
				cannonShellPower = bulletInfo.blastPower;
				cannonShellRadius = bulletInfo.blastRadius;
			}
			ParseBulletDragType();
		}
		public override string GetInfo()
		{
			BulletInfo binfo = BulletInfo.bullets[bulletType];
			StringBuilder output = new StringBuilder();
			output.Append(Environment.NewLine);
			output.AppendLine($"Weapon Type: {weaponType}");

			output.AppendLine($"Rounds Per Minute: {roundsPerMinute * (fireTransforms?.Length ?? 1)}");
			output.AppendLine($"Ammunition: {ammoName}");
	
			output.AppendLine($"Bullet type: {bulletType}");
			output.AppendLine($"Bullet mass: {Math.Round(binfo.bulletMass, 2)} kg");
			output.AppendLine($"Muzzle velocity: {Math.Round(binfo.bulletVelocity, 2)} m/s");
				
			output.AppendLine($"Max Range: {maxEffectiveDistance} m");

			output.AppendLine($"Explosive: {binfo.explosive}");
			if (binfo.explosive)
			{
				output.AppendLine($"Blast:");
				output.AppendLine($"- tnt mass:  {Math.Round((binfo.tntMass > 0 ? binfo.tntMass : binfo.blastPower), 2)} kg");
				output.AppendLine($"- radius:  {Math.Round(BlastPhysicsUtils.CalculateBlastRange(binfo.tntMass), 2)} m");
				output.AppendLine($"Air detonation: {airDetonation}");
				if (airDetonation)
				{
					output.AppendLine($"- auto timing: {airDetonationTiming}");
					output.AppendLine($"- max range: {maxAirDetonationRange} m");
				}
			}		
			
			return output.ToString();
		}
	}
}
