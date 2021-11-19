using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BDArmory.Control;
using BDArmory.Core;
using UnityEngine;
using static BDArmory.Competition.WaypointFollowingStrategy;

namespace BDArmory.Competition
{
    public class TournamentCoordinator
    {
        private SpawnStrategy spawner;
        private OrchestrationStrategy orchestrator;

        public TournamentCoordinator(SpawnStrategy spawner, OrchestrationStrategy orchestrator)
        {
            this.spawner = spawner;
            this.orchestrator = orchestrator;
        }

        public IEnumerator Execute()
        {
            // first, spawn vessels
            yield return spawner.Spawn(VesselSpawner.Instance);

            if( !spawner.DidComplete() )
            {
                Debug.Log("[BDArmory.BDAScoreService] TournamentCoordinator spawn failed");
                yield break;
            }

            // now, hand off to orchestrator
            yield return orchestrator.Execute(BDAScoreService.Instance.client, BDAScoreService.Instance);
        }

        public static TournamentCoordinator BuildFromDescriptor(CompetitionModel competitionModel)
        {
            switch(competitionModel.mode)
            {
                case "ffa":
                    return BuildFFA();
                case "path":
                    return BuildShortCanyonWaypoint();
                case "chase":
                    return BuildChase();
            }
            return null;
        }

        private static TournamentCoordinator BuildFFA()
        {
            var scoreService = BDAScoreService.Instance;
            var scoreClient = scoreService.client;
            var vesselRegistry = scoreClient.vessels;
            var activeVesselModels = scoreClient.activeVessels.ToList().Select(e => vesselRegistry[e]);
            var activeVesselIds = scoreClient.activeVessels.ToList();
            var craftUrls = activeVesselModels.Select(e => e.craft_url);
            // TODO: need coords from descriptor, or fallback to local settings
            var latitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
            var longitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
            var altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
            var spawnRadius = BDArmorySettings.VESSEL_SPAWN_DISTANCE;
            var spawnStrategy = new CircularSpawnStrategy(scoreClient.AsVesselSource(), activeVesselIds, latitude, longitude, altitude, spawnRadius);
            var orchestrationStrategy = new RankedFreeForAllStrategy();
            return new TournamentCoordinator(spawnStrategy, orchestrationStrategy);
        }

        private static TournamentCoordinator BuildShortCanyonWaypoint()
        {
            var scoreService = BDAScoreService.Instance;
            var scoreClient = scoreService.client;
            var vesselSource = scoreClient.AsVesselSource();
            var vesselRegistry = scoreClient.vessels;
            var activeVesselModels = scoreClient.activeVessels.ToList().Select(e => vesselRegistry[e]);
            var craftUrl = activeVesselModels.Select(e => vesselSource.GetLocalPath(e.id)).First();
            // TODO: need coords from descriptor, or fallback to local settings
            //var latitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
            //var longitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
            var latitude = 28.3f;
            var longitude = -39.2f;
            var altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
            var spawnRadius = BDArmorySettings.VESSEL_SPAWN_DISTANCE;
            var spawnStrategy = new PointSpawnStrategy(craftUrl, latitude, longitude, 2*altitude, 315.0f);
            // kerbin-canyon2
            // 28.33,-39.11
            // 28.83,-38.06
            // 29.54,-38.68
            // 30.15,-38.6
            // 30.83,-38.87
            // 30.73,-39.6
            // 30.9,-40.23
            // 30.83,-41.26
            var waypoints = new List<Waypoint> {
                new Waypoint(28.33f, -39.11f, altitude),
                new Waypoint(28.83f, -38.06f, altitude),
                new Waypoint(29.54f, -38.68f, altitude),
                new Waypoint(30.15f, -38.6f, altitude),
                new Waypoint(30.83f, -38.87f, altitude),
                new Waypoint(30.73f, -39.6f, altitude),
                new Waypoint(30.9f, -40.23f, altitude),
                new Waypoint(30.83f, -41.26f, altitude),
            };
            var orchestrationStrategy = new WaypointFollowingStrategy(waypoints);
            return new TournamentCoordinator(spawnStrategy, orchestrationStrategy);
        }

        private static TournamentCoordinator BuildLongCanyonWaypoint()
        {
            var scoreService = BDAScoreService.Instance;
            var scoreClient = scoreService.client;
            var vesselSource = scoreClient.AsVesselSource();
            var vesselRegistry = scoreClient.vessels;
            var activeVesselModels = scoreClient.activeVessels.ToList().Select(e => vesselRegistry[e]);
            var craftUrl = activeVesselModels.Select(e => vesselSource.GetLocalPath(e.id)).First();
            // TODO: need coords from descriptor, or fallback to local settings
            //var latitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x;
            //var longitude = BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y;
            var latitude = 23.0f;
            var longitude = -40.1f;
            var altitude = BDArmorySettings.VESSEL_SPAWN_ALTITUDE;
            var spawnRadius = BDArmorySettings.VESSEL_SPAWN_DISTANCE;
            var spawnStrategy = new PointSpawnStrategy(craftUrl, latitude, longitude, altitude, 315.0f);
            // kerbin-canyon1
            // 23.3,-40.0
            // 24.47,-40.46
            // 24.95,-40.88
            // 25.91,-41.4
            // 26.23,-41.11
            // 26.8,-40.16
            // 27.05,-39.85
            // 27.15,-39.67
            // 27.58,-39.4
            // 28.33,-39.11
            // 28.83,-38.06
            // 29.54,-38.68
            // 30.15,-38.6
            // 30.83,-38.87
            // 30.73,-39.6
            // 30.9,-40.23
            // 30.83,-41.26
            var waypoints = new List<Waypoint> {
                new Waypoint(23.2f, -40.0f, altitude),
                new Waypoint(24.47f, -40.46f, altitude),
                new Waypoint(24.95f, -40.88f, altitude),
                new Waypoint(25.91f, -41.4f, altitude),
                new Waypoint(26.23f, -41.11f, altitude),
                new Waypoint(26.8f, -40.16f, altitude),
                new Waypoint(27.05f, -39.85f, altitude),
                new Waypoint(27.15f, -39.67f, altitude),
                new Waypoint(27.58f, -39.4f, altitude),
                new Waypoint(28.33f, -39.11f, altitude),
                new Waypoint(28.83f, -38.06f, altitude),
                new Waypoint(29.54f, -38.68f, altitude),
                new Waypoint(30.15f, -38.6f, altitude),
                new Waypoint(30.83f, -38.87f, altitude),
                new Waypoint(30.73f, -39.6f, altitude),
                new Waypoint(30.9f, -40.23f, altitude),
                new Waypoint(30.83f, -41.26f, altitude),
            };
            var orchestrationStrategy = new WaypointFollowingStrategy(waypoints);
            return new TournamentCoordinator(spawnStrategy, orchestrationStrategy);
        }

        private static TournamentCoordinator BuildChase()
        {
            return null;
        }
    }

}
