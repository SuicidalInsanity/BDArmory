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
	class Laser : WeaponBase
	{
		LineRenderer[] laserRenderers;
		Vector3 laserPoint;

		[KSPField] public float laserDamage = 10000; //base damage/second of lasers

		[KSPField]
		public string laserTexturePath = "BDArmory/Textures/laser";

		//Used for scaling laser damage down based on distance.
		[KSPField]
		public float tanAngle = 0.0001f;
		//Angle of divergeance/2. Theoretical minimum value calculated using θ = (1.22 L/RL)/2,
		//where L is laser's wavelength and RL is the radius of the mirror (=gun).

		new void Start()
		{
			base.Start();

			eWeaponType = WeaponTypes.Laser;

			Fields["roundsPerMinute"].guiActiveEditor = false;

			if (HighLogic.LoadedSceneIsFlight)
			{
				SetupLaserSpecifics();
			}
			if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
			{
				Events["Jettison"].guiActive = false;
			}
		}
		new void Update()
		{
			base.Update();
			if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && !vessel.packed && vessel.IsControllable)
			{
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
			}
		}

		new void FixedUpdate()
		{
			base.FixedUpdate();
			if (HighLogic.LoadedSceneIsFlight && !vessel.packed)
			{
				if (weaponState == WeaponStates.Enabled &&
					(TimeWarp.WarpMode != TimeWarp.Modes.HIGH || TimeWarp.CurrentRate == 1))
				{
					RunTrajectorySimulation(0, 0, 0, 0, false);
					Aim(0, 0, 0, false);
					if ((userFiring || autoFire || agHoldFiring) &&
							(!turret || turret.TargetInRange(targetPosition, 10, float.MaxValue)))
					{
						if (FireLaser())
						{
							for (int i = 0; i < laserRenderers.Length; i++)
							{
								laserRenderers[i].enabled = true;
							}
						}
					}
					else
					{
						for (int i = 0; i < laserRenderers.Length; i++)
						{
							laserRenderers[i].enabled = false;
						}
						audioSource.Stop();
					}			
				}
				else 
				{
					for (int i = 0; i < laserRenderers.Length; i++)
					{
						laserRenderers[i].enabled = false;
					}
					audioSource.Stop();
				}
			}
			lastFinalAimTarget = finalAimTarget;
		}

		private bool FireLaser()
		{
			float chargeAmount = requestResourceAmount * TimeWarp.fixedDeltaTime;

			if (!pointingAtSelf && !Misc.Misc.CheckMouseIsOnGui() && WMgrAuthorized() && !isOverheated &&
				(part.RequestResource(ammoName, chargeAmount) >= chargeAmount || BDArmorySettings.INFINITE_AMMO))
			{
				if (!audioSource.isPlaying)
				{
					audioSource.PlayOneShot(chargeSound);
					audioSource.Play();
					audioSource.loop = true;
				}
				for (int i = 0; i < fireTransforms.Length; i++)
				{
					Transform tf = fireTransforms[i];

					LineRenderer lr = laserRenderers[i];

					Vector3 rayDirection = tf.forward;

					Vector3 targetDirection = Vector3.zero; //autoTrack enhancer
					Vector3 targetDirectionLR = tf.forward;

					if (((visualTargetVessel != null && visualTargetVessel.loaded) || slaved)
						&& Vector3.Angle(rayDirection, targetDirection) < 1)
					{
						targetDirection = targetPosition + relativeVelocity * Time.fixedDeltaTime * 2 - tf.position;
						rayDirection = targetDirection;
						targetDirectionLR = targetDirection;
					}

					Ray ray = new Ray(tf.position, rayDirection);
					lr.useWorldSpace = false;
					lr.SetPosition(0, Vector3.zero);
					RaycastHit hit;

					if (Physics.Raycast(ray, out hit, maxTargetingRange, 9076737))
					{
						lr.useWorldSpace = true;
						laserPoint = hit.point + targetVelocity * Time.fixedDeltaTime;

						lr.SetPosition(0, tf.position + (part.rb.velocity * Time.fixedDeltaTime));
						lr.SetPosition(1, laserPoint);

						KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
						Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();

						if (p && p.vessel && p.vessel != vessel)
						{
							float distance = hit.distance;
							//Scales down the damage based on the increased surface area of the area being hit by the laser. Think flashlight on a wall.
							p.AddDamage(laserDamage / (1 + Mathf.PI * Mathf.Pow(tanAngle * distance, 2)) *
											 TimeWarp.fixedDeltaTime
											 * 0.425f);

							if (BDArmorySettings.INSTAKILL) p.Destroy();
						}

						if (Time.time - timeFired > 6 / 120 && BDArmorySettings.BULLET_HITS)
						{
							BulletHitFX.CreateBulletHit(p, hit.point, hit, hit.normal, false, 0, 0);
						}
					}
					else
					{
						laserPoint = lr.transform.InverseTransformPoint((targetDirectionLR * maxTargetingRange) + tf.position);
						lr.SetPosition(1, laserPoint);
					}
				}
				heat += heatPerShot * TimeWarp.CurrentRate;
				return true;
			}
			else
			{
				return false;
			}
		}
		void SetupLaserSpecifics()
		{
			chargeSound = GameDatabase.Instance.GetAudioClip(chargeSoundPath);
			if (HighLogic.LoadedSceneIsFlight)
			{
				audioSource.clip = fireSound;
			}

			laserRenderers = new LineRenderer[fireTransforms.Length];

			for (int i = 0; i < fireTransforms.Length; i++)
			{
				Transform tf = fireTransforms[i];
				laserRenderers[i] = tf.gameObject.AddComponent<LineRenderer>();
				Color laserColor = Misc.Misc.ParseColor255(projectileColor);
				laserColor.a = laserColor.a / 2;
				laserRenderers[i].material = new Material(Shader.Find("KSP/Particles/Alpha Blended"));
				laserRenderers[i].material.SetColor("_TintColor", laserColor);
				laserRenderers[i].material.mainTexture = GameDatabase.Instance.GetTexture("BDArmory/Textures/laser", false);
				laserRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; //= false;
				laserRenderers[i].receiveShadows = false;
				laserRenderers[i].startWidth = tracerStartWidth;
				laserRenderers[i].endWidth = tracerEndWidth;
				laserRenderers[i].positionCount = 2;
				laserRenderers[i].SetPosition(0, Vector3.zero);
				laserRenderers[i].SetPosition(1, Vector3.zero);
				laserRenderers[i].useWorldSpace = false;
				laserRenderers[i].enabled = false;
			}
		}

		public override string GetInfo()
		{
			StringBuilder output = new StringBuilder();
			output.Append(Environment.NewLine);
			output.AppendLine($"Weapon Type: Laser");
			output.AppendLine($"Ammunition: {ammoName}");
			output.AppendLine($"Laser damage: {laserDamage}");

			return output.ToString();
		}
	}
}
