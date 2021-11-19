using System;
using System.Collections;
using BDArmory.Control;
using UnityEngine;

namespace BDArmory.Competition
{
    public class PointSpawnStrategy : SpawnStrategy
    {
        private string craftUrl;
        private double latitude, longitude, altitude;
        private float heading, pitch;
        private bool success = false;

        public PointSpawnStrategy(string craftUrl, double latitude, double longitude, double altitude, float heading, float pitch = -0.7f)
        {
            this.craftUrl = craftUrl;
            this.latitude = latitude;
            this.longitude = longitude;
            this.altitude = altitude;
            this.heading = heading;
            this.pitch = pitch;
        }

        public IEnumerator Spawn(VesselSpawner spawner)
        {
            Debug.Log("[BDArmory.BDAScoreService] PointSpawnStrategy spawning.");

            // spawn the given craftUrl at the given location/heading/pitch
            yield return spawner.SpawnVessel(craftUrl, latitude, longitude, altitude, heading, pitch);

            // wait for spawner to finish
            while (spawner.vesselsSpawning)
            {
                yield return new WaitForFixedUpdate();
            }

            if (!spawner.vesselSpawnSuccess)
            {
                Debug.Log("[BDArmory.BDAScoreService] PointSpawnStrategy failed.");
                yield break;
            }

            success = true;
        }

        public bool DidComplete()
        {
            return success;
        }
    }
}
