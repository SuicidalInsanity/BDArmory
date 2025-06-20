using System;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.WeaponMounts
{
    public class ModuleTurret : PartModule
    {
        [KSPField] public int turretID = 0;

        [KSPField] public string pitchTransformName = "pitchTransform";
        public Transform pitchTransform;

        [KSPField] public string yawTransformName = "yawTransform";
        public Transform yawTransform;

        Transform referenceTransform; //set this to gun's fireTransform

        [KSPField] public float pitchSpeedDPS;
        [KSPField] public float yawSpeedDPS;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_MaxPitch"),//Max Pitch
         UI_FloatRange(minValue = 0f, maxValue = 60f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float maxPitch;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_MinPitch"),//Min Pitch
         UI_FloatRange(minValue = 1f, maxValue = 0f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float minPitch;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_YawRange"),//Yaw Range
         UI_FloatRange(minValue = 1f, maxValue = 60f, stepIncrement = 1f, scene = UI_Scene.All)]
        public float yawRange;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_YawStandbyAngle"),
         UI_FloatRange(minValue = -90f, maxValue = 90f, stepIncrement = 0.5f, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.None)]
        public float yawStandbyAngle = 0;
        Quaternion standbyLocalRotation;// = Quaternion.identity;

        [KSPField(isPersistant = true)] public float minPitchLimit = 400;
        [KSPField(isPersistant = true)] public float maxPitchLimit = 400;
        [KSPField(isPersistant = true)] public float yawRangeLimit = 400;

        [KSPField] public bool smoothRotation = false;
        [KSPField] public float smoothMultiplier = 10;

        //sfx
        [KSPField] public string audioPath;
        [KSPField] public float maxAudioPitch = 0.5f;
        [KSPField] public float minAudioPitch = 0f;
        [KSPField] public float maxVolume = 1;
        [KSPField] public float minVolume = 0;

        AudioClip soundClip;
        AudioSource audioSource;
        bool hasAudio;
        float audioRotationRate;
        float targetAudioRotationRate;
        Vector3 lastTurretDirection;
        float maxAudioRotRate;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            pitchTransform = part.FindModelTransform(pitchTransformName);
            yawTransform = part.FindModelTransform(yawTransformName);

            if (!pitchTransform)
            {
                Debug.LogWarning("[BDArmory.ModuleTurret]: " + part.partInfo.title + " has no pitchTransform");
            }

            if (!yawTransform)
            {
                Debug.LogWarning("[BDArmory.ModuleTurret]: " + part.partInfo.title + " has no yawTransform");
            }

            if (!referenceTransform)
            {
                if (pitchTransform)
                    SetReferenceTransform(pitchTransform);
                else
                    SetReferenceTransform(yawTransform);
            }

            SetupTweakables();

            if (!string.IsNullOrEmpty(audioPath) && (yawSpeedDPS != 0 || pitchSpeedDPS != 0))
            {
                soundClip = SoundUtils.GetAudioClip(audioPath);

                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = soundClip;
                audioSource.loop = true;
                audioSource.dopplerLevel = 0;
                audioSource.minDistance = .5f;
                audioSource.maxDistance = 150;
                audioSource.Play();
                audioSource.volume = 0;
                audioSource.pitch = 0;
                audioSource.priority = 9999;
                audioSource.spatialBlend = 1;

                lastTurretDirection = yawTransform.parent.InverseTransformDirection(pitchTransform.forward);

                maxAudioRotRate = Mathf.Min(yawSpeedDPS, pitchSpeedDPS);

                hasAudio = true;
            }
        }

        void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (hasAudio)
                {
                    audioRotationRate = Mathf.Lerp(audioRotationRate, targetAudioRotationRate, 20 * Time.fixedDeltaTime);
                    audioRotationRate = Mathf.Clamp01(audioRotationRate);

                    if (audioRotationRate < 0.05f)
                    {
                        audioSource.volume = 0;
                    }
                    else
                    {
                        audioSource.volume = Mathf.Clamp(2f * audioRotationRate,
                            minVolume * BDArmorySettings.BDARMORY_WEAPONS_VOLUME,
                            maxVolume * BDArmorySettings.BDARMORY_WEAPONS_VOLUME);
                        audioSource.pitch = Mathf.Clamp(audioRotationRate, minAudioPitch, maxAudioPitch);
                    }

                    Vector3 tDir = yawTransform.parent.InverseTransformDirection(pitchTransform.forward);
                    float angle = Vector3.Angle(tDir, lastTurretDirection);
                    float rate = Mathf.Clamp01((angle / Time.fixedDeltaTime) / maxAudioRotRate);
                    lastTurretDirection = tDir;

                    targetAudioRotationRate = rate;
                }
            }
        }

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (hasAudio)
                {
                    if (!BDArmorySetup.GameIsPaused && audioRotationRate > 0.05f)
                    {
                        if (!audioSource.isPlaying) audioSource.Play();
                    }
                    else
                    {
                        if (audioSource.isPlaying)
                        {
                            audioSource.Stop();
                        }
                    }
                }
            }
        }

        void OnDestroy()
        {
            GameEvents.onEditorPartPlaced.Remove(OnEditorPartPlaced);
        }

        public void AimToTarget(Vector3 targetPosition, bool pitch = true, bool yaw = true)
        {
            AimInDirection(targetPosition - referenceTransform.position, pitch, yaw);
        }

        public void AimInDirection(Vector3 targetDirection, bool pitch = true, bool yaw = true)
        {
            if (!yawTransform)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;

            Vector3 yawNormal = yawTransform.up;
            Vector3 yawComponent = targetDirection.ProjectOnPlanePreNormalized(yawNormal);
            Vector3 pitchComponent = targetDirection.ProjectOnPlane(Vector3.Cross(yawComponent, yawNormal));

            float currentYaw = yawTransform.localEulerAngles.y.ToAngle();
            float yawError = VectorUtils.SignedAngleDP(
                referenceTransform.forward.ProjectOnPlanePreNormalized(yawNormal),
                yawComponent,
                Vector3.Cross(yawNormal, referenceTransform.forward));
            float yawOffset = Mathf.Abs(yawError);
            float targetYawAngle = (currentYaw + yawError).ToAngle();
            // clamp target yaw in a non-wobbly way
            if (Mathf.Abs(targetYawAngle) > yawRange / 2)
            {
                var nonWooblyWay = Vector3.Dot(yawTransform.parent.right, targetDirection + referenceTransform.position - yawTransform.position);
                if (float.IsNaN(nonWooblyWay)) return;

                targetYawAngle = yawRange / 2 * Math.Sign(nonWooblyWay);
            }


            float pitchError = (float)Vector3d.Angle(pitchComponent, yawNormal) - (float)Vector3d.Angle(referenceTransform.forward, yawNormal);
            float currentPitch = -pitchTransform.localEulerAngles.x.ToAngle(); // from current rotation transform
            float targetPitchAngle = currentPitch - pitchError;
            float pitchOffset = Mathf.Abs(targetPitchAngle - currentPitch);
            targetPitchAngle = Mathf.Clamp(targetPitchAngle, minPitch, maxPitch); // clamp pitch

            float linPitchMult = yawOffset > 0 ? Mathf.Clamp01((pitchOffset / yawOffset) * (yawSpeedDPS / pitchSpeedDPS)) : 1;
            float linYawMult = pitchOffset > 0 ? Mathf.Clamp01((yawOffset / pitchOffset) * (pitchSpeedDPS / yawSpeedDPS)) : 1;

            float yawSpeed;
            float pitchSpeed;
            if (smoothRotation)
            {
                yawSpeed = Mathf.Clamp(yawOffset * smoothMultiplier, 1f, yawSpeedDPS) * deltaTime;
                pitchSpeed = Mathf.Clamp(pitchOffset * smoothMultiplier, 1f, pitchSpeedDPS) * deltaTime;
            }
            else
            {
                yawSpeed = yawSpeedDPS * deltaTime;
                pitchSpeed = pitchSpeedDPS * deltaTime;
            }

            yawSpeed *= linYawMult;
            pitchSpeed *= linPitchMult;

            if (yawRange < 360 && Mathf.Abs(currentYaw - targetYawAngle) >= 180)
            {
                if (float.IsNaN(currentYaw))
                {
                    return;
                }

                targetYawAngle = currentYaw - (Math.Sign(currentYaw) * 179);
            }

            if (yaw)
                yawTransform.localRotation = Quaternion.RotateTowards(yawTransform.localRotation,
                    Quaternion.Euler(0, targetYawAngle, 0), yawSpeed);
            if (pitch)
                pitchTransform.localRotation = Quaternion.RotateTowards(pitchTransform.localRotation,
                    Quaternion.Euler(-targetPitchAngle, 0, 0), pitchSpeed);
        }

        public float Pitch => -pitchTransform.localEulerAngles.x.ToAngle();
        public float Yaw => yawTransform.localEulerAngles.y.ToAngle();

        public bool ReturnTurret()
        {
            if (!yawTransform)
            {
                return false;
            }

            float deltaTime = Time.fixedDeltaTime;

            float yawOffset = Quaternion.Angle(yawTransform.localRotation, standbyLocalRotation);
            float pitchOffset = Vector3.Angle(pitchTransform.forward, yawTransform.forward);

            float yawSpeed;
            float pitchSpeed;

            if (smoothRotation)
            {
                yawSpeed = Mathf.Clamp(yawOffset * smoothMultiplier, 1f, yawSpeedDPS) * deltaTime;
                pitchSpeed = Mathf.Clamp(pitchOffset * smoothMultiplier, 1f, pitchSpeedDPS) * deltaTime;
            }
            else
            {
                yawSpeed = yawSpeedDPS * deltaTime;
                pitchSpeed = pitchSpeedDPS * deltaTime;
            }

            float linPitchMult = yawOffset > 0 ? Mathf.Clamp01((pitchOffset / yawOffset) * (yawSpeedDPS / pitchSpeedDPS)) : 1;
            float linYawMult = pitchOffset > 0 ? Mathf.Clamp01((yawOffset / pitchOffset) * (pitchSpeedDPS / yawSpeedDPS)) : 1;

            yawSpeed *= linYawMult;
            pitchSpeed *= linPitchMult;

            yawTransform.localRotation = Quaternion.RotateTowards(yawTransform.localRotation, standbyLocalRotation, yawSpeed);
            pitchTransform.localRotation = Quaternion.RotateTowards(pitchTransform.localRotation, Quaternion.identity, pitchSpeed);

            if (yawTransform.localRotation == standbyLocalRotation && pitchTransform.localRotation == Quaternion.identity)
            {
                return true;
            }
            return false;
        }

        public bool TargetInRange(Vector3 targetPosition, float thresholdDegrees, float maxDistance)
        {
            if (!pitchTransform)
            {
                return false;
            }
            bool withinView = Vector3.Angle(targetPosition - pitchTransform.position, pitchTransform.forward) < thresholdDegrees;
            bool withinDistance = (targetPosition - pitchTransform.position).sqrMagnitude < maxDistance * maxDistance;
            return (withinView && withinDistance);
        }

        public void SetReferenceTransform(Transform t)
        {
            referenceTransform = t;
        }

        void SetupTweakables()
        {
            UI_FloatRange minPitchRange = (UI_FloatRange)Fields["minPitch"].uiControlEditor;
            if (minPitchLimit > 90)
            {
                minPitchLimit = minPitch;
            }
            if (minPitchLimit == 0)
            {
                Fields["minPitch"].guiActiveEditor = false;
            }
            minPitchRange.minValue = minPitchLimit;
            minPitchRange.maxValue = 0;
            if (minPitchLimit != 0)
                minPitchRange.stepIncrement = Mathf.Pow(10, Mathf.Min(1f, Mathf.Floor(Mathf.Log10(Mathf.Abs(minPitchLimit)) + (1 - Mathf.Log10(20f) - 1e-4f)))) / 10f; // Use between 20 and 200 divisions

            UI_FloatRange maxPitchRange = (UI_FloatRange)Fields["maxPitch"].uiControlEditor;
            if (maxPitchLimit > 90)
            {
                maxPitchLimit = maxPitch;
            }
            if (maxPitchLimit == 0)
            {
                Fields["maxPitch"].guiActiveEditor = false;
            }
            maxPitchRange.maxValue = maxPitchLimit;
            maxPitchRange.minValue = 0;
            if (maxPitchLimit != 0)
                maxPitchRange.stepIncrement = Mathf.Pow(10, Mathf.Min(1f, Mathf.Floor(Mathf.Log10(Mathf.Abs(maxPitchLimit)) + (1 - Mathf.Log10(20f) - 1e-4f)))) / 10f; // Use between 20 and 200 divisions

            UI_FloatRange yawRangeEd = (UI_FloatRange)Fields["yawRange"].uiControlEditor;
            if (yawRangeLimit > 360)
            {
                yawRangeLimit = yawRange;
            }

            if (yawRangeLimit == 0)
            {
                Fields["yawRange"].guiActiveEditor = false;
            }
            else if (yawRangeLimit < 0)
            {
                yawRangeEd.minValue = 0;
                yawRangeEd.maxValue = 360;

                if (yawRange < 0) yawRange = 360;
            }
            else
            {
                yawRangeEd.minValue = 0;
                yawRangeEd.maxValue = yawRangeLimit;
            }
            if (yawRange != 0)
                yawRangeEd.stepIncrement = Mathf.Pow(10, Math.Min(1f, Mathf.Floor(Mathf.Log10(Mathf.Abs(yawRange)) + (1 - Mathf.Log10(20f) - 1e-4f)))) / 10f; // Use between 20 and 200 divisions

            yawRangeEd.onFieldChanged = SetupStandbyLocalRotation;
            SetupStandbyLocalRotation();
        }
        void SetupStandbyLocalRotation(BaseField field = null, object obj = null)
        {
            UI_FloatRange yawStandbyAngleEd = (UI_FloatRange)Fields["yawStandbyAngle"].uiControlEditor;
            yawStandbyAngleEd.minValue = -yawRange / 2f;
            yawStandbyAngleEd.maxValue = yawRange / 2f;
            yawStandbyAngle = Mathf.Clamp(yawStandbyAngle, yawStandbyAngleEd.minValue, yawStandbyAngleEd.maxValue);
            yawStandbyAngleEd.onFieldChanged = OnStandbyAngleChanged;
            GameEvents.onEditorPartPlaced.Add(OnEditorPartPlaced);
            SetStandbyAngle();
        }

        void OnEditorPartPlaced(Part p = null) { if (p == part) OnStandbyAngleChanged(); }

        void OnStandbyAngleChanged(BaseField field = null, object obj = null)
        {
            SetStandbyAngle();
            foreach (Part symmetryPart in part.symmetryCounterparts)
            {
                ModuleTurret symmetryTurret = symmetryPart.FindModuleImplementing<ModuleTurret>();
                if (part.symMethod == SymmetryMethod.Mirror)
                {
                    symmetryTurret.yawStandbyAngle = -yawStandbyAngle;
                }
                else
                {
                    symmetryTurret.yawStandbyAngle = yawStandbyAngle;
                }

                symmetryTurret.SetStandbyAngle();
            }
        }

        void SetStandbyAngle()
        {
            standbyLocalRotation = Quaternion.AngleAxis(yawStandbyAngle, Vector3.up);
            if (yawTransform != null) yawTransform.localRotation = standbyLocalRotation;
        }
    }
    public class BDAScaleByDistance : PartModule
    {
        /// <summary>
        /// Sibling Module to FXModuleLookAtConstraint, causes indicated mesh object to scale based on distance to target transform
        /// Module ported over to fix the spring on the M230Chaingun (no Stock equivalent), though I guess it could be used for other things as well
        /// </summary>
        [KSPField(isPersistant = false)]
        public string transformToScaleName;

        public Transform transformToScale;

        [KSPField(isPersistant = false)]
        public string scaleFactor = "0,0,1";
        Vector3 scaleFactorV;

        [KSPField(isPersistant = false)]
        public string distanceTransformName;

        public Transform distanceTransform;


        public override void OnStart(PartModule.StartState state)
        {
            ParseScale();
            transformToScale = part.FindModelTransform(transformToScaleName);
            distanceTransform = part.FindModelTransform(distanceTransformName);
        }

        public void Update()
        {
            Vector3 finalScaleFactor;
            float distance = Vector3.Distance(transformToScale.position, distanceTransform.position);
            float sfX = (scaleFactorV.x != 0) ? scaleFactorV.x * distance : 1;
            float sfY = (scaleFactorV.y != 0) ? scaleFactorV.y * distance : 1;
            float sfZ = (scaleFactorV.z != 0) ? scaleFactorV.z * distance : 1;
            finalScaleFactor = new Vector3(sfX, sfY, sfZ);

            transformToScale.localScale = finalScaleFactor;
        }



        void ParseScale()
        {
            string[] split = scaleFactor.Split(',');
            float[] splitF = new float[split.Length];
            for (int i = 0; i < split.Length; i++)
            {
                splitF[i] = float.Parse(split[i]);
            }
            scaleFactorV = new Vector3(splitF[0], splitF[1], splitF[2]);
        }

    }
}
