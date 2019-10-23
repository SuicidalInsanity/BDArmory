using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Utils;
using BDArmory.FX;
using BDArmory.Misc;
using BDArmory.UI;
using UniLinq;
using UnityEngine;

namespace BDArmory.Modules
{
	public class RocketLauncher : WeaponBase
	{
		[KSPField(isPersistant = false)] public string rocketType;
		[KSPField(isPersistant = false)] public string rocketModelPath;
		[KSPField(isPersistant = false)] public float rocketMass;
		[KSPField(isPersistant = false)] public float thrust;
		[KSPField(isPersistant = false)] public float thrustTime;
		[KSPField(isPersistant = false)] public float blastRadius;
		[KSPField(isPersistant = false)] public float blastForce;
		[KSPField] public float blastHeat = -1;
		[KSPField(isPersistant = false)] public bool descendingOrder = true;
		[KSPField] public float thrustDeviation = 0.10f;
		[KSPField] public bool externalAmmo = false;
		public double rocketsMax;
		Transform[] rockets;

		public float rippleRPM = 1000;
		public bool bulletDrop = true; //projectiles are affected by gravity
		public new string fireTransformName = "rockets";

		[KSPEvent(guiActive = true, guiName = "Jettison", active = true, guiActiveEditor = false)]
		public void Jettison() // make rocketpods jettisonable
		{
			if (turret || externalAmmo)
			{
				return;
			}
			part.decouple(0);
			if (BDArmorySetup.Instance.ActiveWeaponManager != null)
				BDArmorySetup.Instance.ActiveWeaponManager.UpdateList();
		}

