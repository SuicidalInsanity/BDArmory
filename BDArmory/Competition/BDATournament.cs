using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

using BDArmory.Competition.OrchestrationStrategies;
using BDArmory.Evolution;
using BDArmory.GameModes.Waypoints;
using BDArmory.Settings;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.VesselSpawning.SpawnStrategies;
using BDArmory.VesselSpawning;

namespace BDArmory.Competition
{
    // A serializable configuration for loading and saving the tournament state.
    [Serializable]
    public class RoundConfig : CircularSpawnConfig
    {
        public RoundConfig(int round, int heat, bool completed, CircularSpawnConfig config) : base(config) { this.round = round; this.heat = heat; this.completed = completed; SerializeTeams(); }
        public int round;
        public int heat;
        public bool completed;
        [SerializeField] private string serializedTeams;
        public void SerializeTeams()
        {
            if (teamsSpecific == null)
            {
                serializedTeams = null;
                return;
            }
            var teamStrings = new List<string>();
            foreach (var team in teamsSpecific)
            {
                teamStrings.Add("[" + string.Join(",", team) + "]");
            }
            serializedTeams = "[" + string.Join(",", teamStrings) + "]";
            craftFiles = null; // Avoid including the file list twice in the tournament.state file.
        }
        public void DeserializeTeams()
        {
            if (teamsSpecific == null) teamsSpecific = new List<List<string>>();
            else teamsSpecific.Clear();
            if (!string.IsNullOrEmpty(serializedTeams))
            {
                var teams = serializedTeams.Substring(1, serializedTeams.Length - 2).Split(new string[] { "],[" }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim(new char[] { '[', ']' })).ToList();
                foreach (var team in teams)
                {
                    var files_tmp = team.Split(',').ToList();
                    // Fix for filenames with commas in them.
                    List<string> files = new List<string>();
                    string current_file = "";
                    foreach (var file in files_tmp)
                    {
                        if (string.IsNullOrEmpty(current_file))
                            current_file = file;
                        else
                            current_file += "," + file;
                        if (current_file.EndsWith(".craft"))
                        {
                            files.Add(current_file);
                            current_file = "";
                        }
                    }
                    if (files.Count > 0)
                        teamsSpecific.Add(files);
                }
            }
            if (teamsSpecific.Count == 0) teamsSpecific = null;
        }
    }

    public enum TournamentType { FFA, Teams };

