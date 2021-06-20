using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.FX;
using BDArmory.Parts;
using BDArmory.Shaders;
using BDArmory.Control;
using UnityEngine;
using BDArmory.Misc;
using BDArmory.Modules;
using BDArmory.Core.Module;

namespace BDArmory.Bullets
{
    public class PooledBullet : MonoBehaviour
    {
        #region Declarations

        public BulletInfo bullet;
        public float leftPenetration;

        public enum PooledBulletTypes
        {
            Standard,
            Explosive
        }

        public enum BulletDragTypes
        {
            None,
            AnalyticEstimate,
            NumericalIntegration
        }

        public PooledBulletTypes bulletType;
        public BulletDragTypes dragType;

        public Vessel sourceVessel;
        public string sourceVesselName;
        public Color lightColor = Misc.Misc.ParseColor255("255, 235, 145, 255");
        public Color projectileColor;
        public string bulletTexturePath;
        public bool fadeColor;
        public Color startColor;
        Color currentColor;
        public bool bulletDrop = true;
        public float tracerStartWidth = 1;
        public float tracerEndWidth = 1;
        public float tracerLength = 0;
        public float tracerDeltaFactor = 1.35f;
        public float tracerLuminance = 1;
        public float initialSpeed;

        public Vector3 currPosition;

        //explosive parameters
        public float radius = 30;
        public float tntMass = 0;
        public float blastPower = 8;
        public float blastHeat = -1;
        public float bulletDmgMult = 1;
        public string explModelPath;
        public string explSoundPath;

        //gravitic parameters
        public float impulse = 0;
        public float massMod = 0;

        Vector3 startPosition;
        public bool airDetonation = false;
        public bool proximityDetonation = false;
        public float detonationRange = 5f;
        public float defaultDetonationRange = 3500f;
        public float maxAirDetonationRange = 3500f;
        float randomWidthScale = 1;
        LineRenderer bulletTrail;
        public float timeToLiveUntil;
        Light lightFlash;
        bool wasInitiated;
        public Vector3 currentVelocity;
        public float bulletMass;
        public float caliber = 1;
        public float bulletVelocity; //muzzle velocity
        public bool explosive = false;
        public float apBulletMod = 0;
        public bool sabot = false;
        public float ballisticCoefficient;
        public float flightTimeElapsed;
        public static Shader bulletShader;
        public static bool shaderInitialized;
        private float impactVelocity;
        private float dragVelocityFactor;

        public bool hasPenetrated = false;
        public bool hasDetonated = false;
        public bool hasRicocheted = false;

        public int penTicker = 0;

        public Rigidbody rb;

        Ray bulletRay;

        #endregion Declarations

        private Vector3[] linePositions = new Vector3[2];

        private double distanceTraveled = 0;
        public double DistanceTraveled { get { return distanceTraveled; } }

