using KSP.Localization;
using System.Linq;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.Bullets;
using static BDArmory.Bullets.PooledBullet;
using static BDArmory.Weapons.ModuleWeapon;

namespace BDArmory.Weapons
{
    public class BDCustomWarhead : BDWarheadBase
    {
        [KSPField]
        public string warheadType = "def";
        public string warheadReportingName;
        public BulletInfo _warheadType;

        [KSPField]
        public string explModelPath = "BDArmory/Models/explosion/explosion";

        [KSPField]
        public string explSoundPath = "BDArmory/Sounds/explode1";

        [KSPField]
        public string smokeTexturePath = "";

        [KSPField]
        public string bulletTexturePath = "BDArmory/Textures/bullet";

        [KSPField]
        public float maxDeviation = 0f;

        public void ParseWarheadType()
        {
            _warheadType = BulletInfo.bullets[warheadType];
            if (_warheadType.DisplayName != "Default Bullet")
                warheadReportingName = _warheadType.DisplayName;
            else
                warheadReportingName = _warheadType.name;
        }

        private void FireProjectile(float detRange = -1, float detTime = -1)
        {
            SourceInfo sourceInfo = new SourceInfo(vessel, Team.Name, part, transform.position);
            GraphicsInfo graphicsInfo = new GraphicsInfo(bulletTexturePath, GUIUtils.ParseColor255(_warheadType.projectileColor), GUIUtils.ParseColor255(_warheadType.startColor),
                _warheadType.caliber / 300, _warheadType.caliber / 750, 0, 1.75f, 2.65f, smokeTexturePath, explModelPath, explSoundPath);
            NukeInfo nukeInfo = new NukeInfo(); // Will inherit parent part's models on enable
            Vector3[] firedVelocities = new Vector3[_warheadType.projectileCount];

            float currentSpeed = (float)vessel.Velocity().magnitude;

            //float incrementVelocity = 1000 / ( + _warheadType.bulletVelocity); //using 1km/s as a reference Unit
            //float dispersionAngle = _warheadType.subProjectileDispersion > 0 ? _warheadType.subProjectileDispersion : 0.5f; //fewer fragments/pellets are going to be larger-> move slower, less dispersion
            //float dispersionVelocityforAngle = 1000 / incrementVelocity * Mathf.Sin(dispersionAngle * Mathf.Deg2Rad); // convert m/s despersion to angle, accounting for vel of round
            for (int s = 0; s < _warheadType.projectileCount; s++)
            {
                //firedVelocities[s] = UnityEngine.Random.onUnitSphere * dispersionVelocityforAngle;
                firedVelocities[s] = VectorUtils.GaussianDirectionDeviation(transform.forward, (maxDeviation / 2)) * _warheadType.bulletVelocity;
            }

            BulletFuzeTypes sFuze;

            if (_warheadType.tntMass > 0 || _warheadType.beehive)
            {
                detRange = detRange < 0 ? detonationRange : detRange;
                detTime = detTime < 0 ? detonationRange / (currentSpeed + _warheadType.bulletVelocity) : detTime;
                string fuzeTypeS = _warheadType.fuzeType.ToLower();
                switch (fuzeTypeS)
                {
                    //Anti-Air fuzes
                    case "timed":
                        sFuze = BulletFuzeTypes.Timed;
                        break;
                    case "proximity":
                        sFuze = BulletFuzeTypes.Proximity;
                        break;
                    case "flak":
                        sFuze = BulletFuzeTypes.Flak;
                        break;
                    //Anti-Armor fuzes
                    case "delay":
                        sFuze = BulletFuzeTypes.Delay;
                        break;
                    case "penetrating":
                        sFuze = BulletFuzeTypes.Penetrating;
                        break;
                    case "impact":
                        sFuze = BulletFuzeTypes.Impact;
                        break;
                    case "none":
                        sFuze = BulletFuzeTypes.None;
                        break;
                    default:
                        sFuze = BulletFuzeTypes.Impact;
                        break;
                }
            }
            else
            {
                sFuze = PooledBullet.BulletFuzeTypes.None;
            }

            PooledBulletTypes eHEType;

            if (_warheadType.tntMass > 0)
            {
                switch (_warheadType.explosive.ToLower())
                {
                    case "standard":
                        eHEType = PooledBulletTypes.Explosive;
                        break;
                    //legacy support for older configs that are still explosive = true
                    case "true":
                        eHEType = PooledBulletTypes.Explosive;
                        break;
                    case "shaped":
                        eHEType = PooledBulletTypes.Shaped;
                        break;
                    default:
                        eHEType = PooledBulletTypes.Slug;
                        break;
                }
            }
            else
            {
                eHEType = PooledBulletTypes.Slug;
            }

            PooledBullet.BulletDragTypes eDragType;
            switch (_warheadType.bulletDragTypeName)
            {
                case "None":
                    eDragType = PooledBullet.BulletDragTypes.None;
                    break;
                case "AnalyticEstimate":
                    eDragType = PooledBullet.BulletDragTypes.AnalyticEstimate;
                    break;
                case "NumericalIntegration":
                    eDragType = PooledBullet.BulletDragTypes.NumericalIntegration;
                    break;
                default:
                    eDragType = PooledBullet.BulletDragTypes.AnalyticEstimate;
                    break;
            }

            FireBullet(_warheadType, _warheadType.projectileCount, sourceInfo, graphicsInfo, nukeInfo, firedVelocities, true,
                    _warheadType.projectileTTL + (detTime < 0.0f ? 0.0f : detTime),
                    TimeWarp.fixedDeltaTime, detRange, detTime, eHEType, sFuze, eDragType,
                    true, false, null, null, false, 1f, true, currentSpeed);
        }

