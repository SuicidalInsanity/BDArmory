using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using BDArmory.Control;
using BDArmory.GameModes;
using BDArmory.Modules;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons.Missiles;

namespace BDArmory.Competition.VesselSpawning
{
    public enum SpawnFailureReason { None, NoCraft, NoTerrain, InvalidVessel, VesselLostParts, VesselFailedToSpawn, TimedOut, Cancelled };

    public static class SpawnUtils
    {
        // Cancel all spawning modes.
        public static void CancelSpawning()
        {
            VesselSpawnerStatus.spawnFailureReason = SpawnFailureReason.Cancelled;

            // Single spawn
            if (CircularSpawning.Instance.vesselsSpawning || CircularSpawning.Instance.vesselsSpawningOnceContinuously)
            { CircularSpawning.Instance.CancelSpawning(); }

            // Continuous spawn
            if (ContinuousSpawning.Instance.vesselsSpawningContinuously)
            { ContinuousSpawning.Instance.CancelSpawning(); }

            SpawnUtils.RevertSpawnLocationCamera(true);
        }

        /// <summary>
        /// If the VESSELNAMING tag exists in the craft file, then KSP renames the vessel at some point after spawning.
        /// This function checks for renamed vessels and sets the name back to what it was.
        /// This must be called once after a yield, before using vessel.vesselName as an index in spawnedVessels.Keys.
        /// </summary>
        /// <param name="vessels"></param>
        public static void CheckForRenamedVessels(Dictionary<string, Vessel> vessels)
        {
            foreach (var vesselName in vessels.Keys.ToList())
            {
                if (vesselName != vessels[vesselName].vesselName)
                {
                    vessels[vesselName].vesselName = vesselName;
                }
            }
        }

        public static int PartCount(Vessel vessel, bool ignoreEVA = true)
        {
            if (!ignoreEVA) return vessel.parts.Count;
            int count = 0;
            using (var part = vessel.parts.GetEnumerator())
                while (part.MoveNext())
                {
                    if (part.Current == null) continue;
                    if (ignoreEVA && part.Current.isKerbalEVA()) continue; // Ignore EVA kerbals, which get added at some point after spawning.
                    ++count;
                }
            return count;
        }

        #region Camera
        public static void ShowSpawnPoint(int worldIndex, double latitude, double longitude, double altitude = 0, float distance = 100, bool spawning = false) => SpawnUtilsInstance.Instance.ShowSpawnPoint(worldIndex, latitude, longitude, altitude, distance, spawning); // Note: this may launch a coroutine when not spawning and there's no active vessel!
        public static void RevertSpawnLocationCamera(bool keepTransformValues = true) => SpawnUtilsInstance.Instance.RevertSpawnLocationCamera(keepTransformValues);
        #endregion

        #region Teams
        public static Dictionary<string, string> originalTeams = new Dictionary<string, string>();
        public static void SaveTeams()
        {
            originalTeams.Clear();
            foreach (var weaponManager in LoadedVesselSwitcher.Instance.WeaponManagers.SelectMany(tm => tm.Value).ToList())
            {
                originalTeams[weaponManager.vessel.vesselName] = weaponManager.Team.Name;
            }
        }
        #endregion

        #region Engine Activation
        public static int CountActiveEngines(Vessel vessel)
        {
            return VesselModuleRegistry.GetModules<ModuleEngines>(vessel).Where(engine => engine.EngineIgnited).ToList().Count + FireSpitter.CountActiveEngines(vessel);
        }

        public static void ActivateAllEngines(Vessel vessel, bool activate = true, bool ignoreModularMissileEngines = true)
        {
            foreach (var engine in VesselModuleRegistry.GetModules<ModuleEngines>(vessel))
            {
                if (ignoreModularMissileEngines && IsModularMissileEngine(engine)) continue; // Ignore modular missile engines.
                var mme = engine.part.FindModuleImplementing<MultiModeEngine>();
                if (mme == null)
                {
                    if (activate) engine.Activate();
                    else engine.Shutdown();
                }
                else
                {
                    if (mme.runningPrimary)
                    {
                        if (activate && !mme.PrimaryEngine.EngineIgnited)
                        {
                            mme.PrimaryEngine.Activate();
                        }
                        else if (!activate && mme.PrimaryEngine.EngineIgnited)
                        {
                            mme.PrimaryEngine.Shutdown();
                        }
                    }
                    else
                    {
                        if (activate && !mme.SecondaryEngine.EngineIgnited)
                        {
                            mme.SecondaryEngine.Activate();
                        }
                        else if (!activate && mme.SecondaryEngine.EngineIgnited)
                        {
                            mme.SecondaryEngine.Shutdown();
                        }
                    }
                }
            }
            FireSpitter.ActivateFSEngines(vessel, activate);
        }

