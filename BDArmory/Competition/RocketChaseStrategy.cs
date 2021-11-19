using System;
using System.Collections;
using UnityEngine;
namespace BDArmory.Competition
{
    public class RocketChaseStrategy : OrchestrationStrategy
    {
        private string rocketCraftFilename;

        public RocketChaseStrategy(string rocketCraftFilename)
        {
            this.rocketCraftFilename = rocketCraftFilename;
        }

        public IEnumerator Execute(BDAScoreClient client, BDAScoreService service)
        {
            yield return new WaitForSeconds(1.0f);

            // create a local file to store the scores until we're ready to report them
            ConfigNode scoreNode = new ConfigNode();

            // loop through all players. for each, do the following:
            // - spawn player
            // - spawn rocket
            // - launch rocket
            // - wait until rocket-player distance exceeds a threshold or rocket is destroyed

        }
    }
}