		public new void Start()
		{
			base.Start();

			eWeaponType = WeaponTypes.Rocket;

			if (!proximityDetonation)
			{
				Fields["maxAirDetonationRange"].guiActive = false;
				Fields["maxAirDetonationRange"].guiActiveEditor = false;
				Fields["defaultDetonationRange"].guiActive = false;
				Fields["defaultDetonationRange"].guiActiveEditor = false;
				Fields["detonationRange"].guiActive = false;
				Fields["detonationRange"].guiActiveEditor = false;
			}
			if (HighLogic.LoadedSceneIsFlight)
			{
				if (!externalAmmo)// only call these for rocket pods
				{
					MakeRocketArray();
					UpdateRocketScales();
				}
			}
			SetInitialDetonationDistance(0, blastForce, -1);
			roundsPerMinute = rippleRPM; // port legacy values 
			ammoName = rocketType;
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
					RunTrajectorySimulation(0, thrust, rocketMass, thrustTime, bulletDrop);
					Aim(0, thrust, rocketMass, true);
				}
			}
		}

		void Fire()
		{
			int rocketsLeft;
			if (!externalAmmo)
			{
				PartResource rocketResource = GetRocketResource();
				rocketsLeft = (int)Math.Floor(rocketResource.amount);
				ammoLeft = "Ammo Left: " + rocketResource.amount;
			}
			else
			{
				vessel.GetConnectedResourceTotals(AmmoID, out double ammoCurrent, out double ammoMax);
				rocketsLeft = (int)Math.Floor(ammoCurrent);
				ammoLeft = "Ammo Left: " + ammoCurrent;
			}

			if (BDArmorySettings.INFINITE_AMMO)
			{
				rocketsLeft = 1;
			}
			float timeGap = (60 / roundsPerMinute) * TimeWarp.CurrentRate;
			if (Time.time - timeFired > timeGap && !pointingAtSelf && (aiControlled || !Misc.Misc.CheckMouseIsOnGui()) && WMgrAuthorized())
			{// fixes rocket ripple code for proper rippling
				for (float iTime = Mathf.Min(Time.time - timeFired - timeGap, TimeWarp.fixedDeltaTime); iTime >= 0; iTime -= timeGap)
				{
					if (rocketsLeft >= 1)
					{
						if (!externalAmmo)
						{
							Transform currentRocketTfm = rockets[rocketsLeft - 1];
							GameObject rocketObj = GameDatabase.Instance.GetModel(rocketModelPath);
							rocketObj = (GameObject)Instantiate(rocketObj, currentRocketTfm.position, currentRocketTfm.parent.rotation);
							rocketObj.transform.rotation = currentRocketTfm.parent.rotation;
							rocketObj.transform.localScale = part.rescaleFactor * Vector3.one;
							currentRocketTfm.localScale = Vector3.zero;
							Rocket rocket = rocketObj.AddComponent<Rocket>();
							rocket.explModelPath = explModelPath;
							rocket.explSoundPath = explSoundPath;
							rocket.spawnTransform = currentRocketTfm;
							rocket.mass = rocketMass;
							rocket.blastForce = blastForce;
							rocket.blastHeat = blastHeat;
							rocket.blastRadius = blastRadius;
							rocket.thrust = thrust;
							rocket.thrustTime = thrustTime;
							rocket.proximityDetonation = proximityDetonation;
							rocket.detonationRange = detonationRange;
							rocket.maxAirDetonationRange = maxAirDetonationRange;
							rocket.randomThrustDeviation = thrustDeviation;
							rocket.sourceVessel = vessel;
							rocketObj.SetActive(true);
							rocketObj.transform.SetParent(currentRocketTfm.parent);
							rocket.parentRB = part.rb;

							if (!BDArmorySettings.INFINITE_AMMO)
							{
								GetRocketResource().amount--;
								UpdateRocketScales();
							}
						}
						else
						{
							for (int i = 0; i < fireTransforms.Length; i++)
							{
								Transform currentRocketTfm = fireTransforms[i];
								GameObject rocketObj = GameDatabase.Instance.GetModel(rocketModelPath);
								rocketObj = (GameObject)Instantiate(rocketObj, currentRocketTfm.position, currentRocketTfm.rotation);
								rocketObj.transform.rotation = currentRocketTfm.rotation;
								Rocket rocket = rocketObj.AddComponent<Rocket>();
								rocket.explModelPath = explModelPath;
								rocket.explSoundPath = explSoundPath;
								rocket.spawnTransform = currentRocketTfm;
								rocket.mass = rocketMass;
								rocket.blastForce = blastForce;
								rocket.blastHeat = blastHeat;
								rocket.blastRadius = blastRadius;
								rocket.thrust = thrust;
								rocket.thrustTime = thrustTime;
								rocket.proximityDetonation = proximityDetonation;
								rocket.detonationRange = detonationRange;
								rocket.maxAirDetonationRange = maxAirDetonationRange;
								rocket.randomThrustDeviation = thrustDeviation;
								rocket.sourceVessel = vessel;
								rocketObj.SetActive(true);
								rocketObj.transform.SetParent(currentRocketTfm);
								rocket.parentRB = part.rb;
								if (!BDArmorySettings.INFINITE_AMMO)
								{
									part.RequestResource(ammoName, 1);
								}
							}
						}
					}
					// add shell casings/heat?	TBH, would be easier to simply move the codeblock to Fire() instead of replicating the recoil/muzzleflash/casings/aduio/heat code.
					//Or to spin this off to ModuleWeaponRocket child class and do a override Fire() call if externalammo.
					//Arguably the best option, wouldn't need MM patches for legacy RL support
					timeFired = Time.time - iTime;
					audioSource.PlayOneShot(GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/launch")); //change to firesound
				}
			}
			if (useRippleFire)
			{
				StartCoroutine(IncrementRippleIndex(initialFireDelay * TimeWarp.CurrentRate));
			}
		}
		void MakeRocketArray()
		{
			Transform rocketsTransform = part.FindModelTransform("rockets");// important to keep this seperate from the fireTransformName transform
			int numOfRockets = rocketsTransform.childCount;     // due to rockets.Rocket_n being inconsistantly aligned 
			rockets = new Transform[numOfRockets];              // (and subsequently messing up the aim() vestors) 
																// and this overwriting the previous fireTransFormName -> fireTransForms
			for (int i = 0; i < numOfRockets; i++)
			{
				string rocketName = rocketsTransform.GetChild(i).name;
				int rocketIndex = int.Parse(rocketName.Substring(7)) - 1;
				rockets[rocketIndex] = rocketsTransform.GetChild(i);
			}
			if (!descendingOrder) Array.Reverse(rockets);
		}

		void UpdateRocketScales()
		{
			PartResource rocketResource = GetRocketResource();
			var rocketsLeft = Math.Floor(rocketResource.amount);
			rocketsMax = rocketResource.maxAmount;
			for (int i = 0; i < rocketsMax; i++)
			{
				if (i < rocketsLeft) rockets[i].localScale = Vector3.one;
				else rockets[i].localScale = Vector3.zero;
			}
		}
		public PartResource GetRocketResource()
		{
			IEnumerator<PartResource> res = part.Resources.GetEnumerator();
			while (res.MoveNext())
			{
				if (res.Current == null) continue;
				if (res.Current.resourceName == ammoName) return res.Current;
			}
			res.Dispose();
			return null;
		}
		public override string GetInfo()
		{
			StringBuilder output = new StringBuilder();
			output.Append(Environment.NewLine);
			output.AppendLine($"Weapon Type: Rocket launcher");

			output.AppendLine($"Rounds Per Minute: {roundsPerMinute * (fireTransforms?.Length ?? 1)}");
			output.AppendLine($"Ammunition: {ammoName}");
				
			output.AppendLine($"Max Range: {maxEffectiveDistance} m");
				
			//output.AppendLine($"Rocket mass: {rocketMass} kg");
			//output.AppendLine($"Thrust: {thrust}kn"); mass and thrust don't really tell us the important bit, so lets replace that with accel
			output.AppendLine($"Acceleration: {thrust / rocketMass}m/s2");
			output.AppendLine($"Blast:");
			output.AppendLine($"- radius: {blastRadius}");
			output.AppendLine($"- power: {blastForce}");
			output.AppendLine($"- heat: {blastHeat}");
			output.AppendLine($"Proximity Fuzed: {proximityDetonation}");
			if (proximityDetonation)
			{
				output.AppendLine($"- fuze radius: {(blastRadius * .66f)} m");
			}			
			return output.ToString();
		}
	}
	public class Rocket : MonoBehaviour
	{
		public Transform spawnTransform;
		public Vessel sourceVessel;
		public float mass;
		public float thrust;
		public float thrustTime;
		public float blastRadius;
		public float blastForce;
		public float blastHeat;
		public bool proximityDetonation;
		public float maxAirDetonationRange;
		public float detonationRange;
		public string explModelPath;
		public string explSoundPath;

		public float randomThrustDeviation = 0.05f;

		public Rigidbody parentRB;

		float startTime;
		public AudioSource audioSource;

		Vector3 prevPosition;
		Vector3 currPosition;
		Vector3 startPosition;

		float stayTime = 0.04f;
		float lifeTime = 10;

		//bool isThrusting = true;

		Rigidbody rb;

		KSPParticleEmitter[] pEmitters;

		float randThrustSeed;

		void Start()
		{
			BDArmorySetup.numberOfParticleEmitters++;

			rb = gameObject.AddComponent<Rigidbody>();
			pEmitters = gameObject.GetComponentsInChildren<KSPParticleEmitter>();

			IEnumerator<KSPParticleEmitter> pe = pEmitters.AsEnumerable().GetEnumerator();
			while (pe.MoveNext())
			{
				if (pe.Current == null) continue;
				if (FlightGlobals.getStaticPressure(transform.position) == 0 && pe.Current.useWorldSpace)
				{
					pe.Current.emit = false;
				}
				else if (pe.Current.useWorldSpace)
				{
					BDAGaplessParticleEmitter gpe = pe.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
					gpe.rb = rb;
					gpe.emit = true;
				}
				else
				{
					EffectBehaviour.AddParticleEmitter(pe.Current);
				}
			}
			pe.Dispose();

			prevPosition = transform.position;
			currPosition = transform.position;
			startPosition = transform.position;
			startTime = Time.time;

			rb.mass = mass;
			rb.isKinematic = true;
			//rigidbody.velocity = startVelocity;
			if (!FlightGlobals.RefFrameIsRotating) rb.useGravity = false;

			rb.useGravity = false;

			randThrustSeed = UnityEngine.Random.Range(0f, 100f);

			SetupAudio();
		}

		void FixedUpdate()
		{
			//floating origin and velocity offloading corrections
			if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
			{
				transform.position -= FloatingOrigin.OffsetNonKrakensbane;
				prevPosition -= FloatingOrigin.OffsetNonKrakensbane;
			}
			float distanceFromStart = Vector3.Distance(transform.position, startPosition);

			if (Time.time - startTime < stayTime && transform.parent != null)
			{
				transform.rotation = transform.parent.rotation;
				transform.position = spawnTransform.position;
				//+(transform.parent.rigidbody.velocity*Time.fixedDeltaTime);
			}
			else
			{
				if (transform.parent != null && parentRB)
				{
					transform.parent = null;
					rb.isKinematic = false;
					rb.velocity = parentRB.velocity + Krakensbane.GetFrameVelocityV3f();
				}
			}

			if (rb && !rb.isKinematic)
			{
				//physics
				if (FlightGlobals.RefFrameIsRotating)
				{
					rb.velocity += FlightGlobals.getGeeForceAtPosition(transform.position) * Time.fixedDeltaTime;
				}

				//guidance and attitude stabilisation scales to atmospheric density.
				float atmosMultiplier =
					Mathf.Clamp01(2.5f *
								  (float)
								  FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position),
									  FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody));

				//model transform. always points prograde
				transform.rotation = Quaternion.RotateTowards(transform.rotation,
					Quaternion.LookRotation(rb.velocity + Krakensbane.GetFrameVelocity(), transform.up),
					atmosMultiplier * (0.5f * (Time.time - startTime)) * 50 * Time.fixedDeltaTime);


				if (Time.time - startTime < thrustTime && Time.time - startTime > stayTime)
				{
					float random = randomThrustDeviation * (1 - (Mathf.PerlinNoise(4 * Time.time, randThrustSeed) * 2));
					float random2 = randomThrustDeviation * (1 - (Mathf.PerlinNoise(randThrustSeed, 4 * Time.time) * 2));
					rb.AddRelativeForce(new Vector3(random, random2, thrust));
				}
			}


			if (Time.time - startTime > thrustTime)
			{
				//isThrusting = false;
				IEnumerator<KSPParticleEmitter> pEmitter = pEmitters.AsEnumerable().GetEnumerator();
				while (pEmitter.MoveNext())
				{
					if (pEmitter.Current == null) continue;
					if (pEmitter.Current.useWorldSpace)
					{
						pEmitter.Current.minSize = Mathf.MoveTowards(pEmitter.Current.minSize, 0.1f, 0.05f);
						pEmitter.Current.maxSize = Mathf.MoveTowards(pEmitter.Current.maxSize, 0.2f, 0.05f);
					}
					else
					{
						pEmitter.Current.minSize = Mathf.MoveTowards(pEmitter.Current.minSize, 0, 0.1f);
						pEmitter.Current.maxSize = Mathf.MoveTowards(pEmitter.Current.maxSize, 0, 0.1f);
						if (pEmitter.Current.maxSize == 0)
						{
							pEmitter.Current.emit = false;
						}
					}
				}
				pEmitter.Dispose();
			}

			if (Time.time - startTime > 0.1f + stayTime)
			{
				currPosition = transform.position;
				float dist = (currPosition - prevPosition).magnitude;
				Ray ray = new Ray(prevPosition, currPosition - prevPosition);
				RaycastHit hit;
				KerbalEVA hitEVA = null;
				//if (Physics.Raycast(ray, out hit, dist, 2228224))
				//{
				//    try
				//    {
				//        hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
				//        if (hitEVA != null)
				//            Debug.Log("[BDArmory]:Hit on kerbal confirmed!");
				//    }
				//    catch (NullReferenceException)
				//    {
				//        Debug.Log("[BDArmory]:Whoops ran amok of the exception handler");
				//    }

				//    if (hitEVA && hitEVA.part.vessel != sourceVessel)
				//    {
				//        Detonate(hit.point);
				//    }
				//}

				if (!hitEVA)
				{
					if (Physics.Raycast(ray, out hit, dist, 9076737))
					{
						Part hitPart = null;
						try
						{
							KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
							hitPart = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
						}
						catch (NullReferenceException)
						{
						}


						if (hitPart == null || (hitPart != null && hitPart.vessel != sourceVessel))
						{
							Detonate(hit.point);
						}
					}
					else if (FlightGlobals.getAltitudeAtPos(transform.position) < 0)
					{
						Detonate(transform.position);
					}
				}
			}
			else if (FlightGlobals.getAltitudeAtPos(currPosition) <= 0)
			{
				Detonate(currPosition);
			}
			prevPosition = currPosition;

			if (Time.time - startTime > lifeTime) // life's 10s, quite a long time for faster rockets
			{
				Detonate(transform.position);
			}
			if (distanceFromStart >= maxAirDetonationRange)//rockets are performance intensive, lets cull those that have flown too far away
			{
				Detonate(transform.position);
			}
			if (ProximityAirDetonation(distanceFromStart))
			{
				Detonate(transform.position);
			}
		}
		private bool ProximityAirDetonation(float distanceFromStart)
		{
			bool detonate = false;

			if (distanceFromStart <= blastRadius) return false;

			if (proximityDetonation)
			{
				using (var hitsEnu = Physics.OverlapSphere(transform.position, detonationRange, 557057).AsEnumerable().GetEnumerator())
				{
					while (hitsEnu.MoveNext())
					{
						if (hitsEnu.Current == null) continue;
						try
						{
							Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
							if (partHit?.vessel != sourceVessel)
							{
								if (BDArmorySettings.DRAW_DEBUG_LABELS)
									Debug.Log("[BDArmory]: Bullet proximity sphere hit | Distance overlap = " + detonationRange + "| Part name = " + partHit.name);
								return detonate = true;
							}
						}
						catch
						{
						}
					}
				}
			}	
			return detonate;
		}
		void Update()
		{
			if (HighLogic.LoadedSceneIsFlight)
			{
				if (BDArmorySetup.GameIsPaused)
				{
					if (audioSource.isPlaying)
					{
						audioSource.Stop();
					}
				}
				else
				{
					if (!audioSource.isPlaying)
					{
						audioSource.Play();
					}
				}
			}
		}
		void Detonate(Vector3 pos)
		{
			BDArmorySetup.numberOfParticleEmitters--;

			ExplosionFx.CreateExplosion(pos, BlastPhysicsUtils.CalculateExplosiveMass(blastRadius),
				explModelPath, explSoundPath, true);

			IEnumerator<KSPParticleEmitter> emitter = pEmitters.AsEnumerable().GetEnumerator();
			while (emitter.MoveNext())
			{
				if (emitter.Current == null) continue;
				if (!emitter.Current.useWorldSpace) continue;
				emitter.Current.gameObject.AddComponent<BDAParticleSelfDestruct>();
				emitter.Current.transform.parent = null;
			}
			emitter.Dispose();
			Destroy(gameObject); //destroy rocket on collision
		}


		void SetupAudio()
		{
			audioSource = gameObject.AddComponent<AudioSource>();
			audioSource.loop = true;
			audioSource.minDistance = 1;
			audioSource.maxDistance = 2000;
			audioSource.dopplerLevel = 0.5f;
			audioSource.volume = 0.9f * BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
			audioSource.pitch = 1f;
			audioSource.priority = 255;
			audioSource.spatialBlend = 1;

			audioSource.clip = GameDatabase.Instance.GetAudioClip("BDArmory/Sounds/rocketLoop");

			UpdateVolume();
			BDArmorySetup.OnVolumeChange += UpdateVolume;
		}

		void UpdateVolume()
		{
			if (audioSource)
			{
				audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
			}
		}
	}
}