        protected override void WarheadSpecificSetup()
        {
            ParseWarheadType();
        }
        protected override void WarheadSpecificUISetup()
        {

        }

        public override void DetonateIfPossible()
        {
            if (!hasDetonated && Armed)
            {
                hasDetonated = true;

                if (fuseFailureRate > 0f)
                    if (Random.Range(0f, 1f) < fuseFailureRate)
                        fuseFailed = true;

                if (!fuseFailed)
                {
                    direction = part.partTransform.forward; //both the missileReferenceTransform and smallWarhead part's forward direction is Z+, or transform.forward.
                                                            // could also do warheadType == "standard" ? default: part.partTransform.forward, as this simplifies the isAngleAllowed check in ExplosionFX, but at the cost of standard heads always being 360deg blasts (but we don't have limited angle blasts for missiels at present anyway, so not a bit deal RN)
                                                            //var sourceWeapon = part.FindModuleImplementing<EngageableWeapon>();
                    FireProjectile();

                    ////////////////////////////////////////////////////
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDCustomWarhead]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} (Team:{Team.Name}) detonating with a {warheadType} warhead");
                    part.explode();
                }
                else
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDCustomWarhead]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} explosive fuse failed!");
            }
        }

        protected override void Detonate()
        {
            if (!hasDetonated && Armed)
            {
                hasDetonated = true;

                if (fuseFailureRate > 0f)
                    if (Random.Range(0f, 1f) < fuseFailureRate)
                        fuseFailed = true;

                if (!fuseFailed)
                {
                    direction = part.partTransform.forward;
                    FireProjectile();
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDCustomWarhead]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} detonating with a {warheadType} warhead");
                    /////////////////////////

                    part.Destroy();
                    part.explode();
                }
                else
                    if (BDArmorySettings.DEBUG_MISSILES)
                        Debug.Log($"[BDArmory.BDCustomWarhead]: {part} ({(uint)(part.GetInstanceID())}) from {SourceVesselName} explosive fuse failed!");
            }
        }
    }
}