    [Serializable]
    public class TournamentState
    {
        public static string defaultStateFile = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "BDArmory", "PluginData", "tournament.state"));
        public uint tournamentID;
        public string savegame;
        private List<string> craftFiles; // For FFA style tournaments.
        private List<List<string>> teamFiles; // For teams style tournaments.
        private List<List<string>> opponentTeamFiles; // For gauntlet style tournaments.
        public int vesselCount;
        public int teamCount;
        public int teamsPerHeat;
        public int vesselsPerTeam;
        public bool fullTeams;
        public TournamentType tournamentType = TournamentType.FFA;
        [NonSerialized] public Dictionary<int, Dictionary<int, CircularSpawnConfig>> rounds; // <Round, <Heat, CircularSpawnConfig>>
        [NonSerialized] public Dictionary<int, HashSet<int>> completed = new Dictionary<int, HashSet<int>>();
        [NonSerialized] private List<Queue<string>> teamSpawnQueues = new List<Queue<string>>();
        [NonSerialized] private List<Queue<string>> opponentTeamSpawnQueues = new List<Queue<string>>();
        private string message;

        /* Generate rounds and heats by shuffling the crafts list and breaking it into groups.
         * The last heat in a round will have fewer craft if the number of craft is not divisible by the number of vessels per heat.
         * The vessels per heat is limited to the number of available craft.
         */
        public bool Generate(string folder, int numberOfRounds, int vesselsPerHeat, int tournamentStyle)
        {
            folder ??= ""; // Sanitise null strings.
            tournamentID = (uint)DateTime.UtcNow.Subtract(new DateTime(2020, 1, 1)).TotalSeconds;
            savegame = HighLogic.SaveFolder;
            tournamentType = TournamentType.FFA;
            var abs_folder = Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn", folder);
            if (!Directory.Exists(abs_folder))
            {
                message = "Tournament folder (" + folder + ") containing craft files does not exist.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                return false;
            }
            craftFiles = Directory.GetFiles(abs_folder, "*.craft").ToList();
            vesselCount = craftFiles.Count;
            int fullHeatCount;
            switch (vesselsPerHeat)
            {
                case -1: // Auto
                    var autoVesselsPerHeat = OptimiseVesselsPerHeat(craftFiles.Count);
                    vesselsPerHeat = autoVesselsPerHeat.Item1;
                    fullHeatCount = Mathf.CeilToInt(craftFiles.Count / vesselsPerHeat) - autoVesselsPerHeat.Item2;
                    break;
                case 0: // Unlimited (all vessels in one heat).
                    vesselsPerHeat = craftFiles.Count;
                    fullHeatCount = 1;
                    break;
                default:
                    vesselsPerHeat = Mathf.Clamp(vesselsPerHeat, 1, craftFiles.Count);
                    fullHeatCount = craftFiles.Count / vesselsPerHeat;
                    break;
            }
            rounds = new Dictionary<int, Dictionary<int, CircularSpawnConfig>>();
            switch (tournamentStyle)
            {
                case 0: // RNG
                    {
                        message = $"Generating {numberOfRounds} randomised rounds for tournament {tournamentID} for {vesselCount} vessels in AutoSpawn{(folder == "" ? "" : "/" + folder)}, each with {vesselsPerHeat} vessels per heat.";
                        Debug.Log("[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            craftFiles.Shuffle();
                            int vesselsThisHeat = vesselsPerHeat;
                            int count = 0;
                            List<string> selectedFiles = craftFiles.Take(vesselsThisHeat).ToList();
                            rounds.Add(rounds.Count, new Dictionary<int, CircularSpawnConfig>());
                            int heatIndex = 0;
                            while (selectedFiles.Count > 0)
                            {
                                rounds[roundIndex].Add(rounds[roundIndex].Count, new CircularSpawnConfig(
                                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                    true, // Kill everything first.
                                    BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                    0, // Number of teams.
                                    null, // List of team numbers.
                                    null, // List of List of teams' vessels.
                                    null, // No folder, we're going to specify the craft files.
                                    selectedFiles.ToList() // Add a copy of the craft files list.
                                ));
                                count += vesselsThisHeat;
                                vesselsThisHeat = heatIndex++ < fullHeatCount ? vesselsPerHeat : vesselsPerHeat - 1; // Take one less for the remaining heats to distribute the deficit of craft files.
                                selectedFiles = craftFiles.Skip(count).Take(vesselsThisHeat).ToList();
                            }
                        }
                        break;
                    }
                case 1: // N-choose-K
                    {
                        var nCr = N_Choose_K(vesselCount, vesselsPerHeat);
                        message = $"Generating a round-robin style tournament for {vesselCount} vessels in AutoSpawn{(folder == "" ? "" : "/" + folder)} with {vesselsPerHeat} vessels per heat and {numberOfRounds} rounds. This requires {numberOfRounds * nCr} heats.";
                        Debug.Log($"[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        // Generate all combinations of vessels for a round.
                        var heatList = new List<CircularSpawnConfig>();
                        foreach (var combination in Combinations(vesselCount, vesselsPerHeat))
                        {
                            heatList.Add(new CircularSpawnConfig(
                                BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                true, // Kill everything first.
                                BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                0, // Number of teams.
                                null, // List of team numbers.
                                null, // List of List of teams' vessels.
                                null, // No folder, we're going to specify the craft files.
                                combination.Select(i => craftFiles[i]).ToList() // Add a copy of the craft files list.
                            ));
                        }
                        // Populate the rounds.
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            heatList.Shuffle(); // Randomise the playing order within each round.
                            rounds.Add(roundIndex, heatList.Select((heat, index) => new KeyValuePair<int, CircularSpawnConfig>(index, heat)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                        }
                        break;
                    }
                default:
                    {
                        BDACompetitionMode.Instance.competitionStatus.Add("Tournament style not implemented yet for FFA.");
                        throw new ArgumentOutOfRangeException("tournamentStyle", "Invalid tournament style value - not implemented.");
                    }
            }
            teamFiles = null; // Clear the teams lists.
            return true;
        }

        /// <summary>
        /// Generate a tournament.state file for teams tournaments.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="numberOfRounds"></param>
        /// <param name="teamsPerHeat"></param>
        /// <param name="vesselsPerTeam"></param>
        /// <param name="numberOfTeams"></param>
        /// <returns></returns>
        public bool Generate(string folder, int numberOfRounds, int teamsPerHeat, int vesselsPerTeam, int numberOfTeams, int tournamentStyle)
        {
            folder ??= ""; // Sanitise null strings.
            tournamentID = (uint)DateTime.UtcNow.Subtract(new DateTime(2020, 1, 1)).TotalSeconds;
            savegame = HighLogic.SaveFolder;
            tournamentType = TournamentType.Teams;
            var absFolder = Path.Combine(KSPUtil.ApplicationRootPath, "AutoSpawn", folder);
            if (!Directory.Exists(absFolder))
            {
                message = "Tournament folder (" + folder + ") containing craft files or team folders does not exist.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                return false;
            }
            if (numberOfTeams > 1) // Make teams from the files in the spawn folder.
            {
                craftFiles = Directory.GetFiles(absFolder, "*.craft").ToList();
                if (craftFiles.Count < numberOfTeams)
                {
                    message = "Insufficient vessels in AutoSpawn" + (!string.IsNullOrEmpty(folder) ? "/" + folder : "") + " to make " + numberOfTeams + " teams.";
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    Debug.Log("[BDArmory.BDATournament]: " + message);
                    return false;
                }
                craftFiles.Shuffle();

                int numberPerTeam = craftFiles.Count / numberOfTeams;
                int residue = craftFiles.Count - numberPerTeam * numberOfTeams;
                teamFiles = new List<List<string>>();
                for (int teamCount = 0, count = 0; teamCount < numberOfTeams; ++teamCount)
                {
                    var toTake = numberPerTeam + (teamCount < residue ? 1 : 0);
                    teamFiles.Add(craftFiles.Skip(count).Take(toTake).ToList());
                    count += toTake;
                }
            }
            else // Make teams from the folders under the spawn folder.
            {
                var teamDirs = Directory.GetDirectories(absFolder);
                if (tournamentStyle == 2) // If it's a gauntlet tournament, ignore the opponents folder if it's in the main folder.
                {
                    var opponentFolder = Path.GetFileName(BDArmorySettings.VESSEL_SPAWN_GAUNTLET_OPPONENTS_FILES_LOCATION.TrimEnd(new char[] { Path.DirectorySeparatorChar }));
                    if (teamDirs.Select(d => Path.GetFileName(d)).Contains(opponentFolder))
                    {
                        teamDirs = teamDirs.Where(d => Path.GetFileName(d) != opponentFolder).ToArray();
                    }
                }
                if (teamDirs.Length < (tournamentStyle != 2 ? 2 : 1)) // Make teams from each vessel in the spawn folder. Allow for a single subfolder for putting bad craft or other tmp things in, unless it's a gauntlet competition.
                {
                    numberOfTeams = -1; // Flag for treating craft files as folder names.
                    craftFiles = Directory.GetFiles(absFolder, "*.craft").ToList();
                    teamFiles = craftFiles.Select(f => new List<string> { f }).ToList();
                }
                else
                {
                    teamFiles = new List<List<string>>();
                    foreach (var teamDir in teamDirs)
                    {
                        var currentTeamFiles = Directory.GetFiles(teamDir, "*.craft").ToList();
                        if (currentTeamFiles.Count > 0)
                            teamFiles.Add(currentTeamFiles);
                    }
                    foreach (var team in teamFiles)
                        team.Shuffle();
                    craftFiles = teamFiles.SelectMany(v => v).ToList();
                }
            }
            vesselCount = craftFiles.Count;
            if (teamFiles.Count < (tournamentStyle != 2 ? 2 : 1))
            {
                message = $"Insufficient {(numberOfTeams != 1 ? "craft files" : "folders")} in '{Path.Combine("AutoSpawn", folder)}' to generate a tournament.";
                if (BDACompetitionMode.Instance) BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                return false;
            }
            teamCount = teamFiles.Count;
            teamsPerHeat = Mathf.Clamp(teamsPerHeat, (tournamentStyle != 2 ? 2 : 1), teamFiles.Count);
            this.teamsPerHeat = teamsPerHeat;
            this.vesselsPerTeam = vesselsPerTeam;
            fullTeams = BDArmorySettings.TOURNAMENT_FULL_TEAMS;
            var teamsIndex = Enumerable.Range(0, teamFiles.Count).ToList();
            teamSpawnQueues.Clear();

            int fullHeatCount = teamFiles.Count / teamsPerHeat;
            rounds = new Dictionary<int, Dictionary<int, CircularSpawnConfig>>();
            switch (tournamentStyle)
            {
                case 0: // RNG
                    {
                        message = $"Generating {numberOfRounds} randomised rounds for tournament {tournamentID} for {teamCount} teams in AutoSpawn{(folder == "" ? "" : "/" + folder)}, each with {teamsPerHeat} teams per heat.";
                        Debug.Log("[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            teamsIndex.Shuffle();
                            int teamsThisHeat = teamsPerHeat;
                            int count = 0;
                            var selectedTeams = teamsIndex.Take(teamsThisHeat).ToList();
                            var selectedCraft = SelectTeamCraft(selectedTeams, vesselsPerTeam);
                            rounds.Add(rounds.Count, new Dictionary<int, CircularSpawnConfig>());
                            int heatIndex = 0;
                            while (selectedTeams.Count > 0)
                            {
                                rounds[roundIndex].Add(rounds[roundIndex].Count, new CircularSpawnConfig(
                                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                    true, // Kill everything first.
                                    BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                    numberOfTeams, // Number of teams indicator.
                                    null, //selectedCraft.Select(c => c.Count).ToList(), // Not used here.
                                    selectedCraft, // List of lists of vessels. For splitting specific vessels into specific teams.
                                    null, // No folder, we're going to specify the craft files.
                                    null // No list of craft files, we've specified them directly in selectedCraft.
                                ));
                                count += teamsThisHeat;
                                teamsThisHeat = heatIndex++ < fullHeatCount ? teamsPerHeat : teamsPerHeat - 1; // Take one less for the remaining heats to distribute the deficit of teams.
                                selectedTeams = teamsIndex.Skip(count).Take(teamsThisHeat).ToList();
                                selectedCraft = SelectTeamCraft(selectedTeams, vesselsPerTeam);
                            }
                        }
                        break;
                    }
                case 1: // N-choose-K
                    {
                        var nCr = N_Choose_K(teamCount, teamsPerHeat);
                        message = $"Generating a round-robin style tournament for {teamCount} teams in AutoSpawn{(folder == "" ? "" : "/" + folder)} with {teamsPerHeat} teams per heat and {numberOfRounds} rounds. This requires {numberOfRounds * nCr} heats.";
                        Debug.Log($"[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        // Generate all combinations of teams for a round.
                        var combinations = Combinations(teamCount, teamsPerHeat);
                        // Populate the rounds.
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            var heatList = new List<CircularSpawnConfig>();
                            foreach (var combination in combinations)
                            {
                                var selectedCraft = SelectTeamCraft(combination.Select(i => teamsIndex[i]).ToList(), vesselsPerTeam); // Vessel selection for a team can vary between rounds if the number of vessels in a team doesn't match the vesselsPerTeam parameter.
                                heatList.Add(new CircularSpawnConfig(
                                    BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                    BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                    BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                    BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                    true, // Kill everything first.
                                    BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                    numberOfTeams, // Number of teams indicator.
                                    null, //selectedCraft.Select(c => c.Count).ToList(), // Not used here.
                                    selectedCraft, // List of lists of vessels. For splitting specific vessels into specific teams.
                                    null, // No folder, we're going to specify the craft files.
                                    null // No list of craft files, we've specified them directly in selectedCraft.
                                ));
                            }
                            heatList.Shuffle(); // Randomise the playing order within each round.
                            rounds.Add(roundIndex, heatList.Select((heat, index) => new KeyValuePair<int, CircularSpawnConfig>(index, heat)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                        }
                        break;
                    }
                case 2: // Gauntlet
                    {
                        // Gauntlet is like N-choose-2K except that it's selecting K teams from the main folder and K teams from the opponents (e.g., 2v2, 3v3, 2v3, (2v2)v(2v2), (2v3)v(3v2), etc.)
                        #region Opponent config
                        var opponentFolder = Path.Combine("AutoSpawn", BDArmorySettings.VESSEL_SPAWN_GAUNTLET_OPPONENTS_FILES_LOCATION);
                        var opponentAbsFolder = Path.Combine(KSPUtil.ApplicationRootPath, opponentFolder);
                        if (!Directory.Exists(opponentAbsFolder))
                        {
                            message = "Opponents folder (" + opponentFolder + ") containing craft files or team folders does not exist.";
                            BDACompetitionMode.Instance.competitionStatus.Add(message);
                            Debug.Log("[BDArmory.BDATournament]: " + message);
                            return false;
                        }
                        var opponentTeamDirs = Directory.GetDirectories(opponentAbsFolder);
                        List<string> opponentCraftFiles;
                        if (opponentTeamDirs.Length < 1) // Make teams from each vessel in the opponents folder.
                        {
                            opponentCraftFiles = Directory.GetFiles(opponentAbsFolder, "*.craft").ToList();
                            opponentTeamFiles = opponentCraftFiles.Select(f => new List<string> { f }).ToList();
                        }
                        else
                        {
                            opponentTeamFiles = new List<List<string>>();
                            foreach (var teamDir in opponentTeamDirs)
                            {
                                var currentTeamFiles = Directory.GetFiles(teamDir, "*.craft").ToList();
                                if (currentTeamFiles.Count > 0)
                                    opponentTeamFiles.Add(currentTeamFiles);
                            }
                            foreach (var team in opponentTeamFiles)
                                team.Shuffle();
                            opponentCraftFiles = opponentTeamFiles.SelectMany(v => v).ToList();
                        }
                        if (opponentTeamFiles.Count < 1)
                        {
                            message = $"Insufficient {(opponentTeamDirs.Length < 1 ? "craft files" : "folders")} in '{opponentFolder}' to generate a gauntlet tournament.";
                            if (BDACompetitionMode.Instance) BDACompetitionMode.Instance.competitionStatus.Add(message);
                            Debug.Log("[BDArmory.BDATournament]: " + message);
                            return false;
                        }
                        var opponentTeamCount = opponentTeamFiles.Count;
                        var opponentTeamsIndex = Enumerable.Range(0, opponentTeamCount).ToList();
                        #endregion

                        #region Tournament generation
                        var nCr = N_Choose_K(teamCount, teamsPerHeat) * N_Choose_K(opponentTeamCount, BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT);
                        message = $"Generating a gauntlet style tournament for {teamCount} teams in AutoSpawn{(folder == "" ? "" : "/" + folder)} and {opponentTeamCount} opponent teams in {opponentFolder} with {BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT} teams per heat and {numberOfRounds} rounds. This requires {numberOfRounds * nCr} heats.";
                        Debug.Log($"[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        // Generate all combinations of teams for a round.
                        var combinations = Combinations(teamCount, teamsPerHeat);
                        var opponentCombinations = Combinations(opponentTeamCount, BDArmorySettings.TOURNAMENT_OPPONENT_TEAMS_PER_HEAT);
                        // Populate the rounds.
                        for (int roundIndex = 0; roundIndex < numberOfRounds; ++roundIndex)
                        {
                            var heatList = new List<CircularSpawnConfig>();
                            foreach (var combination in combinations)
                            {
                                var selectedCraft = SelectTeamCraft(combination.Select(i => teamsIndex[i]).ToList(), vesselsPerTeam); // Vessel selection for a team can vary between rounds if the number of vessels in a team doesn't match the vesselsPerTeam parameter.
                                foreach (var opponentCombination in opponentCombinations)
                                {
                                    var selectedOpponentCraft = SelectTeamCraft(opponentCombination.Select(i => opponentTeamsIndex[i]).ToList(), BDArmorySettings.TOURNAMENT_OPPONENT_VESSELS_PER_TEAM, true); // Vessel selection for a team can vary between rounds if the number of vessels in a team doesn't match the vesselsPerTeam parameter.
                                    heatList.Add(new CircularSpawnConfig(
                                        BDArmorySettings.VESSEL_SPAWN_WORLDINDEX,
                                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x,
                                        BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y,
                                        BDArmorySettings.VESSEL_SPAWN_ALTITUDE_,
                                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR,
                                        BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE,
                                        true, // Kill everything first.
                                        BDArmorySettings.VESSEL_SPAWN_REASSIGN_TEAMS, // Assign teams.
                                        numberOfTeams, // Number of teams indicator. (Should be -1 for gauntlets for now.)
                                        null, //selectedCraft.Select(c => c.Count).ToList(), // Not used here.
                                        selectedCraft.Concat(selectedOpponentCraft).ToList(), // List of lists of vessels. For splitting specific vessels into specific teams.
                                        null, // No folder, we're going to specify the craft files.
                                        null // No list of craft files, we've specified them directly in selectedCraft.
                                    ));
                                }
                            }
                            heatList.Shuffle(); // Randomise the playing order within each round.
                            rounds.Add(roundIndex, heatList.Select((heat, index) => new KeyValuePair<int, CircularSpawnConfig>(index, heat)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                        }
                        #endregion
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException("tournamentStyle", "Invalid tournament style value - not implemented.");
            }
            return true;
        }

        List<List<string>> SelectTeamCraft(List<int> selectedTeams, int vesselsPerTeam, bool opponentQueue = false)
        {
            // Get the right spawn queues and file lists.
            var spawnQueues = opponentQueue ? opponentTeamSpawnQueues : teamSpawnQueues;
            var teams = opponentQueue ? opponentTeamFiles : teamFiles;

            if (spawnQueues.Count == 0) // Set up the spawn queues if needed.
            {
                foreach (var teamIndex in teams)
                    spawnQueues.Add(new Queue<string>());
            }

            List<List<string>> selectedCraft = new List<List<string>>();
            List<string> currentTeam = new List<string>();
            foreach (var index in selectedTeams)
            {
                if (spawnQueues[index].Count < vesselsPerTeam)
                {
                    // First append craft files that aren't already in the queue.
                    var craftToAdd = teams[index].Where(c => !spawnQueues[index].Contains(c)).ToList();
                    craftToAdd.Shuffle();
                    foreach (var craft in craftToAdd)
                    {
                        spawnQueues[index].Enqueue(craft);
                    }
                    if (BDArmorySettings.TOURNAMENT_FULL_TEAMS)
                    {
                        // Then continue to fill the queue with craft files until we have enough.
                        while (spawnQueues[index].Count < vesselsPerTeam)
                        {
                            craftToAdd = teams[index].ToList();
                            craftToAdd.Shuffle();
                            foreach (var craft in craftToAdd)
                            {
                                spawnQueues[index].Enqueue(craft);
                            }
                        }
                    }
                }
                currentTeam.Clear();
                while (currentTeam.Count < vesselsPerTeam && spawnQueues[index].Count > 0)
                {
                    currentTeam.Add(spawnQueues[index].Dequeue());
                }
                selectedCraft.Add(currentTeam.ToList());
            }
            return selectedCraft;
        }

        Tuple<int, int> OptimiseVesselsPerHeat(int count)
        {
            if (BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y < BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.x) BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y = BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.x;
            var options = count > BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y && count < 2 * BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.x - 1 ?
                Enumerable.Range(BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y / 2, BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y - BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y / 2 + 1).Reverse().ToList() // Tweak the range when just over the upper limit to give more balanced heats.
                : Enumerable.Range(BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.x, BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.y - BDArmorySettings.TOURNAMENT_AUTO_VESSELS_PER_HEAT_RANGE.x + 1).Reverse().ToList();
            foreach (var val in options)
            {
                if (count % val == 0)
                    return new Tuple<int, int>(val, 0);
            }
            var result = OptimiseVesselsPerHeat(count + 1);
            return new Tuple<int, int>(result.Item1, result.Item2 + 1);
        }

        public bool SaveState(string stateFile)
        {
            if (rounds == null) return false; // Nothing to save.
            try
            {
                List<string> strings = new List<string>();

                strings.Add(JsonUtility.ToJson(this));
                foreach (var round in rounds.Keys)
                    foreach (var heat in rounds[round].Keys)
                        strings.Add(JsonUtility.ToJson(new RoundConfig(round, heat, completed.ContainsKey(round) && completed[round].Contains(heat), rounds[round][heat])));

                if (!Directory.GetParent(stateFile).Exists)
                { Directory.GetParent(stateFile).Create(); }
                File.WriteAllLines(stateFile, strings);
                Debug.Log($"[BDArmory.BDATournament]: Tournament state saved to {stateFile}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.BDATournament]: Exception thrown in SaveState: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }

        public bool LoadState(string stateFile)
        {
            try
            {
                if (!File.Exists(stateFile)) return false;
                var strings = File.ReadAllLines(stateFile);
                var data = JsonUtility.FromJson<TournamentState>(strings[0]);
                tournamentID = data.tournamentID;
                savegame = data.savegame;
                vesselCount = data.vesselCount;
                teamCount = data.teamCount;
                teamsPerHeat = data.teamsPerHeat;
                vesselsPerTeam = data.vesselsPerTeam;
                fullTeams = data.fullTeams;
                tournamentType = data.tournamentType;
                rounds = new Dictionary<int, Dictionary<int, CircularSpawnConfig>>();
                completed = new Dictionary<int, HashSet<int>>();
                for (int i = 1; i < strings.Length; ++i)
                {
                    if (strings[i].Length > 0)
                    {
                        var roundConfig = JsonUtility.FromJson<RoundConfig>(strings[i]);
                        if (!strings[i].Contains("worldIndex")) roundConfig.worldIndex = 1; // Default old tournament states to be on Kerbin.
                        roundConfig.DeserializeTeams();
                        if (!rounds.ContainsKey(roundConfig.round)) rounds.Add(roundConfig.round, new Dictionary<int, CircularSpawnConfig>());
                        rounds[roundConfig.round].Add(roundConfig.heat, new CircularSpawnConfig(
                            roundConfig.worldIndex,
                            roundConfig.latitude,
                            roundConfig.longitude,
                            roundConfig.altitude,
                            roundConfig.distance,
                            roundConfig.absDistanceOrFactor,
                            roundConfig.killEverythingFirst,
                            roundConfig.assignTeams,
                            roundConfig.numberOfTeams,
                            roundConfig.teamCounts == null || roundConfig.teamCounts.Count == 0 ? null : roundConfig.teamCounts,
                            roundConfig.teamsSpecific == null || roundConfig.teamsSpecific.Count == 0 ? null : roundConfig.teamsSpecific,
                            roundConfig.folder,
                            roundConfig.craftFiles
                        ));
                        if (roundConfig.completed)
                        {
                            if (!completed.ContainsKey(roundConfig.round)) completed.Add(roundConfig.round, new HashSet<int>());
                            completed[roundConfig.round].Add(roundConfig.heat);
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmory.BDATournament]: " + e.Message);
                return false;
            }
        }

        #region Helper functions
        /// <summary>
        /// Calculate N-choose-K.
        /// </summary>
        /// <param name="n">N</param>
        /// <param name="k">K</param>
        /// <returns>The number of ways of choosing K unique items of a collection of N.</returns>
        public static int N_Choose_K(int n, int k)
        {
            k = Mathf.Clamp(k, 0, n);
            k = Math.Min(n, k);
            var numer = Enumerable.Range(n - k + 1, k).Aggregate(1, (acc, val) => acc * val);
            var denom = Enumerable.Range(1, k).Aggregate(1, (acc, val) => acc * val);
            return Mathf.RoundToInt(numer / denom);
        }
        /// <summary>
        /// Generate all combinations of N-choose-K.
        /// </summary>
        /// <param name="n">N</param>
        /// <param name="k">K</param>
        /// <returns>List of list of unique combinations of K indices from 0 to N-1.</returns>
        public static List<List<int>> Combinations(int n, int k)
        {
            k = Mathf.Clamp(k, 0, n);
            var combinations = new List<List<int>>();
            var temp = new List<int>();
            GenerateCombinations(ref combinations, temp, 0, n, k);
            return combinations;
        }
        /// <summary>
        /// Recursively generate all combinations of N-choose-K.
        /// Helper function.
        /// </summary>
        /// <param name="combinations">The combinations are accumulated in this list of lists.</param>
        /// <param name="temp">Temporary buffer containing current chosen values.</param>
        /// <param name="i">Current choice</param>
        /// <param name="n">N</param>
        /// <param name="k">K remaining to choose</param>
        static void GenerateCombinations(ref List<List<int>> combinations, List<int> temp, int i, int n, int k)
        {
            if (k == 0)
            {
                combinations.Add(temp.ToList()); // Take a copy otherwise C# disposes of it.
                return;
            }
            for (int j = i; j < n; ++j)
            {
                temp.Add(j);
                GenerateCombinations(ref combinations, temp, j + 1, n, k - 1);
                temp.RemoveAt(temp.Count - 1);
            }
        }
        #endregion
    }

    public enum TournamentStatus { Stopped, Running, Waiting, Completed };

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDATournament : MonoBehaviour
    {
        public static BDATournament Instance;

        #region Flags and Variables
        TournamentState tournamentState;
        string stateFile;
        string message;
        private Coroutine runTournamentCoroutine;
        public TournamentStatus tournamentStatus = TournamentStatus.Stopped;
        public uint tournamentID = 0;
        public TournamentType tournamentType = TournamentType.FFA;
        public int numberOfRounds = 0;
        public int currentRound = 0;
        public int numberOfHeats = 0;
        public int currentHeat = 0;
        public int heatsRemaining = 0;
        public int vesselCount = 0;
        public int teamCount = 0;
        public int teamsPerHeat = 0;
        public int vesselsPerTeam = 0;
        public bool fullTeams = false;
        bool competitionStarted = false;
        public bool warpingInProgress = false;
        #endregion

        void Awake()
        {
            if (Instance)
                Destroy(Instance);
            Instance = this;
            stateFile = TournamentState.defaultStateFile;
        }

        void Start()
        {
            BDArmorySettings.LAST_USED_SAVEGAME = HighLogic.SaveFolder;
            StartCoroutine(LoadStateWhenReady());
        }

        IEnumerator LoadStateWhenReady()
        {
            while (BDACompetitionMode.Instance == null)
                yield return null;
            LoadTournamentState(); // Load the last state.
        }

        void OnDestroy()
        {
            StopTournament(); // Stop any running tournament.
            SaveTournamentState(); // Save the last state.
        }

        // Load tournament state from disk
        bool LoadTournamentState(string stateFile = "")
        {
            if (stateFile != "") this.stateFile = stateFile;
            tournamentState = new TournamentState();
            if (tournamentState.LoadState(this.stateFile))
            {
                message = "Tournament state loaded from " + this.stateFile;
                tournamentID = tournamentState.tournamentID;
                tournamentType = tournamentState.tournamentType;
                vesselCount = tournamentState.vesselCount;
                teamCount = tournamentState.teamCount;
                teamsPerHeat = tournamentState.teamsPerHeat;
                vesselsPerTeam = tournamentState.vesselsPerTeam;
                fullTeams = tournamentState.fullTeams;
                numberOfRounds = tournamentState.rounds.Count;
                numberOfHeats = numberOfRounds > 0 ? tournamentState.rounds[0].Count : 0;
                heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum();
            }
            else
                message = "Failed to load tournament state.";
            Debug.Log("[BDArmory.BDATournament]: " + message);
            // if (BDACompetitionMode.Instance != null)
            //     BDACompetitionMode.Instance.competitionStatus.Add(message);
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
            return true;
        }

        // Save tournament state to disk
        bool SaveTournamentState(bool backup = false)
        {
            var saveTo = stateFile;
            if (backup)
            {
                var saveToDir = Path.GetDirectoryName(TournamentState.defaultStateFile);
                saveToDir = Path.Combine(saveToDir, "Unfinished Tournaments");
                if (!Directory.Exists(saveToDir)) Directory.CreateDirectory(saveToDir);
                saveTo = Path.ChangeExtension(Path.Combine(saveToDir, Path.GetFileName(stateFile)), $".state-{tournamentID}");
            }
            return tournamentState.SaveState(saveTo);
        }

        public void SetupTournament(string folder, int rounds, int vesselsPerHeat = 0, int teamsPerHeat = 0, int vesselsPerTeam = 0, int numberOfTeams = 0, int tournamentStyle = 0, string stateFile = "")
        {
            if (tournamentState != null && tournamentState.rounds != null)
            {
                heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum();
                if (heatsRemaining > 0 && heatsRemaining < numberOfRounds * numberOfHeats) // Started, but incomplete tournament.
                {
                    SaveTournamentState(true);
                }
            }
            if (stateFile != "") this.stateFile = stateFile;
            if ((BDArmorySettings.WAYPOINTS_MODE || (BDArmorySettings.RUNWAY_PROJECT && (BDArmorySettings.RUNWAY_PROJECT_ROUND == 50 || BDArmorySettings.RUNWAY_PROJECT_ROUND == 55))) && BDArmorySettings.WAYPOINTS_ONE_AT_A_TIME) vesselsPerHeat = 1; // Override vessels per heat.
            tournamentState = new TournamentState();
            if (numberOfTeams == 0) // FFA
            {
                if (!tournamentState.Generate(folder, rounds, vesselsPerHeat, tournamentStyle)) return;
            }
            else // Folders or random teams
            {
                if (!tournamentState.Generate(folder, rounds, teamsPerHeat, vesselsPerTeam, numberOfTeams, tournamentStyle)) return;
            }
            tournamentID = tournamentState.tournamentID;
            tournamentType = tournamentState.tournamentType;
            vesselCount = tournamentState.vesselCount;
            teamCount = tournamentState.teamCount;
            this.teamsPerHeat = tournamentState.teamsPerHeat;
            this.vesselsPerTeam = tournamentState.vesselsPerTeam;
            fullTeams = tournamentState.fullTeams;
            numberOfRounds = tournamentState.rounds.Count;
            numberOfHeats = numberOfRounds > 0 ? tournamentState.rounds[0].Count : 0;
            heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum();
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
            SaveTournamentState();
        }

        public void RunTournament()
        {
            tournamentState.savegame = HighLogic.SaveFolder;
            BDACompetitionMode.Instance.StopCompetition();
            SpawnUtils.CancelSpawning();
            if (runTournamentCoroutine != null)
                StopCoroutine(runTournamentCoroutine);
            runTournamentCoroutine = StartCoroutine(RunTournamentCoroutine());
            if (BDArmorySettings.AUTO_DISABLE_UI) SetGameUI(false);
        }

        public void StopTournament()
        {
            if (runTournamentCoroutine != null)
            {
                StopCoroutine(runTournamentCoroutine);
                runTournamentCoroutine = null;
            }
            tournamentStatus = heatsRemaining > 0 ? TournamentStatus.Stopped : TournamentStatus.Completed;
            if (BDArmorySettings.AUTO_DISABLE_UI) SetGameUI(true);
        }

        IEnumerator RunTournamentCoroutine()
        {
            bool firstRun = true; // Whether a heat has been run yet (particularly for loading partway through a tournament).
            yield return new WaitForFixedUpdate();
            foreach (var roundIndex in tournamentState.rounds.Keys)
            {
                currentRound = roundIndex;
                foreach (var heatIndex in tournamentState.rounds[roundIndex].Keys)
                {
                    currentHeat = heatIndex;
                    if (tournamentState.completed.ContainsKey(roundIndex) && tournamentState.completed[roundIndex].Contains(heatIndex)) continue; // We've done that heat.

                    message = $"Running heat {heatIndex} of round {roundIndex} of tournament {tournamentState.tournamentID} ({heatsRemaining} heats remaining in the tournament).";
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    Debug.Log("[BDArmory.BDATournament]: " + message);

                    int attempts = 0;
                    bool unrecoverable = false;
                    competitionStarted = false;
                    while (!competitionStarted && attempts++ < 3) // 3 attempts is plenty
                    {
                        tournamentStatus = TournamentStatus.Running;
                        if (BDArmorySettings.WAYPOINTS_MODE || (BDArmorySettings.RUNWAY_PROJECT && (BDArmorySettings.RUNWAY_PROJECT_ROUND == 50 || BDArmorySettings.RUNWAY_PROJECT_ROUND == 55)))
                            yield return ExecuteWaypointHeat(roundIndex, heatIndex);
                        else
                            yield return ExecuteHeat(roundIndex, heatIndex, attempts == 3 && BDArmorySettings.COMPETITION_START_DESPITE_FAILURES); // On the third attempt, start despite failures if the option is set.
                        if (!competitionStarted)
                            switch (CircularSpawning.Instance.spawnFailureReason)
                            {
                                case SpawnFailureReason.None: // Successful spawning, but competition failed to start for some reason.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + BDACompetitionMode.Instance.competitionStartFailureReason + ", trying again.");
                                    break;
                                case SpawnFailureReason.VesselLostParts: // Recoverable spawning failure.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + CircularSpawning.Instance.spawnFailureReason + ", trying again with increased altitude.");
                                    if (tournamentState.rounds[roundIndex][heatIndex].altitude < 10) tournamentState.rounds[roundIndex][heatIndex].altitude = Math.Min(tournamentState.rounds[roundIndex][heatIndex].altitude + 3, 10); // Increase the spawning altitude for ground spawns and try again.
                                    break;
                                case SpawnFailureReason.TimedOut: // Recoverable spawning failure.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + CircularSpawning.Instance.spawnFailureReason + ", trying again.");
                                    break;
                                case SpawnFailureReason.NoTerrain: // Failed to find the terrain when ground spawning.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + CircularSpawning.Instance.spawnFailureReason + ", trying again.");
                                    attempts = Math.Max(attempts, 2); // Try only once more.
                                    break;
                                case SpawnFailureReason.DependencyIssues:
                                    message = $"Failed to start heat due to {CircularSpawning.Instance.spawnFailureReason}, aborting. Make sure dependencies are installed and enabled, then revert to launch and try again.";
                                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                                    Debug.LogWarning($"[BDArmory.BDATournament]: {message}");
                                    attempts = 3;
                                    unrecoverable = true;
                                    break;
                                default: // Spawning is unrecoverable.
                                    BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + CircularSpawning.Instance.spawnFailureReason + ", aborting.");
                                    attempts = 3;
                                    unrecoverable = true;
                                    break;
                            }
                    }
                    if (!competitionStarted)
                    {
                        message = $"Failed to run heat {(unrecoverable?"due to unrecoverable error": $"after 3 spawning attempts")}, failure reasons: " + CircularSpawning.Instance.spawnFailureReason + ", " + BDACompetitionMode.Instance.competitionStartFailureReason + ". Stopping tournament. Please fix the failure reason before continuing the tournament.";
                        Debug.Log("[BDArmory.BDATournament]: " + message);
                        BDACompetitionMode.Instance.competitionStatus.Add(message);
                        tournamentStatus = TournamentStatus.Stopped;
                        yield break;
                    }
                    firstRun = false;

                    // Register the heat as completed.
                    if (!tournamentState.completed.ContainsKey(roundIndex)) tournamentState.completed.Add(roundIndex, new HashSet<int>());
                    tournamentState.completed[roundIndex].Add(heatIndex);
                    SaveTournamentState();
                    heatsRemaining = tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum();

                    if (TournamentAutoResume.Instance != null && TournamentAutoResume.Instance.CheckMemoryUsage()) yield break;

                    if (tournamentState.completed[roundIndex].Count < tournamentState.rounds[roundIndex].Count)
                    {
                        // Wait a bit for any user action
                        tournamentStatus = TournamentStatus.Waiting;
                        double startTime = Planetarium.GetUniversalTime();
                        while ((Planetarium.GetUniversalTime() - startTime) < BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS)
                        {
                            BDACompetitionMode.Instance.competitionStatus.Add("Waiting " + (BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS - (Planetarium.GetUniversalTime() - startTime)).ToString("0") + "s, then running the next heat.");
                            yield return new WaitForSeconds(1);
                        }
                    }
                }
                if (!firstRun)
                {
                    message = "All heats in round " + roundIndex + " have been run.";
                    BDACompetitionMode.Instance.competitionStatus.Add(message);
                    Debug.Log("[BDArmory.BDATournament]: " + message);
                    if (BDArmorySettings.WAYPOINTS_MODE || (BDArmorySettings.RUNWAY_PROJECT && (BDArmorySettings.RUNWAY_PROJECT_ROUND == 50 || BDArmorySettings.RUNWAY_PROJECT_ROUND == 55)))
                    {
                        /* commented out until this is made functional
                        foreach (var tracer in WaypointFollowingStrategy.Ghosts) //clear and reset vessel ghosts each new Round
                        {
                            tracer.gameObject.SetActive(false);
                        }
                        WaypointFollowingStrategy.Ghosts.Clear();
                        */
                    }
                    if (heatsRemaining > 0)
                    {
                        if (BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS > 0)
                        {
                            BDACompetitionMode.Instance.competitionStatus.Add($"Warping ahead {BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS} mins, then running the next round.");
                            yield return WarpAhead(BDArmorySettings.TOURNAMENT_TIMEWARP_BETWEEN_ROUNDS * 60);
                        }
                        else
                        {
                            // Wait a bit for any user action
                            tournamentStatus = TournamentStatus.Waiting;
                            double startTime = Planetarium.GetUniversalTime();
                            while ((Planetarium.GetUniversalTime() - startTime) < BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS)
                            {
                                BDACompetitionMode.Instance.competitionStatus.Add("Waiting " + (BDArmorySettings.TOURNAMENT_DELAY_BETWEEN_HEATS - (Planetarium.GetUniversalTime() - startTime)).ToString("0") + "s, then running the next round.");
                                yield return new WaitForSeconds(1);
                            }
                        }
                    }
                }
            }
            message = "All rounds in tournament " + tournamentState.tournamentID + " have been run.";
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            Debug.Log("[BDArmory.BDATournament]: " + message);
            tournamentStatus = TournamentStatus.Completed;
            if (BDArmorySettings.AUTO_DISABLE_UI) SetGameUI(true);
            var partialStatePath = Path.ChangeExtension(Path.Combine(Path.GetDirectoryName(TournamentState.defaultStateFile), "Unfinished Tournaments", Path.GetFileName(stateFile)), $".state-{tournamentID}");
            if (File.Exists(partialStatePath)) File.Delete(partialStatePath); // Remove the now completed tournament state file.

            if (BDArmorySettings.AUTO_RESUME_TOURNAMENT && BDArmorySettings.AUTO_QUIT_AT_END_OF_TOURNAMENT && TournamentAutoResume.Instance != null)
            {
                TournamentAutoResume.AutoQuit(5);
                message = "Quitting KSP in 5s due to reaching the end of a tournament.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.LogWarning("[BDArmory.BDATournament]: " + message);
                yield break;
            }
        }

        IEnumerator ExecuteWaypointHeat(int roundIndex, int heatIndex)
        {
            if (TournamentCoordinator.Instance.IsRunning) TournamentCoordinator.Instance.Stop();
            var spawnConfig = tournamentState.rounds[roundIndex][heatIndex];
            spawnConfig.worldIndex = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].worldIndex;
            spawnConfig.latitude = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.x;
            spawnConfig.longitude = WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].spawnPoint.y;

            TournamentCoordinator.Instance.Configure(new SpawnConfigStrategy(spawnConfig),
                new WaypointFollowingStrategy(WaypointCourses.CourseLocations[BDArmorySettings.WAYPOINT_COURSE_INDEX].waypoints),
                CircularSpawning.Instance
            );

            // Run the waypoint competition.
            TournamentCoordinator.Instance.Run();
            competitionStarted = true;
            yield return new WaitWhile(() => TournamentCoordinator.Instance.IsRunning);
        }

        IEnumerator ExecuteHeat(int roundIndex, int heatIndex, bool startDespiteFailures = false)
        {
            CircularSpawning.Instance.SpawnAllVesselsOnce(tournamentState.rounds[roundIndex][heatIndex]);
            while (CircularSpawning.Instance.vesselsSpawning)
                yield return new WaitForFixedUpdate();
            if (!CircularSpawning.Instance.vesselSpawnSuccess)
            {
                tournamentStatus = TournamentStatus.Stopped;
                yield break;
            }
            yield return new WaitForFixedUpdate();

            // NOTE: runs in separate coroutine
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                {
                    case 33:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 44:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 53: 
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 60: // FIXME temporary index, to be assigned later
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    default:
                        BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, startDespiteFailures);
                        break;
                }
            }
            else
                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE, startDespiteFailures);
            yield return new WaitForFixedUpdate(); // Give the competition start a frame to get going.

            // start timer coroutine for the duration specified in settings UI
            var duration = BDArmorySettings.COMPETITION_DURATION * 60f;
            message = "Starting " + (duration > 0 ? "a " + duration.ToString("F0") + "s" : "an unlimited") + " duration competition.";
            Debug.Log("[BDArmory.BDATournament]: " + message);
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            while (BDACompetitionMode.Instance.competitionStarting || BDACompetitionMode.Instance.sequencedCompetitionStarting)
                yield return new WaitForFixedUpdate(); // Wait for the competition to actually start.
            if (!BDACompetitionMode.Instance.competitionIsActive)
            {
                var message = "Competition failed to start.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                tournamentStatus = TournamentStatus.Stopped;
                yield break;
            }
            competitionStarted = true;
            while (BDACompetitionMode.Instance.competitionIsActive) // Wait for the competition to finish.
                yield return new WaitForSeconds(1);
        }

        GameObject warpCamera;
        IEnumerator WarpAhead(double warpTimeBetweenHeats)
        {
            if (!FlightGlobals.currentMainBody.hasSolidSurface)
            {
                message = "Sorry, unable to TimeWarp without a solid surface to place the spawn probe on.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDATournament]: " + message);
                yield return new WaitForSeconds(5f);
                yield break;
            }
            warpingInProgress = true;
            Vessel spawnProbe;
            var vesselsToKill = FlightGlobals.Vessels.ToList();
            int tries = 0;
            do
            {
                spawnProbe = VesselSpawner.SpawnSpawnProbe();
                yield return new WaitWhile(() => spawnProbe != null && (!spawnProbe.loaded || spawnProbe.packed));
                while (spawnProbe != null && FlightGlobals.ActiveVessel != spawnProbe)
                {
                    LoadedVesselSwitcher.Instance.ForceSwitchVessel(spawnProbe);
                    yield return null;
                }
            } while (++tries < 3 && spawnProbe == null);
            if (spawnProbe == null)
            {
                message = "Failed to spawn spawnProbe, aborting warp.";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.LogWarning("[BDArmory.BDATournament]: " + message);
                yield break;
            }
            var up = VectorUtils.GetUpDirection(spawnProbe.transform.position);
            var refDirection = Math.Abs(Vector3.Dot(Vector3.up, up)) < 0.71f ? Vector3.up : Vector3.forward; // Avoid that the reference direction is colinear with the local surface normal.
            spawnProbe.SetPosition(spawnProbe.transform.position - BodyUtils.GetRadarAltitudeAtPos(spawnProbe.transform.position) * up);
            if (spawnProbe.altitude > 0) spawnProbe.Landed = true;
            else spawnProbe.Splashed = true;
            spawnProbe.SetWorldVelocity(Vector3d.zero); // Set the velocity to zero so that warp goes in high mode.
            // Kill all other vessels (including debris).
            foreach (var vessel in vesselsToKill)
                SpawnUtils.RemoveVessel(vessel);
            while (SpawnUtils.removingVessels) yield return null;

            // Adjust the camera for a nice view.
            if (warpCamera == null) warpCamera = new GameObject("WarpCamera");
            var cameraLocalPosition = 3f * Vector3.Cross(up, refDirection).normalized + up;
            warpCamera.SetActive(true);
            warpCamera.transform.position = spawnProbe.transform.position;
            warpCamera.transform.rotation = Quaternion.LookRotation(-cameraLocalPosition, up);
            var flightCamera = FlightCamera.fetch;
            var originalCameraParentTransform = flightCamera.transform.parent;
            var originalCameraNearClipPlane = flightCamera.mainCamera.nearClipPlane;
            flightCamera.SetTargetNone();
            flightCamera.transform.parent = warpCamera.transform;
            flightCamera.transform.localPosition = cameraLocalPosition;
            flightCamera.transform.localRotation = Quaternion.identity;
            flightCamera.SetDistance(3000f);

            var warpTo = Planetarium.GetUniversalTime() + warpTimeBetweenHeats;
            var startTime = Time.time;
            do
            {
                if (TimeWarp.WarpMode != TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > 1) // Warping in low mode, abort.
                {
                    TimeWarp.fetch.CancelAutoWarp();
                    TimeWarp.SetRate(0, true, false);
                    while (TimeWarp.CurrentRate > 1) yield return null; // Wait for the warping to stop.
                    spawnProbe.SetPosition(spawnProbe.transform.position - BodyUtils.GetRadarAltitudeAtPos(spawnProbe.transform.position) * up);
                    if (spawnProbe.altitude > 0) spawnProbe.Landed = true;
                    else spawnProbe.Splashed = true;
                    spawnProbe.SetWorldVelocity(Vector3d.zero); // Set the velocity to zero so that warp goes in high mode.
                }
                startTime = Time.time;
                while (TimeWarp.WarpMode != TimeWarp.Modes.HIGH && Time.time - startTime < 3)
                {
                    spawnProbe.SetWorldVelocity(Vector3d.zero); // Set the velocity to zero so that warp goes in high mode.
                    yield return null; // Give it a second to switch to high warp mode.
                }
                TimeWarp.fetch.WarpTo(warpTo);
                startTime = Time.time;
                while (TimeWarp.CurrentRate < 2 && Time.time - startTime < 1) yield return null; // Give it a second to get going.
            } while (TimeWarp.WarpMode != TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > 1); // Warping, but not high warp, bugger. Try again. FIXME KSP isn't the focused app, it doesn't want to go into high warp!
            while (TimeWarp.CurrentRate > 1) yield return null; // Wait for the warping to stop.

            // Put the camera parent back.
            flightCamera.transform.parent = originalCameraParentTransform;
            flightCamera.mainCamera.nearClipPlane = originalCameraNearClipPlane;
            warpCamera.SetActive(false);

            warpingInProgress = false;
        }

        void SetGameUI(bool enable)
        { if (isActiveAndEnabled) StartCoroutine(SetGameUIWorker(enable)); }
        IEnumerator SetGameUIWorker(bool enable)
        {
            // On first entering flight mode, KSP issues a couple of ShowUI after a few seconds. WTF KSP devs?!
            yield return new WaitUntil(() => BDACompetitionMode.Instance is not null && (BDACompetitionMode.Instance.competitionStarting || BDACompetitionMode.Instance.competitionIsActive));
            // Also, triggering ShowUI/HideUI doesn't trigger the onShowUI/onHideUI events, so we need to fire them off ourselves.
            if (enable) { KSP.UI.UIMasterController.Instance.ShowUI(); GameEvents.onShowUI.Fire(); }
            else { KSP.UI.UIMasterController.Instance.HideUI(); GameEvents.onHideUI.Fire(); }
        }
    }

    /// <summary>
    /// A class to automatically load and resume a tournament upon starting KSP.
    /// Borrows heavily from the AutoLoadGame mod. 
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class TournamentAutoResume : MonoBehaviour
    {
        public static TournamentAutoResume Instance;
        public static bool firstRun = true;
        string savesDir;
        string savegame;
        string save = "persistent";
        string game;
        bool sceneLoaded = false;
        public static float memoryUsage
        {
            get
            {
                if (_memoryUsage > 0) return _memoryUsage;
                _memoryUsage = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() + UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
                var gfxDriver = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
                _memoryUsage += gfxDriver > 0 ? gfxDriver : 5f * (1 << 30); // Use the GfxDriver memory usage if available, otherwise estimate it at 5GB (which is a little more than what I get with no extra visual mods at ~4.5GB).
                _memoryUsage /= (1 << 30); // In GB.
                return _memoryUsage;
            }
            set { _memoryUsage = 0; } // Reset condition for calculating it again.
        }
        static float _memoryUsage;

        void Awake()
        {
            if (Instance != null || !firstRun) // Only the first loaded instance gets to run.
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this);
            GameEvents.onLevelWasLoadedGUIReady.Add(onLevelWasLoaded);
            savesDir = Path.Combine(KSPUtil.ApplicationRootPath, "saves");
        }

        void OnDestroy()
        {
            GameEvents.onLevelWasLoadedGUIReady.Remove(onLevelWasLoaded);
        }

        void onLevelWasLoaded(GameScenes scene)
        {
            sceneLoaded = true;
            if (scene != GameScenes.MAINMENU) return;
            if (!firstRun) return;
            firstRun = false;
            StartCoroutine(WaitForSettings());
        }

        IEnumerator WaitForSettings()
        {
            yield return new WaitForSeconds(0.5f);
            var tic = Time.realtimeSinceStartup;
            yield return new WaitUntil(() => (BDArmorySettings.ready || Time.realtimeSinceStartup - tic > 30)); // Wait until the settings are ready or timed out.
            Debug.Log($"[BDArmory.BDATournament]: BDArmory settings loaded, auto-load to KSC: {BDArmorySettings.AUTO_LOAD_TO_KSC}, auto-resume tournaments: {BDArmorySettings.AUTO_RESUME_TOURNAMENT}, auto-resume evolution: {BDArmorySettings.AUTO_RESUME_EVOLUTION}.");
            if (BDArmorySettings.AUTO_RESUME_TOURNAMENT || BDArmorySettings.AUTO_RESUME_EVOLUTION || BDArmorySettings.AUTO_LOAD_TO_KSC)
            { yield return StartCoroutine(AutoResumeTournament()); }
        }

        IEnumerator AutoResumeTournament()
        {
            bool resumingEvolution = false;
            bool resumingTournament = false;
            bool generateNewTournament = false;
            EvolutionWorkingState evolutionState = null;
            if (BDArmorySettings.AUTO_RESUME_EVOLUTION) // Auto-resume evolution overrides auto-resume tournament.
            {
                evolutionState = TryLoadEvolutionState();
                resumingEvolution = evolutionState != null;
            }
            if (!resumingEvolution && BDArmorySettings.AUTO_RESUME_TOURNAMENT)
            {
                resumingTournament = TryLoadTournamentState(out generateNewTournament);
            }
            if (!(resumingEvolution || resumingTournament)) // Auto-Load To KSC
            {
                if (!TryLoadCleanSlate()) yield break; // This shouldn't fail, but anyway...
            }
            // Load saved game.
            var tic = Time.time;
            sceneLoaded = false;
            if (!(BDArmorySettings.GENERATE_CLEAN_SAVE ? GenerateCleanGame() : LoadGame())) yield break;
            yield return new WaitUntil(() => (sceneLoaded || Time.time - tic > 10));
            if (!sceneLoaded) { Debug.Log("[BDArmory.BDATournament]: Failed to load scene."); yield break; }
            if (!(resumingEvolution || resumingTournament)) yield break; // Just load to the KSC.
            // Switch to flight mode.
            sceneLoaded = false;
            FlightDriver.StartWithNewLaunch(VesselSpawner.spawnProbeLocation, "GameData/Squad/Flags/default.png", FlightDriver.LaunchSiteName, new VesselCrewManifest()); // This triggers an error for SpaceCenterCamera2, but I don't see how to fix it and it doesn't appear to be harmful.
            tic = Time.time;
            yield return new WaitUntil(() => (sceneLoaded || Time.time - tic > 10));
            if (!sceneLoaded) { Debug.Log("[BDArmory.BDATournament]: Failed to load flight scene."); yield break; }
            // Resume the tournament.
            yield return new WaitForSeconds(1);
            if (resumingEvolution) // Auto-resume evolution overrides auto-resume tournament.
            {
                tic = Time.time;
                yield return new WaitWhile(() => (BDAModuleEvolution.Instance == null && Time.time - tic < 10)); // Wait for the tournament to be loaded or time out.
                if (BDAModuleEvolution.Instance == null) yield break;
                BDArmorySetup.windowBDAToolBarEnabled = true;
                LoadedVesselSwitcher.Instance.SetVisible(true);
                BDArmorySetup.Instance.showEvolutionGUI = true;
                BDAModuleEvolution.Instance.ResumeEvolution(evolutionState);
            }
            else if (resumingTournament)
            {
                tic = Time.time;
                if (generateNewTournament)
                {
                    yield return new WaitWhile(() => (BDATournament.Instance == null && Time.time - tic < 10)); // Wait for the BDATournament instance to be started or time out.
                    if (BDATournament.Instance == null) yield break;
                    BDATournament.Instance.SetupTournament(
                        BDArmorySettings.VESSEL_SPAWN_FILES_LOCATION,
                        BDArmorySettings.TOURNAMENT_ROUNDS,
                        BDArmorySettings.TOURNAMENT_VESSELS_PER_HEAT,
                        BDArmorySettings.TOURNAMENT_TEAMS_PER_HEAT,
                        BDArmorySettings.TOURNAMENT_VESSELS_PER_TEAM,
                        BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS,
                        BDArmorySettings.TOURNAMENT_STYLE
                    );
                }
                yield return new WaitWhile(() => ((BDATournament.Instance == null || BDATournament.Instance.tournamentID == 0) && Time.time - tic < 10)); // Wait for the tournament to be loaded or time out.
                if (BDATournament.Instance == null || BDATournament.Instance.tournamentID == 0) yield break;
                BDArmorySetup.windowBDAToolBarEnabled = true;
                LoadedVesselSwitcher.Instance.SetVisible(true);
                VesselSpawnerWindow.Instance.SetVisible(true);
                BDATournament.Instance.RunTournament();
            }
        }

        EvolutionWorkingState TryLoadEvolutionState()
        {
            Debug.Log("[BDArmory.BDATournament]: Attempting to auto-resume evolution.");
            EvolutionWorkingState evolutionState = null;
            evolutionState = BDAModuleEvolution.LoadState();
            if (string.IsNullOrEmpty(evolutionState.savegame)) { Debug.Log($"[BDArmory.BDATournament]: No savegame found in evolution state."); return null; }
            if (string.IsNullOrEmpty(evolutionState.evolutionId) || !File.Exists(Path.Combine(BDAModuleEvolution.configDirectory, evolutionState.evolutionId + ".cfg"))) { Debug.Log($"[BDArmory.BDATournament]: No saved evolution configured."); return null; }
            savegame = Path.Combine(savesDir, evolutionState.savegame, save + ".sfs");
            game = evolutionState.savegame;
            return evolutionState;
        }
        bool TryLoadTournamentState(out bool generateNewTournament)
        {
            generateNewTournament = false;
            // Check that there is an incomplete tournament, otherwise abort.
            bool incompleteTournament = false;
            if (File.Exists(TournamentState.defaultStateFile)) // Tournament state file exists.
            {
                var tournamentState = new TournamentState();
                if (!tournamentState.LoadState(TournamentState.defaultStateFile)) return false; // Failed to load
                savegame = Path.Combine(savesDir, tournamentState.savegame, save + ".sfs");
                if (File.Exists(savegame) && tournamentState.rounds.Select(r => r.Value.Count).Sum() - tournamentState.completed.Select(c => c.Value.Count).Sum() > 0) // Tournament state includes the savegame and has some rounds remaining —> Let's try resuming it! 
                {
                    incompleteTournament = true;
                    game = tournamentState.savegame;
                }
            }
            if (!incompleteTournament && BDArmorySettings.AUTO_GENERATE_TOURNAMENT_ON_RESUME) // Generate a new tournament based on the current settings.
            {
                generateNewTournament = true;
                game = BDArmorySettings.LAST_USED_SAVEGAME;
                savegame = Path.Combine(savesDir, game, save + ".sfs");
                if (File.Exists(savegame)) // Found a usable savegame and we assume the generated tournament will be usable. (It should just show error messages in-game otherwise.)
                    incompleteTournament = true;
            }
            return incompleteTournament;
        }
        bool TryLoadCleanSlate()
        {
            game = BDArmorySettings.LAST_USED_SAVEGAME;
            savegame = Path.Combine(savesDir, game, save + ".sfs");
            return true;
        }

        bool GenerateCleanGame()
        {
            // Grab the scenarios from the previous persistent game.
            HighLogic.CurrentGame = GamePersistence.LoadGame("persistent", game, true, false);
            var scenarios = HighLogic.CurrentGame.scenarios;

            if (BDArmorySettings.GENERATE_CLEAN_SAVE)
            {
                // Generate a new clean game and add in the scenarios.
                HighLogic.CurrentGame = new Game();
                HighLogic.CurrentGame.startScene = GameScenes.SPACECENTER;
                HighLogic.CurrentGame.Mode = Game.Modes.SANDBOX;
                HighLogic.SaveFolder = game;
                foreach (var scenario in scenarios) { CheckForScenario(scenario.moduleName, scenario.targetScenes); }

                // Generate the default roster and make them all badass pilots.
                HighLogic.CurrentGame.CrewRoster = KerbalRoster.GenerateInitialCrewRoster(HighLogic.CurrentGame.Mode);
                foreach (var kerbal in HighLogic.CurrentGame.CrewRoster.Kerbals(ProtoCrewMember.RosterStatus.Available))
                {
                    kerbal.isBadass = true; // Make them badass.
                    KerbalRoster.SetExperienceTrait(kerbal, KerbalRoster.pilotTrait); // Make the kerbal a pilot (so they can use SAS properly).
                    KerbalRoster.SetExperienceLevel(kerbal, KerbalRoster.GetExperienceMaxLevel()); // Make them experienced.
                }
            }
            else
            {
                GamePersistence.UpdateScenarioModules(HighLogic.CurrentGame);
            }
            // Update the game state and save it to the persistent save (sine that's what eventually ends up getting loaded when we call Start()).
            HighLogic.CurrentGame.Updated();
            GamePersistence.SaveGame("persistent", game, SaveMode.OVERWRITE);
            HighLogic.CurrentGame.Start();
            return true;
        }

        bool LoadGame()
        {
            var gameNode = GamePersistence.LoadSFSFile(save, game);
            if (gameNode == null)
            {
                Debug.LogWarning($"[BDArmory.BDATournament]: Unable to load the save game: {savegame}");
                return false;
            }
            Debug.Log($"[BDArmory.BDATournament]: Loaded save game: {savegame}");
            KSPUpgradePipeline.Process(gameNode, game, SaveUpgradePipeline.LoadContext.SFS, OnLoadDialogPiplelineFinished, (opt, n) => Debug.LogWarning($"[BDArmory.BDATournament]: KSPUpgradePipeline finished with error: {savegame}"));
            return true;
        }

        void OnLoadDialogPiplelineFinished(ConfigNode node)
        {
            HighLogic.CurrentGame = GamePersistence.LoadGameCfg(node, game, true, false);
            if (HighLogic.CurrentGame == null) return;
            if (GamePersistence.UpdateScenarioModules(HighLogic.CurrentGame))
            {
                if (node != null)
                { GameEvents.onGameStatePostLoad.Fire(node); }
                GamePersistence.SaveGame(HighLogic.CurrentGame, save, game, SaveMode.OVERWRITE);
            }
            HighLogic.CurrentGame.startScene = GameScenes.SPACECENTER;
            HighLogic.SaveFolder = game;
            HighLogic.CurrentGame.Start();
        }

        /// <summary>
        /// Look for the scenario in the currently loaded assemblies and add the scenario to the requested scenes.
        /// These come from a previous persistent.sfs save.
        /// </summary>
        /// <param name="scenarioName">Name of the scenario.</param>
        /// <param name="targetScenes">The scenes the scenario should be present in.</param>
        void CheckForScenario(string scenarioName, List<GameScenes> targetScenes)
        {
            foreach (var assy in AssemblyLoader.loadedAssemblies)
            {
                foreach (var type in assy.assembly.GetTypes())
                {
                    if (type == null) continue;
                    if (type.Name == scenarioName)
                    {
                        HighLogic.CurrentGame.AddProtoScenarioModule(type, targetScenes.ToArray());
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Check the non-native memory usage and automatically quit if it's above the configured threshold.
        /// Note: only the managed (non-native) memory is checked, the amount of native memory may or may not be comparable to the amount of non-native memory. FIXME This needs checking in a long tournament.
        /// </summary>
        /// <returns></returns>
        public bool CheckMemoryUsage()
        {
            if ((!BDArmorySettings.AUTO_RESUME_TOURNAMENT && !BDArmorySettings.AUTO_RESUME_EVOLUTION) || BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD > BDArmorySetup.SystemMaxMemory) return false; // Only trigger if Auto-Resume Tournaments is enabled and the Quit Memory Usage Threshold is set.
            memoryUsage = 0; // Trigger recalculation of memory usage.
            if (memoryUsage >= BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD)
            {
                if (BDACompetitionMode.Instance != null) BDACompetitionMode.Instance.competitionStatus.Add("Quitting in 3s due to memory usage threshold reached.");
                Debug.LogWarning($"[BDArmory.BDATournament]: Quitting KSP due to reaching Auto-Quit Memory Threshold: {memoryUsage} / {BDArmorySettings.QUIT_MEMORY_USAGE_THRESHOLD}GB");
                StartCoroutine(AutoQuitCoroutine(3)); // Trigger quit in 3s to give the tournament coroutine time to stop and the message to be shown.
                return true;
            }
            return false;
        }

        public static void AutoQuit(float delay = 1) => Instance.StartCoroutine(Instance.AutoQuitCoroutine(delay));

        /// <summary>
        /// Automatically quit KSP after a delay.
        /// </summary>
        /// <param name="delay"></param>
        /// <returns></returns>
        IEnumerator AutoQuitCoroutine(float delay = 1)
        {
            yield return new WaitForSeconds(delay);
            HighLogic.LoadScene(GameScenes.MAINMENU);
            yield return new WaitForSeconds(0.5f); // Pause on the Main Menu a moment, then quit.
            Debug.Log("[BDArmory.BDATournament]: Quitting KSP.");
            Application.Quit();
        }
    }
}