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

    # Rounds (configurations for spawning and teams)
    state["rounds"] = {f"Round {i}": json.loads(rnd) for i, rnd in enumerate(state["_rounds"])}
    for rnd in state["rounds"].values():
        rnd["teams"] = [json.loads(team)["team"] for team in rnd["_teams"]]
        del rnd["_teams"]

    del state["_rounds"]

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
                field: {other_player: values for other_player, values in zip(score_data["players"], score_data[field]) if other_player != player}
                    for field in ("hitCounts", "damageFromGuns", "damageFromRockets", "rocketPartDamageCounts", "rocketStrikeCounts", "rammingPartLossCounts", "damageFromMissiles", "missilePartDamageCounts", "missileHitCounts", "battleDamageFrom")
            } | {
                "damageTypesTaken": score_data["damageTypesTaken"],
                "everyoneWhoDamagedMe": score_data["everyoneWhoDamagedMe"]
            } for score_data in [json.loads(rnd) for rnd in json.loads(scores[player])["serializedScoreData"]]
        ] for player in scores
    }
    state["scores"] = _scores

    with open(json_file, "w") as f:
        json.dump(state, f, indent=2)

    if args.print:
        print(json.dumps(state, indent=2))

else:  # Re-encode the tournament.json to a tournament.state file
    raise NotImplementedError("JSON -> state conversion not yet implemented.")
    with open(json_file, "r") as f:
        state = json.load(f)

    # Rounds
    # Scores

    with open(state_file, "w") as f:
        json.dump(state, f, indent=2)