        void OnEnable()
        {
            startPosition = transform.position;
            initialSpeed = currentVelocity.magnitude; // this is the velocity used for drag estimations (only), use total velocity, not muzzle velocity
            if (initialSpeed > 1500 && caliber < 30)
            {
                sabot = true; //assume anyround moving more that 1500m/s is a sabot round
            }
            distanceTraveled = 0; // Reset the distance travelled for the bullet (since it comes from a pool).

            if (!wasInitiated)
            {
                //projectileColor.a = projectileColor.a/2;
                //startColor.a = startColor.a/2;
            }

            projectileColor.a = Mathf.Clamp(projectileColor.a, 0.25f, 1f);
            startColor.a = Mathf.Clamp(startColor.a, 0.25f, 1f);
            currentColor = projectileColor;
            if (fadeColor)
            {
                currentColor = startColor;
            }

            if (lightFlash == null || !gameObject.GetComponent<Light>())
            {
                lightFlash = gameObject.AddOrGetComponent<Light>();
                lightFlash.type = LightType.Point;
                lightFlash.range = 8;
                lightFlash.intensity = 1;
                lightFlash.color = lightColor;
                lightFlash.enabled = true;
            }

            //tracer setup
            if (bulletTrail == null || !gameObject.GetComponent<LineRenderer>())
            {
                bulletTrail = gameObject.AddOrGetComponent<LineRenderer>();
            }

            if (!wasInitiated)
            {
                bulletTrail.positionCount = linePositions.Length;
            }
            linePositions[0] = transform.position;
            linePositions[1] = transform.position;
            bulletTrail.SetPositions(linePositions);

            if (!shaderInitialized)
            {
                shaderInitialized = true;
                bulletShader = BDAShaderLoader.BulletShader;
            }

            if (!wasInitiated)
            {
                bulletTrail.material = new Material(bulletShader);
                randomWidthScale = UnityEngine.Random.Range(0.5f, 1f);
                gameObject.layer = 15;
            }

            bulletTrail.material.mainTexture = GameDatabase.Instance.GetTexture(bulletTexturePath, false);
            bulletTrail.material.SetColor("_TintColor", currentColor);
            bulletTrail.material.SetFloat("_Lum", tracerLuminance);

            tracerStartWidth *= 2f;
            tracerEndWidth *= 2f;

            leftPenetration = 1;
            wasInitiated = true;
            StartCoroutine(FrameDelayedRoutine());

            // Log shots fired.
            if (this.sourceVessel)
            {
                sourceVesselName = sourceVessel.GetName(); // Set the source vessel name as the vessel might have changed its name or died by the time the bullet hits.
                if (BDACompetitionMode.Instance && BDACompetitionMode.Instance.Scores.ContainsKey(sourceVesselName))
                    ++BDACompetitionMode.Instance.Scores[sourceVesselName].shotsFired;
            }
            else
            {
                sourceVesselName = null;
            }
        }

        void OnDisable()
        {
            sourceVessel = null;
        }

        void OnDestroy()
        {
            StopCoroutine(FrameDelayedRoutine());
        }

        IEnumerator FrameDelayedRoutine()
        {
            yield return new WaitForFixedUpdate();
            lightFlash.enabled = false;
        }

        void OnWillRenderObject()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }
            Camera currentCam = Camera.current;
            if (TargetingCamera.IsTGPCamera(currentCam))
            {
                UpdateWidth(currentCam, 4);
            }
            else
            {
                UpdateWidth(currentCam, 1);
            }
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            //floating origin and velocity offloading corrections
            if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
            {
                transform.position -= FloatingOrigin.OffsetNonKrakensbane;
                startPosition -= FloatingOrigin.OffsetNonKrakensbane;
            }

            if (tracerLength == 0)
            {
                // visual tracer velocity is relative to the observer
                linePositions[0] = transform.position + ((currentVelocity - FlightGlobals.ActiveVessel.Velocity()) * tracerDeltaFactor * 0.45f * Time.fixedDeltaTime);
            }
            else
            {
                linePositions[0] = transform.position + ((currentVelocity - FlightGlobals.ActiveVessel.Velocity()).normalized * tracerLength);
            }

            if (fadeColor)
            {
                FadeColor();
                bulletTrail.material.SetColor("_TintColor", currentColor * tracerLuminance);
            }
            linePositions[1] = transform.position;

            bulletTrail.SetPositions(linePositions);

            if (Time.time > timeToLiveUntil) //kill bullet when TTL ends
            {
                KillBullet();
                return;
            }

            if (CheckBulletCollision(Time.fixedDeltaTime))
                return;

            MoveBullet(Time.fixedDeltaTime);

            //////////////////////////////////////////////////
            //Flak Explosion (air detonation/proximity fuse)
            //////////////////////////////////////////////////