        public static bool IsModularMissileEngine(ModuleEngines engine)
        {
            var part = engine.part;
            if (part is not null)
            {
                var firstDecoupler = BDModularGuidance.FindFirstDecoupler(part.parent, null);
                if (firstDecoupler is not null && HasMMGInChildren(firstDecoupler.part)) return true;
            }
            return false;
        }
        static bool HasMMGInChildren(Part part)
        {
            if (part is null) return false;
            if (part.FindModuleImplementing<BDModularGuidance>() is not null) return true;
            foreach (var child in part.children)
                if (HasMMGInChildren(child)) return true;
            return false;
        }
        #endregion

        #region Intake hacks
        public static void HackIntakesOnNewVessels(bool enable)
        {
            if (enable)
            {
                GameEvents.onVesselLoaded.Add(HackIntakesEventHandler);
                GameEvents.OnVesselRollout.Add(HackIntakes);
            }
            else
            {
                GameEvents.onVesselLoaded.Remove(HackIntakesEventHandler);
                GameEvents.OnVesselRollout.Remove(HackIntakes);
            }
        }
        static void HackIntakesEventHandler(Vessel vessel) => HackIntakes(vessel, true);

        public static void HackIntakes(Vessel vessel, bool enable)
        {
            if (vessel == null || !vessel.loaded) return;
            if (enable)
            {
                foreach (var intake in VesselModuleRegistry.GetModules<ModuleResourceIntake>(vessel))
                    intake.checkForOxygen = false;
            }
            else
            {
                foreach (var intake in VesselModuleRegistry.GetModules<ModuleResourceIntake>(vessel))
                {
                    var checkForOxygen = ConfigNodeUtils.FindPartModuleConfigNodeValue(intake.part.partInfo.partConfig, "ModuleResourceIntake", "checkForOxygen");
                    if (!string.IsNullOrEmpty(checkForOxygen)) // Use the default value from the part.
                    {
                        try
                        {
                            intake.checkForOxygen = bool.Parse(checkForOxygen);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[BDArmory.BDArmorySetup]: Failed to parse checkForOxygen configNode of {intake.name}: {e.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[BDArmory.BDArmorySetup]: No default value for checkForOxygen found in partConfig for {intake.name}, defaulting to true.");
                        intake.checkForOxygen = true;
                    }
                }
            }
        }
        public static void HackIntakes(ShipConstruct ship) // This version only needs to enable the hack.
        {
            if (ship == null) return;
            foreach (var part in ship.Parts)
            {
                var intakes = part.FindModulesImplementing<ModuleResourceIntake>();
                if (intakes.Count() > 0)
                {
                    foreach (var intake in intakes)
                        intake.checkForOxygen = false;
                }
            }
        }
        #endregion

        #region Vessel Removal
        public static bool removingVessels => SpawnUtilsInstance.Instance.removeVesselsPending > 0;
        public static void RemoveVessel(Vessel vessel) => SpawnUtilsInstance.Instance.RemoveVessel(vessel);
        public static IEnumerator RemoveAllVessels() => SpawnUtilsInstance.Instance.RemoveAllVessels();
        #endregion

        #region AI/WM stuff for RWP
        public static bool CheckAIWMPlacement(Vessel vessel)
        {
            var message = "";
            List<string> failureStrings = new List<string>();
            var AI = VesselModuleRegistry.GetBDModulePilotAI(vessel, true);
            var WM = VesselModuleRegistry.GetMissileFire(vessel, true);
            if (AI == null) message = " has no AI";
            if (WM == null) message += (AI == null ? " or WM" : " has no WM");
            if (AI != null || WM != null)
            {
                int count = 0;
                if (AI != null && (AI.part.parent == null || AI.part.vessel.rootPart != AI.part.parent))
                {
                    message += (WM == null ? " and its AI" : "'s AI");
                    ++count;
                }
                if (WM != null && (WM.part.parent == null || WM.part.vessel.rootPart != WM.part.parent))
                {
                    message += (AI == null ? " and its WM" : (count > 0 ? " and WM" : "'s WM"));
                    ++count;
                };
                if (count > 0) message += (count > 1 ? " are" : " is") + " not attached to its root part";
            }

            if (!string.IsNullOrEmpty(message))
            {
                message = $"{vessel.vesselName}" + message + ".";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.LogWarning("[BDArmory.SpawnUtils]: " + message);
                return false;
            }
            return true;
        }

        public static void CheckAIWMCounts(Vessel vessel)
        {
            var numberOfAIs = VesselModuleRegistry.GetModuleCount<BDModulePilotAI>(vessel);
            var numberOfWMs = VesselModuleRegistry.GetModuleCount<MissileFire>(vessel);
            string message = null;
            if (numberOfAIs != 1 && numberOfWMs != 1) message = $"{vessel.vesselName} has {numberOfAIs} AIs and {numberOfWMs} WMs";
            else if (numberOfAIs != 1) message = $"{vessel.vesselName} has {numberOfAIs} AIs";
            else if (numberOfWMs != 1) message = $"{vessel.vesselName} has {numberOfWMs} WMs";
            if (message != null)
            {
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.LogWarning("[BDArmory.SpawnUtils]: " + message);
            }
        }
        #endregion
    }

    /// <summary>
    /// Non-static MonoBehaviour version to be able to call coroutines.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class SpawnUtilsInstance : MonoBehaviour
    {
        public static SpawnUtilsInstance Instance;

        void Awake()
        {
            if (Instance != null) Destroy(Instance);
            Instance = this;
            spawnLocationCamera = new GameObject("StationaryCameraParent");
            spawnLocationCamera = (GameObject)Instantiate(spawnLocationCamera, Vector3.zero, Quaternion.identity);
            spawnLocationCamera.SetActive(false);
            if (BDArmorySettings.HACK_INTAKES) SpawnUtils.HackIntakesOnNewVessels(true);
        }

        void OnDestroy()
        {
            VesselSpawnerField.Save();
            Destroy(spawnLocationCamera);
            SpawnUtils.HackIntakesOnNewVessels(false);
        }

        #region Vessel Removal
        public int removeVesselsPending = 0;
        // Remove a vessel and clean up any remaining parts. This fixes the case where the currently focussed vessel refuses to die properly.
        public void RemoveVessel(Vessel vessel)
        {
            if (vessel == null) return;
            if (BDArmorySettings.ASTEROID_RAIN && vessel.vesselType == VesselType.SpaceObject) return; // Don't remove asteroids we're using.
            if (BDArmorySettings.ASTEROID_FIELD && vessel.vesselType == VesselType.SpaceObject) return; // Don't remove asteroids we're using.
            StartCoroutine(RemoveVesselCoroutine(vessel));
        }
        public IEnumerator RemoveVesselCoroutine(Vessel vessel)
        {
            if (vessel == null) yield break;
            ++removeVesselsPending;
            if (vessel != FlightGlobals.ActiveVessel && vessel.vesselType != VesselType.SpaceObject)
            {
                if (KerbalSafetyManager.Instance.safetyLevel != KerbalSafetyLevel.Off)
                    KerbalSafetyManager.Instance.RecoverVesselNow(vessel);
                else
                    ShipConstruction.RecoverVesselFromFlight(vessel.protoVessel, HighLogic.CurrentGame.flightState, true);
            }
            else
            {
                if (vessel.vesselType == VesselType.SpaceObject)
                {
                    if (BDArmorySettings.ASTEROID_RAIN && AsteroidRain.IsManagedAsteroid(vessel)) yield break; // Don't remove asteroids when we're using them.
                    if (BDArmorySettings.ASTEROID_FIELD && AsteroidField.IsManagedAsteroid(vessel)) yield break; // Don't remove asteroids when we're using them.
                    var cometVessel = vessel.FindVesselModuleImplementing<CometVessel>();
                    if (cometVessel) { Destroy(cometVessel); }
                }
                vessel.Die(); // Kill the vessel
                yield return waitForFixedUpdate;
                if (vessel != null)
                {
                    var partsToKill = vessel.parts.ToList(); // If it left any parts, kill them. (This occurs when the currently focussed vessel gets killed.)
                    foreach (var part in partsToKill)
                        part.Die();
                }
                yield return waitForFixedUpdate;
            }
            --removeVesselsPending;
        }

        /// <summary>
        /// Remove all the vessels.
        /// This works by spawning in a spawnprobe at the current camera coordinates so that we can clean up the other vessels properly.
        /// </summary>
        /// <returns></returns>
        public IEnumerator RemoveAllVessels()
        {
            var vesselsToKill = FlightGlobals.Vessels.ToList();
            // Spawn in the SpawnProbe at the camera position.
            var spawnProbe = VesselSpawner.SpawnSpawnProbe();
            if (spawnProbe != null) // If the spawnProbe is null, then just try to kill everything anyway.
            {
                spawnProbe.Landed = false; // Tell KSP that it's not landed so KSP doesn't mess with its position.
                yield return new WaitWhile(() => spawnProbe != null && (!spawnProbe.loaded || spawnProbe.packed));
                // Switch to the spawn probe.
                while (spawnProbe != null && FlightGlobals.ActiveVessel != spawnProbe)
                {
                    LoadedVesselSwitcher.Instance.ForceSwitchVessel(spawnProbe);
                    yield return waitForFixedUpdate;
                }
            }
            // Kill all other vessels (including debris).
            foreach (var vessel in vesselsToKill)
                RemoveVessel(vessel);
            // Finally, remove the SpawnProbe.
            RemoveVessel(spawnProbe);

            // Now, clear the teams and wait for everything to be removed.
            SpawnUtils.originalTeams.Clear();
            yield return new WaitWhile(() => removeVesselsPending > 0);
        }
        #endregion

        #region Camera Adjustment
        GameObject spawnLocationCamera;
        Transform originalCameraParentTransform;
        float originalCameraNearClipPlane;
        Coroutine delayedShowSpawnPointCoroutine;
        private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
        /// <summary>
        /// Show the spawn point.
        /// Note: When not spawning and there's no active vessel, this may launch a coroutine to perform the actual shift.
        /// Note: If spawning is true, then the spawnLocationCamera takes over the camera and RevertSpawnLocationCamera should be called at some point to allow KSP to do its own camera stuff.
        /// </summary>
        /// <param name="worldIndex">The body the spawn point is on.</param>
        /// <param name="latitude">Latitude</param>
        /// <param name="longitude">Longitude</param>
        /// <param name="altitude">Altitude</param>
        /// <param name="distance">Distance to view the point from.</param>
        /// <param name="spawning">Whether spawning is actually happening.</param>
        /// <param name="recurse">State parameter for when we need to spawn a probe first.</param>
        public void ShowSpawnPoint(int worldIndex, double latitude, double longitude, double altitude = 0, float distance = 100, bool spawning = false, bool recurse = true)
        {
            if (BDArmorySettings.ASTEROID_RAIN) { AsteroidRain.Instance.Reset(); }
            if (BDArmorySettings.ASTEROID_FIELD) { AsteroidField.Instance.Reset(); }
            if (!spawning && (FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.state == Vessel.State.DEAD))
            {
                if (!recurse)
                {
                    Debug.LogWarning($"[BDArmory.SpawnUtils]: No active vessel, unable to show spawn point.");
                    return;
                }
                Debug.LogWarning($"[BDArmory.SpawnUtils]: Active vessel is dead or packed, spawning a new one.");
                if (delayedShowSpawnPointCoroutine != null) { StopCoroutine(delayedShowSpawnPointCoroutine); delayedShowSpawnPointCoroutine = null; }
                delayedShowSpawnPointCoroutine = StartCoroutine(DelayedShowSpawnPoint(worldIndex, latitude, longitude, altitude, distance, spawning));
                return;
            }
            if (!spawning)
            {
                var overLand = (worldIndex != -1 ? FlightGlobals.Bodies[worldIndex] : FlightGlobals.currentMainBody).TerrainAltitude(latitude, longitude) > 0;
                FlightGlobals.fetch.SetVesselPosition(worldIndex != -1 ? worldIndex : FlightGlobals.currentMainBody.flightGlobalsIndex, latitude, longitude, overLand ? Math.Max(5, altitude) : altitude, FlightGlobals.ActiveVessel.vesselType == VesselType.Plane ? 0 : 90, 0, true, overLand); // FIXME This should be using the vessel reference transform to determine the inclination. Also below.
                var flightCamera = FlightCamera.fetch;
                flightCamera.SetDistance(distance);
                var radialUnitVector = (flightCamera.transform.parent.position - FlightGlobals.currentMainBody.transform.position).normalized;
                flightCamera.transform.parent.rotation = Quaternion.LookRotation(flightCamera.transform.parent.forward, radialUnitVector);
                FloatingOrigin.SetOffset(FlightGlobals.ActiveVessel.transform.position); // This adjusts local coordinates, such that the vessel position is (0,0,0).
                VehiclePhysics.Gravity.Refresh();
            }
            else
            {
                FlightGlobals.fetch.SetVesselPosition(worldIndex != -1 ? worldIndex : FlightGlobals.currentMainBody.flightGlobalsIndex, latitude, longitude, altitude, 0, 0, true);
                var terrainAltitude = FlightGlobals.currentMainBody.TerrainAltitude(latitude, longitude);
                var spawnPoint = FlightGlobals.currentMainBody.GetWorldSurfacePosition(latitude, longitude, terrainAltitude + altitude);
                FloatingOrigin.SetOffset(spawnPoint); // This adjusts local coordinates, such that spawnPoint is (0,0,0).
                var radialUnitVector = -FlightGlobals.currentMainBody.transform.position.normalized;
                var refDirection = Math.Abs(Vector3.Dot(Vector3.up, radialUnitVector)) < 0.71f ? Vector3.up : Vector3.forward; // Avoid that the reference direction is colinear with the local surface normal.
                var flightCamera = FlightCamera.fetch;
                var cameraPosition = Vector3.RotateTowards(distance * radialUnitVector, Vector3.Cross(radialUnitVector, refDirection), 70f * Mathf.Deg2Rad, 0);
                if (!spawnLocationCamera.activeSelf)
                {
                    spawnLocationCamera.SetActive(true);
                    originalCameraParentTransform = flightCamera.transform.parent;
                    originalCameraNearClipPlane = GUIUtils.GetMainCamera().nearClipPlane;
                }
                spawnLocationCamera.transform.position = Vector3.zero;
                spawnLocationCamera.transform.rotation = Quaternion.LookRotation(-cameraPosition, radialUnitVector);
                flightCamera.transform.parent = spawnLocationCamera.transform;
                flightCamera.SetTarget(spawnLocationCamera.transform);
                flightCamera.transform.localPosition = cameraPosition;
                flightCamera.transform.localRotation = Quaternion.identity;
                flightCamera.SetDistance(distance);
            }
        }

        IEnumerator DelayedShowSpawnPoint(int worldIndex, double latitude, double longitude, double altitude = 0, float distance = 100, bool spawning = false)
        {
            Vessel spawnProbe = VesselSpawner.SpawnSpawnProbe();
            if (spawnProbe != null)
            {
                spawnProbe.Landed = false;
                yield return new WaitWhile(() => spawnProbe != null && (!spawnProbe.loaded || spawnProbe.packed));
                FlightGlobals.ForceSetActiveVessel(spawnProbe);
                while (spawnProbe != null && FlightGlobals.ActiveVessel != spawnProbe)
                {
                    spawnProbe.SetWorldVelocity(Vector3d.zero);
                    LoadedVesselSwitcher.Instance.ForceSwitchVessel(spawnProbe);
                    yield return waitForFixedUpdate;
                }
            }
            ShowSpawnPoint(worldIndex, latitude, longitude, altitude, distance, spawning, false);
        }

        public void RevertSpawnLocationCamera(bool keepTransformValues = true)
        {
            if (spawnLocationCamera == null || !spawnLocationCamera.activeSelf) return;
            if (delayedShowSpawnPointCoroutine != null) { StopCoroutine(delayedShowSpawnPointCoroutine); delayedShowSpawnPointCoroutine = null; }
            var flightCamera = FlightCamera.fetch;
            if (originalCameraParentTransform != null)
            {
                if (keepTransformValues && flightCamera.transform != null && flightCamera.transform.parent != null)
                {
                    originalCameraParentTransform.position = flightCamera.transform.parent.position;
                    originalCameraParentTransform.rotation = flightCamera.transform.parent.rotation;
                    originalCameraNearClipPlane = GUIUtils.GetMainCamera().nearClipPlane;
                }
                flightCamera.transform.parent = originalCameraParentTransform;
                GUIUtils.GetMainCamera().nearClipPlane = originalCameraNearClipPlane;
            }
            if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.state != Vessel.State.DEAD)
                LoadedVesselSwitcher.Instance.ForceSwitchVessel(FlightGlobals.ActiveVessel); // Update the camera.
            spawnLocationCamera.SetActive(false);
        }
        #endregion
    }
}