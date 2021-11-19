using System;
using System.Collections;
using BDArmory.Control;

namespace BDArmory.Competition
{
    public interface SpawnStrategy
    {
        /// <summary>
        /// Part 1 of Remote Orchestration
        ///
        /// Spawns craft according to a provided strategy and prepares them for flight.
        /// </summary>
        /// <param name="spawner"></param>
        /// <returns></returns>
        public IEnumerator Spawn(VesselSpawner spawner);

        public bool DidComplete();
    }
}
