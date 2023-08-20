# Standard library imports
import argparse
import json
from pathlib import Path

VERSION = "1.0.0"

parser = argparse.ArgumentParser(
    description="Tournament state parser",
    formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    epilog="The tournament.state file is recursively encoded JSON instead of proper JSON due to Unity's simplistic JSONUtility functionality. This script decodes it and converts the result to proper JSON, saving it to tournament.json."
)
parser.add_argument("state", type=Path, nargs="?", help="The tournament.state file.")
parser.add_argument("-p", "--print", action="store_true", help="Print the JSON to the console.")
parser.add_argument("-r", "--re-encode", action="store_true", help="Re-encode the tournament.json file back to the tournament.state file.")
args = parser.parse_args()

if args.state is None:
    args.state = Path(__file__).parent / "PluginData" / "tournament.state"
state_file: Path = args.state
json_file: Path = state_file.with_suffix(".json")

if not args.re_encode:  # Decode the tournament.state to pure JSON and optionally print it.
    with open(args.state, "r") as f:
        state = json.load(f)

    # Various elements are recursively encoded in JSON strings due to Unity's limited JSONUtility functionality.
    # We decode and organise them here.

    # Heats (configurations for spawning and teams)
    state["heats"] = {f"Heat {i}": json.loads(rnd) for i, rnd in enumerate(state["_heats"])}
    for heat in state["heats"].values():
        heat["teams"] = [json.loads(team)["team"] for team in heat["_teams"]]
        del heat["_teams"]
    del state["_heats"]

    # Scores
    _scores = json.loads(state["_scores"])
    del state["_scores"]
    _scores["weights"] = {k: v for k, v in zip(_scores["_weightKeys"], _scores["_weightValues"])}
    del _scores["_weightKeys"], _scores["_weightValues"]
    players = _scores["_players"]
    del _scores["_players"]
    _scores["scores"] = {p: s for p, s in zip(players, _scores["_scores"])}
    del _scores["_scores"]
    _scores["files"] = {p: s for p, s in zip(players, _scores["_files"])}
    del _scores["_files"]
    results = [json.loads(results) for results in _scores["_results"]]
    for result in results:
        result["survivingTeams"] = [json.loads(team) for team in result["_survivingTeams"]]
        del result["_survivingTeams"]
        result["deadTeams"] = [json.loads(team) for team in result["_deadTeams"]]
        del result["_deadTeams"]
    _scores["results"] = results
    del _scores["_results"]
    scores = _scores["scores"]
    _scores["scores"] = {
        player:
        [
            json.loads(score_data["scoreData"]) | {
                field: {other_player: values for other_player, values in zip(players, score_data[field]) if other_player != player}
                    for field in ("hitCounts", "damageFromGuns", "damageFromRockets", "rocketPartDamageCounts", "rocketStrikeCounts", "rammingPartLossCounts", "damageFromMissiles", "missilePartDamageCounts", "missileHitCounts", "battleDamageFrom")
            } | {
                "damageTypesTaken": score_data["damageTypesTaken"],
                "everyoneWhoDamagedMe": score_data["everyoneWhoDamagedMe"]
            } for score_data in [json.loads(rnd) for rnd in json.loads(scores[player])["serializedScoreData"]]
        ] for player in players
    }
    state["scores"] = _scores

    # Team files
    state["teamFiles"] = [json.loads(team)["ls"] for team in state["_teamFiles"]]
    del state["_teamFiles"]

    with open(json_file, "w") as f:
        json.dump(state, f, indent=2)

    if args.print:
        print(json.dumps(state, indent=2))

else:  # Re-encode the tournament.json to a tournament.state file
    with open(json_file, "r") as f:
        state = json.load(f)
    separators = (',', ':')

    # Heats
    for heat in state["heats"].values():
        heat["_teams"] = [json.dumps({"team": team}, separators=separators) for team in heat["teams"]]
        del heat["teams"]
    state["_heats"] = [json.dumps(heat, separators=separators) for heat in state["heats"].values()]
    del state["heats"]

    # Scores
    scores = state["scores"]
    scores["_weightKeys"] = list(scores["weights"].keys())
    scores["_weightValues"] = list(scores["weights"].values())
    scores["_players"] = list(scores["scores"].keys())
    players = scores["_players"]
    _scores = [scores["scores"][player] for player in players]
    scores["_files"] = [scores["files"][player] for player in players]
    results = scores["results"]
    for result in results:
        result["_survivingTeams"] = [json.dumps(team, separators=separators) for team in result["survivingTeams"]]
        result["_deadTeams"] = [json.dumps(team, separators=separators) for team in result["deadTeams"]]
        del result["survivingTeams"], result["deadTeams"]
    scores["_results"] = [json.dumps(result, separators=separators) for result in results]
    _scores = [
        json.dumps({"serializedScoreData": [
            json.dumps({
                "scoreData": json.dumps(
                    {
                        field: heat[field] for field in heat if field not in ("hitCounts", "damageFromGuns", "damageFromRockets", "rocketPartDamageCounts", "rocketStrikeCounts", "rammingPartLossCounts", "damageFromMissiles", "missilePartDamageCounts", "missileHitCounts", "battleDamageFrom", "damageTypesTaken", "everyoneWhoDamagedMe")
                    }, separators=separators)
            } | {
                field: [heat[field][player] if player in heat[field] else 0 for player in players] for field in ("hitCounts", "damageFromGuns", "damageFromRockets", "rocketPartDamageCounts", "rocketStrikeCounts", "rammingPartLossCounts", "damageFromMissiles", "missilePartDamageCounts", "missileHitCounts", "battleDamageFrom")
            } | {
                field: heat[field] for field in ("damageTypesTaken", "everyoneWhoDamagedMe")
            }, separators=separators)
            for heat in playerScores
        ]}, separators=separators) for playerScores in _scores
    ]
    scores["_scores"] = _scores
    del scores["weights"], scores["scores"], scores["files"], scores["results"]
    state["_scores"] = json.dumps(scores, separators=separators)
    del state["scores"]

    # Team files
    state["_teamFiles"] = [json.dumps({"ls": team}, separators=separators) for team in state["teamFiles"]]
    del state["teamFiles"]

    with open(state_file, "w") as f:
        json.dump(state, f, separators=separators)