            if (ProximityAirDetonation((float)distanceTraveled))
            {
                //detonate
                ExplosionFx.CreateExplosion(currPosition, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, currentVelocity, false, bulletMass);
                KillBullet();

                return;
            }
        }

        /// <summary>
        /// Move the bullet for the period of time, tracking distance traveled and accounting for drag and gravity.
        /// This is now done using the second order symplectic leapfrog method.
        /// </summary>
        /// <param name="period">Period to consider, typically Time.fixedDeltaTime</param>
        public void MoveBullet(float period)
        {
            if (bulletDrop)
                currentVelocity += 0.5f * period * FlightGlobals.getGeeForceAtPosition(transform.position);

            //calculate flight time for drag purposes
            flightTimeElapsed += period;

            // calculate flight distance for achievement purposes
            distanceTraveled += currentVelocity.magnitude * period;

            //Drag types currently only affect Impactvelocity
            //Numerical Integration is currently Broken
            switch (dragType)
            {
                case BulletDragTypes.None:
                    break;

                case BulletDragTypes.AnalyticEstimate:
                    CalculateDragAnalyticEstimate();
                    break;

                case BulletDragTypes.NumericalIntegration:
                    CalculateDragNumericalIntegration();
                    break;
            }

            //move bullet
            transform.position += currentVelocity * period;

            if (bulletDrop)
                currentVelocity += 0.5f * period * FlightGlobals.getGeeForceAtPosition(transform.position);
        }

        /// <summary>
        /// Check for bullet collision in the upcoming period. 
        /// </summary>
        /// <param name="period">Period to consider, typically Time.fixedDeltaTime</param>
        /// <param name="reverse">Also perform raycast in reverse to detect collisions from rays starting within an object.</param>
        /// <returns>true if a collision is detected, false otherwise.</returns>
        public bool CheckBulletCollision(float period, bool reverse = false)
        {
            //reset our hit variables to default state
            hasPenetrated = true;
            hasDetonated = false;
            hasRicocheted = false;
            penTicker = 0;
            currPosition = transform.position;

            float dist = currentVelocity.magnitude * period;
            bulletRay = new Ray(currPosition, currentVelocity + 0.5f * period * FlightGlobals.getGeeForceAtPosition(transform.position));
            var hits = Physics.RaycastAll(bulletRay, dist, 9076737);
            if (reverse)
            {
                var reverseHits = Physics.RaycastAll(new Ray(currPosition + currentVelocity * period, -currentVelocity), dist, 9076737);
                for (int i = 0; i < reverseHits.Length; ++i)
                {
                    reverseHits[i].distance = dist - reverseHits[i].distance;
                }
                hits = hits.Concat(reverseHits).ToArray();
            }
            if (hits.Length > 0)
            {
                var orderedHits = hits.OrderBy(x => x.distance);

                using (var hitsEnu = orderedHits.GetEnumerator())
                {
                    while (hitsEnu.MoveNext())
                    {
                        if (!hasPenetrated || hasRicocheted || hasDetonated)
                        {
                            return true;
                        }

                        RaycastHit hit = hitsEnu.Current;
                        Part hitPart = null;
                        KerbalEVA hitEVA = null;

                        try
                        {
                            hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                            hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                        }
                        catch (NullReferenceException e)
                        {
                            Debug.Log("[BDArmory.PooledBullet]:NullReferenceException for Ballistic Hit: " + e.Message);
                            return true;
                        }

                        if (hitPart != null && ProjectileUtils.IsIgnoredPart(hitPart)) continue; // Ignore ignored parts.

                        if (hitEVA != null)
                        {
                            hitPart = hitEVA.part;
                            // relative velocity, separate from the below statement, because the hitpart might be assigned only above
                            if (hitPart.rb != null)
                                impactVelocity = (currentVelocity * dragVelocityFactor - (hitPart.rb.velocity + Krakensbane.GetFrameVelocityV3f())).magnitude;
                            else
                                impactVelocity = currentVelocity.magnitude * dragVelocityFactor;
                            distanceTraveled += hit.distance;
                            ProjectileUtils.ApplyDamage(hitPart, hit, 1, 1, caliber, bulletMass, impactVelocity, bulletDmgMult, distanceTraveled, explosive, hasRicocheted, sourceVessel, bullet.name);
                            ExplosiveDetonation(hitPart, hit, bulletRay);
                            KillBullet(); // Kerbals are too thick-headed for penetration...
                            return true;
                        }

                        if (hitPart != null && hitPart.vessel == sourceVessel) continue;  //avoid autohit;

                        Vector3 impactVector = currentVelocity;
                        if (hitPart != null && hitPart.rb != null)
                        {
                            // using relative velocity vector instead of just bullet velocity
                            // since KSP vessels might move faster than bullets
                            impactVector = currentVelocity * dragVelocityFactor - (hitPart.rb.velocity + Krakensbane.GetFrameVelocityV3f());
                        }

                        float hitAngle = Vector3.Angle(impactVector, -hit.normal);

                        if (ProjectileUtils.CheckGroundHit(hitPart, hit, caliber))
                        {
                            ProjectileUtils.CheckBuildingHit(hit, bulletMass, currentVelocity, bulletDmgMult);
                            if (!RicochetScenery(hitAngle))
                            {
                                ExplosiveDetonation(hitPart, hit, bulletRay);
                                KillBullet();
                                distanceTraveled += hit.distance;
                                return true;
                            }
                            else
                            {
                                DoRicochet(hitPart, hit, hitAngle, hit.distance / dist, period);
                                return true;
                            }
                        }

                        //Standard Pipeline Hitpoints, Armor and Explosives
                        impactVelocity = impactVector.magnitude;
                        if (massMod != 0)
                        {
                            var ME = hitPart.FindModuleImplementing<ModuleMassAdjust>();
                            if (ME == null)
                            {
                                ME = (ModuleMassAdjust)hitPart.AddModule("ModuleMassAdjust");
                            }
                            ME.massMod += massMod;
                            ME.duration += BDArmorySettings.WEAPON_FX_DURATION;
                        }
                        if (impulse != 0 && hitPart.rb != null)
                        {
                            hitPart.rb.AddForceAtPosition(impactVector.normalized * impulse, hit.point, ForceMode.Acceleration);
                            ProjectileUtils.ApplyScore(hitPart, sourceVesselName, distanceTraveled, 0, bullet.name, true);
                            break; //impulse rounds shouldn't penetrate/do damage
                        }
                        float anglemultiplier = (float)Math.Cos(Math.PI * hitAngle / 180.0);
                        //need to:
                        //calculate armor thickness
                        float thickness = ProjectileUtils.CalculateThickness(hitPart, anglemultiplier);
                        //calculate armor strength
                        float penetration = 0;
                        float penetrationFactor = 0;
                        var Armor = hitPart.FindModuleImplementing<HitpointTracker>();
                        if (Armor != null)
                        {
                            float Ductility = Armor.Ductility;
                            float hardness = Armor.Hardness;
                            float Strength = Armor.Strength;
                            float safeTemp = Armor.SafeUseTemp;
                            float Density = Armor.Density;
                            Debug.Log("[PooledBUllet].ArmorVars found: Strength : " + Strength + "; Ductility: " + Ductility + "; Hardness: " + hardness + "; MaxTemp: " + safeTemp + "; Density: " + Density);
                            float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(bulletMass, impactVelocity);
                            float armorStrength = ProjectileUtils.CalculateArmorStrength(caliber, thickness, Ductility, Strength, Density, safeTemp, hitPart);
                            //calculate bullet deformation
                            float newCaliber = ProjectileUtils.CalculateDeformation(armorStrength, bulletEnergy, caliber, impactVelocity, hardness, apBulletMod, Density);
                            //calculate penetration
                            penetration = ProjectileUtils.CalculatePenetration(caliber, newCaliber, bulletMass, impactVelocity, Ductility, Density, Strength, thickness);
                            caliber = newCaliber; //update bullet with new caliber post-deformation(if any)
                            penetrationFactor = ProjectileUtils.CalculateArmorPenetration(hitPart, penetration);
                            ProjectileUtils.CalculateArmorDamage(hitPart, penetrationFactor, caliber, hardness, Ductility, Density, impactVelocity, sourceVesselName);

                            //calculate return bullet post-pen vel

                            //calculate armor damage
                        }
                        else
                        {
                            Debug.Log("[PooledBUllet].ArmorVars not found; hitPart null");
                        }
                        if (penetrationFactor > 1)
                        {
                            currentVelocity = currentVelocity * (float)Math.Sqrt(thickness / penetration);
                            if (penTicker > 0) currentVelocity *= 0.55f; //implement armor density modifying this ar some point?
                            flightTimeElapsed -= period;
                        }
                        if (penetrationFactor >= 2)
                        {
                            //its not going to bounce if it goes right through
                            hasRicocheted = false;
                        }
                        else
                        {
                            if (RicochetOnPart(hitPart, hit, hitAngle, impactVelocity, hit.distance / dist, period))
                            {
                                bool viableBullet = ProjectileUtils.CalculateBulletStatus(bulletMass, caliber, sabot);
                                if (!viableBullet)
                                {
                                    KillBullet();
                                    return true;
                                }
                                else
                                {
                                    hasRicocheted = true;
                                }
                            }

                        }

                        if (penetrationFactor > 1 && !hasRicocheted) //fully penetrated continue ballistic damage
                        {
                            hasPenetrated = true;
                            bool viableBullet = ProjectileUtils.CalculateBulletStatus(bulletMass, caliber, sabot);
                            ProjectileUtils.ApplyDamage(hitPart, hit, 1, penetrationFactor, caliber, bulletMass, impactVelocity, bulletDmgMult, distanceTraveled, explosive, hasRicocheted, sourceVessel, bullet.name);
                            penTicker += 1;
                            ProjectileUtils.CheckPartForExplosion(hitPart);

                            //Explosive bullets that penetrate should explode shortly after
                            //if penetration is very great, they will have moved on
                            //checking velocity as they would not be able to come out the other side
                            //if (explosive && penetrationFactor < 3 || currentVelocity.magnitude <= 800f)
                            if (explosive || !viableBullet)
                            {
                                //move bullet
                                transform.position += (currentVelocity * period) / 3;

                                distanceTraveled += hit.distance;
                                if (explosive)
                                {
                                    ExplosiveDetonation(hitPart, hit, bulletRay);
                                    ProjectileUtils.CalculateShrapnelDamage(hitPart, hit, caliber, tntMass, 0, sourceVesselName, bulletMass, penetrationFactor); //calc daamge from bullet exploding
                                }
                                hasDetonated = true;
                                KillBullet();
                                return true;
                            }
                        }
                        else if (!hasRicocheted) // explosive bullets that get stopped by armor will explode
                        {
                            if (hitPart.rb != null && hitPart.rb.mass > 0)
                            {
                                float forceAverageMagnitude = impactVelocity * impactVelocity *
                                                      (1f / hit.distance) * (bulletMass - tntMass);

                                float accelerationMagnitude =
                                    forceAverageMagnitude / (hitPart.vessel.GetTotalMass() * 1000);

                                hitPart.rb.AddForceAtPosition(impactVector.normalized * accelerationMagnitude, hit.point, ForceMode.Acceleration);

                                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                                    Debug.Log("[BDArmory.PooledBullet]: Force Applied " + Math.Round(accelerationMagnitude, 2) + "| Vessel mass in kgs=" + hitPart.vessel.GetTotalMass() * 1000 + "| bullet effective mass =" + (bulletMass - tntMass));
                            }

                            distanceTraveled += hit.distance;
                            hasPenetrated = false;
                            ProjectileUtils.ApplyDamage(hitPart, hit, 1, penetrationFactor, caliber, bulletMass, impactVelocity, bulletDmgMult, distanceTraveled, explosive, hasRicocheted, sourceVessel, bullet.name);
                            ExplosiveDetonation(hitPart, hit, bulletRay);
                            hasDetonated = true;
                            KillBullet();
                            return true;
                        }

                        /////////////////////////////////////////////////////////////////////////////////
                        // penetrated after a few ticks
                        /////////////////////////////////////////////////////////////////////////////////

                        //penetrating explosive
                        //richochets
                        if ((penTicker >= 2 && explosive) || (hasRicocheted && explosive))
                        {
                            //detonate
                            ExplosiveDetonation(hitPart, hit, bulletRay, airDetonation);
                            distanceTraveled += hit.distance;
                            return true;
                        }

                        //bullet should not go any further if moving too slowly after hit
                        //smaller caliber rounds would be too deformed to do any further damage
                        if (currentVelocity.magnitude <= 100 && hasPenetrated)
                        {
                            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                            {
                                Debug.Log("[BDArmory.PooledBullet]: Bullet Velocity too low, stopping");
                            }
                            KillBullet();
                            distanceTraveled += hit.distance;
                            return true;
                        }
                    }//end While
                }//end enumerator
            }//end of hits
            return false;
        }

        private bool ProximityAirDetonation(float distanceFromStart)
        {
            bool detonate = false;

            if (distanceFromStart <= 500f) return false;

            if (explosive && airDetonation)
            {
                if (distanceFromStart > maxAirDetonationRange || distanceFromStart > defaultDetonationRange)
                {
                    return detonate = true;
                }

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
                                if (partHit == null) continue;
                                if (partHit.vessel == sourceVessel) continue;
                                if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.

                                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                                    Debug.Log("[BDArmory.PooledBullet]: Bullet proximity sphere hit | Distance overlap = " + detonationRange + "| Part name = " + partHit.name);

                                return detonate = true;
                            }
                            catch (Exception e)
                            {
                                // ignored
                                Debug.LogWarning("[BDArmory.PooledBullet]: Exception thrown in ProximityAirDetonation: " + e.Message + "\n" + e.StackTrace);
                            }
                        }
                    }
                }
            }
            return detonate;
        }

        private void CalculateDragNumericalIntegration()
        {
            Vector3 dragAcc = currentVelocity * currentVelocity.magnitude *
                              (float)
                              FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position),
                                  FlightGlobals.getExternalTemperature(transform.position));
            dragAcc *= 0.5f;
            dragAcc /= ballisticCoefficient;

            currentVelocity -= dragAcc * TimeWarp.deltaTime;
            //numerical integration; using Euler is silly, but let's go with it anyway
        }

        private void CalculateDragAnalyticEstimate()
        {
            float analyticDragVelAdjustment = (float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(currPosition), FlightGlobals.getExternalTemperature(currPosition));
            analyticDragVelAdjustment *= flightTimeElapsed * initialSpeed;
            analyticDragVelAdjustment += 2 * ballisticCoefficient;

            analyticDragVelAdjustment = 2 * ballisticCoefficient * initialSpeed / analyticDragVelAdjustment;
            //velocity as a function of time under the assumption of a projectile only acted upon by drag with a constant drag area

            dragVelocityFactor = analyticDragVelAdjustment / initialSpeed;
        }

        private bool ExplosiveDetonation(Part hitPart, RaycastHit hit, Ray ray, bool airDetonation = false)
        {
            ///////////////////////////////////////////////////////////////////////
            // High Explosive Detonation
            ///////////////////////////////////////////////////////////////////////

            if (hitPart == null || hitPart.vessel != sourceVessel)
            {
                //if bullet hits and is HE, detonate and kill bullet
                if (explosive)
                {
                    if (BDArmorySettings.DRAW_DEBUG_LABELS)
                    {
                        Debug.Log("[BDArmory.PooledBullet]: Detonation Triggered | penetration: " + hasPenetrated + " penTick: " + penTicker + " airDet: " + airDetonation);
                    }

                    if (airDetonation)
                    {
                        ExplosionFx.CreateExplosion(hit.point, GetExplosivePower(), explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, default, false, bulletMass);
                    }
                    else
                    {
                        ExplosionFx.CreateExplosion(hit.point - (ray.direction * 0.1f), GetExplosivePower(), explModelPath, explSoundPath, ExplosionSourceType.Bullet, caliber, null, sourceVesselName, null, default, false, bulletMass);
                    }

                    KillBullet();
                    hasDetonated = true;
                    return true;
                }
            }
            return false;
        }

        public void UpdateWidth(Camera c, float resizeFactor)
        {
            if (c == null)
            {
                return;
            }
            if (bulletTrail == null)
            {
                return;
            }
            if (!gameObject.activeInHierarchy)
            {
                return;
            }

            float fov = c.fieldOfView;
            float factor = (fov / 60) * resizeFactor *
                           Mathf.Clamp(Vector3.Distance(transform.position, c.transform.position), 0, 3000) / 50;
            bulletTrail.startWidth = tracerStartWidth * factor * randomWidthScale;
            bulletTrail.endWidth = tracerEndWidth * factor * randomWidthScale;
        }

        void KillBullet()
        {
            gameObject.SetActive(false);
        }

        void FadeColor()
        {
            Vector4 endColorV = new Vector4(projectileColor.r, projectileColor.g, projectileColor.b, projectileColor.a);
            float delta = TimeWarp.deltaTime;
            Vector4 finalColorV = Vector4.MoveTowards(currentColor, endColorV, delta);
            currentColor = new Color(finalColorV.x, finalColorV.y, finalColorV.z, Mathf.Clamp(finalColorV.w, 0.25f, 1f));
        }

        bool RicochetOnPart(Part p, RaycastHit hit, float angleFromNormal, float impactVel, float fractionOfDistance, float period)
        {
            float hitTolerance = p.crashTolerance;
            //15 degrees should virtually guarantee a ricochet, but 75 degrees should nearly always be fine
            float chance = (((angleFromNormal - 5) / 75) * (hitTolerance / 150)) * 100 / Mathf.Clamp01(impactVel / 600);
            float random = UnityEngine.Random.Range(0f, 100f);
            if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.PooledBullet]: Ricochet chance: " + chance);
            if (random < chance)
            {
                DoRicochet(p, hit, angleFromNormal, fractionOfDistance, period);
                return true;
            }
            else
            {
                return false;
            }
        }

        bool RicochetScenery(float hitAngle)
        {
            float reflectRandom = UnityEngine.Random.Range(-75f, 90f);
            if (reflectRandom > 90 - hitAngle && caliber <= 30f)
            {
                return true;
            }

            return false;
        }

        public void DoRicochet(Part p, RaycastHit hit, float hitAngle, float fractionOfDistance, float period)
        {
            //ricochet
            if (BDArmorySettings.BULLET_HITS)
            {
                BulletHitFX.CreateBulletHit(p, hit.point, hit, hit.normal, true, caliber, 0);
            }

            tracerStartWidth /= 2;
            tracerEndWidth /= 2;

            MoveBullet(fractionOfDistance * period);
            transform.position = hit.point; // Error in position is around 1e-3.
            currentVelocity = Vector3.Reflect(currentVelocity, hit.normal);
            currentVelocity = (hitAngle / 150) * currentVelocity * 0.65f;

            Vector3 randomDirection = UnityEngine.Random.rotation * Vector3.one;

            currentVelocity = Vector3.RotateTowards(currentVelocity, randomDirection,
                UnityEngine.Random.Range(0f, 5f) * Mathf.Deg2Rad, 0);
            MoveBullet((1f - fractionOfDistance) * period);
        }

        private float GetExplosivePower()
        {
            return tntMass > 0 ? tntMass : blastPower;
        }
    }
}