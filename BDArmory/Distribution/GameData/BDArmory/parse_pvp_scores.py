#!/usr/bin/env python3

# Standard library imports
import argparse
import json
import math
import sys
import traceback
from pathlib import Path
from typing import Union

# Third party imports
import matplotlib.pyplot as plt

VERSION = "1.2.0"

parser = argparse.ArgumentParser(description="PVP score parser", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument('tournament', type=str, nargs='*', help="Tournament folder to parse.")
parser.add_argument('-c', '--current-dir', action='store_true', help="Parse the logs in the current directory as if it was a tournament without the folder structure.")
parser.add_argument('--csv', action='store_true', help="Create a CSV file with the PVP scores for the entire tournament.")
parser.add_argument('--plot', action='store_true', help="Plot a diagram with of the overall PVP scores.")
parser.add_argument("--version", action='store_true', help="Show the script version, then exit.")
args = parser.parse_args()

if args.version:
    print(f"Version: {VERSION}")
    sys.exit()


def CalculateAccuracy(hits, shots): return 100 * hits / shots if shots > 0 else 0


def CalculateAvgHP(hp, heats): return hp / heats if heats > 0 else 0


def cumsum(l):
    v = 0
    for i in l:
        v += i
        yield v


def naturalSortKey(key: Union[str, Path]):
    if isinstance(key, Path):
        key = key.name
    try:
        return int(key.rsplit(' ')[1])  # If the key ends in an integer, split that off and use that as the sort key.
    except:
        return key  # Otherwise, just use the key.


if args.current_dir and len(args.tournament) == 0:
    tournamentDirs = [Path('')]
else:
    if len(args.tournament) == 0:
        tournamentDirs = None
        logsDir = Path(__file__).parent / "Logs"
        if logsDir.exists():
            tournamentFolders = list(logsDir.resolve().glob("Tournament*"))
            if len(tournamentFolders) > 0:
                tournamentFolders = sorted(list(dir for dir in tournamentFolders if dir.is_dir()), key=naturalSortKey)
            if len(tournamentFolders) > 0:
                tournamentDirs = [tournamentFolders[-1]]  # Latest tournament dir
        if tournamentDirs is None:  # Didn't find a tournament dir, revert to current-dir
            tournamentDirs = [Path('')]
            args.current_dir = True
    else:
        tournamentDirs = [Path(tournamentDir) for tournamentDir in args.tournament]  # Specified tournament dir


for tournamentNumber, tournamentDir in enumerate(tournamentDirs):
    try:
        with open(tournamentDir / "summary.json", 'r') as f:
            summary = json.load(f)
        with open(tournamentDir / "results.json", 'r') as f:
            results = json.load(f)
        weights = {k: w for k, w in summary['meta']['score weights'].items() if w != 0}
        pvp_data = {}
        pvp_score = {'score weights': weights}
        for stage_index, stage in results.items():
            pvp_data[stage_index] = {}
            pvp_score[stage_index] = {}
            for heat_index, heat in stage.items():
                pvp_data[stage_index][heat_index] = {}
                number_of_opponents = len(heat['craft']) - 1
                for craft, stats in heat['craft'].items():
                    if craft not in pvp_score[stage_index]:
                        pvp_score[stage_index][craft] = {}
                    pvp_data[stage_index][heat_index][craft] = {}
                    pvp_data[stage_index][heat_index][craft]['shared'] = {
                        'wins': 1 if heat['result']['result'] == "Win" and craft in next(iter(heat['result']['teams'].values())).split(", ") else 0,
                        'survivedCount': 1 if craft in heat['craft'] and stats['state'] == 'ALIVE' else 0,
                        'miaCount': 1 if craft in heat['craft'] and stats['state'] == 'MIA' else 0,
                        'deathCount': 1 if stats['state'] == 'DEAD' else 0,
                        'deathOrder': stats['deathOrder'] / len(heat['craft']) if craft in heat['craft'] and 'deathOrder' in stats else 1,
                        'deathTime': stats['deathTime'] if 'deathTime' in stats else heat['duration'],
                        'HPremaining': CalculateAvgHP(stats['HPremaining'] if 'HPremaining' in stats and stats['state'] == 'ALIVE' else 0, 1 if stats['state'] == 'ALIVE' else 0),
                        'accuracy': CalculateAccuracy(stats['hits'] if 'hits' in stats else 0, stats['shots'] if 'shots' in stats else 0),
                        'rocket_accuracy': CalculateAccuracy(stats['rocket_strikes'] if 'rocket_strikes' in stats else 0, stats['rockets_fired'] if 'rockets_fired' in stats else 0),
                    }
                    score_vs_all = sum(weights[k] * pvp_data[stage_index][heat_index][craft]['shared'].get(k, 0) for k in weights)  # To be shared amongst all opponents.
                    pvp_data[stage_index][heat_index][craft]['individual'] = {}
                    for opponent, data in heat['craft'].items():
                        if opponent == craft:
                            continue
                        pvp_data[stage_index][heat_index][craft]['individual'][opponent] = {
                            'cleanKills': 1 if any((field in data and data[field] == craft) for field in ('cleanKillBy', 'cleanRocketKillBy', 'cleanMissileKillBy', 'cleanRamKillBy')) else 0,
                            'assists': 1 if data['state'] == 'DEAD' and any(field in data and craft in data[field] for field in ('hitsBy', 'rocketPartsHitBy', 'missilePartsHitBy', 'rammedPartsLostBy')) and not any((field in data) for field in ('cleanKillBy', 'cleanRocketKillBy', 'cleanMissileKillBy', 'cleanRamKillBy')) else 0,
                            'hits': data['hitsBy'][craft] if 'hitsBy' in data and craft in data['hitsBy'] else 0,
                            'hitsTaken': stats['hitsBy'][opponent] if 'hitsBy' in stats and opponent in stats['hitsBy'] else 0,
                            'bulletDamage': data['bulletDamageBy'][craft] if 'bulletDamageBy' in data and craft in data['bulletDamageBy'] else 0,
                            'bulletDamageTaken': stats['bulletDamageBy'][opponent] if 'bulletDamageBy' in stats and opponent in stats['bulletDamageBy'] else 0,
                            'rocketHits': data['rocketHitsBy'][craft] if 'rocketHitsBy' in data and craft in data['rocketHitsBy'] else 0,
                            'rocketHitsTaken': stats['rocketHitsBy'][opponent] if 'rocketHitsBy' in stats and opponent in stats['rocketHitsBy'] else 0,
                            'rocketPartsHit': data['rocketPartsHitBy'][craft] if 'rocketPartsHitBy' in data and craft in data['rocketPartsHitBy'] else 0,
                            'rocketPartsHitTaken': stats['rocketPartsHitBy'][opponent] if 'rocketPartsHitBy' in stats and opponent in stats['rocketPartsHitBy'] else 0,
                            'rocketDamage': data['rocketDamageBy'][craft] if 'rocketDamageBy' in data and craft in data['rocketDamageBy'] else 0,
                            'rocketDamageTaken': stats['rocketDamageBy'][opponent] if 'rocketDamageBy' in stats and opponent in stats['rocketDamageBy'] else 0,
                            'missileHits': data['missileHitsBy'][craft] if 'missileHitsBy' in data and craft in data['missileHitsBy'] else 0,
                            'missileHitsTaken': stats['missileHitsBy'][opponent] if 'missileHitsBy' in stats and opponent in stats['missileHitsBy'] else 0,
                            'missilePartsHit': data['missilePartsHitBy'][craft] if 'missilePartsHitBy' in data and craft in data['missilePartsHitBy'] else 0,
                            'missilePartsHitTaken': stats['missilePartsHitBy'][opponent] if 'missilePartsHitBy' in stats and opponent in stats['missilePartsHitBy'] else 0,
                            'missileDamage': data['missileDamageBy'][craft] if 'missileDamageBy' in data and craft in data['missileDamageBy'] else 0,
                            'missileDamageTaken': stats['missileDamageBy'][opponent] if 'missileDamageBy' in stats and opponent in stats['missileDamageBy'] else 0,
                            'ramScore': data['rammedPartsLostBy'][craft] if 'rammedPartsLostBy' in data and craft in data['rammedPartsLostBy'] else 0,
                            'ramScoreTaken': stats['rammedPartsLostBy'][opponent] if 'rammedPartsLostBy' in stats and opponent in stats['rammedPartsLostBy'] else 0,
                            'battleDamage': data['battleDamageBy'][craft] if 'battleDamageBy' in data and craft in data['battleDamageBy'] else 0,
                            'battleDamageTaken': stats['battleDamageBy'][opponent] if 'battleDamageBy' in stats and opponent in stats['battleDamageBy'] else 0,
                        }
                        score_vs_opponent = sum(weights[k] * pvp_data[stage_index][heat_index][craft]['individual'][opponent].get(k, 0) for k in weights)
                        pvp_score[stage_index][craft][opponent] = pvp_score[stage_index][craft].get(opponent, 0) + score_vs_opponent + score_vs_all / number_of_opponents

        # Add in a totals over the entire tournament entry.
        players = list(set().union(*[set(round_data.keys()) for round_index, round_data in pvp_score.items() if round_index.startswith('Round')]))
        score_totals = {player1: {player2: sum(round_data.get(player1, {}).get(player2, 0) for round_index, round_data in pvp_score.items() if round_index.startswith('Round')) for player2 in players} for player1 in players}  # Combine scores over all rounds
        players = sorted(players, key=lambda p: sum(score_totals[p].values()), reverse=True)  # Sort by overall rank
        pvp_score['totals'] = score_totals

        with open(tournamentDir / "pvp_scores.json", 'w') as f:
            json.dump(pvp_score, f, indent=2)

        if args.csv:
            lines = ['Player,' + ','.join(players) + ',Sum'] + [f'{player},' + ','.join(str(s) for s in score_totals[player].values()) + f",{sum(score_totals[player].values())}" for player in players]
            with open(tournamentDir / "pvp_scores.csv", 'w') as f:
                f.write('\n'.join(lines))
        
        if args.plot:
            grand_totals = {player: sum(scores.values()) for player, scores in score_totals.items()}
            players = sorted(grand_totals, key=lambda k: grand_totals[k], reverse=True)
            L = len(score_totals)
            nodes = {players[i]:(math.sin(math.pi*2*i/L), math.cos(math.pi*2*i/L)) for i in range(L)}
            edges = {p0:{p1:(nodes[p0], nodes[p1], score_totals[p0][p1]) for p1 in players} for p0 in players}
            colourmap = plt.get_cmap('hsv')
            colours = {players[i]: colourmap(i/L) for i in range(L)}
            plt.figure(figsize=(16, 10), dpi=200)
            lines = []
            for p0 in players:
                for p1 in players:
                    if p0==p1:continue
                    lines.append(([edges[p0][p1][0][0], edges[p0][p1][1][0]], [edges[p0][p1][0][1], edges[p0][p1][1][1]], colours[p0], edges[p0][p1][2])) # x, y, colour, width
            lines = sorted(lines, key=lambda l:l[3], reverse=True)
            minWidth = min([line[3] for line in lines])
            maxWidth = max([line[3] for line in lines])
            for p in players:
                plt.plot(nodes[p][0], nodes[p][1], color=colours[p], marker='*', markersize=20)
            for line in lines:
                plt.plot(line[0], line[1], color=line[2], linewidth=line[3]*10/(maxWidth-minWidth)+minWidth+2, solid_capstyle='round')
            plt.legend(players, loc='upper right')
            plt.axis('equal')
            plt.show()

    except Exception as e:
        print(f"Failed to parse {tournamentDir}. Have you run the tournament parser on it first?")
        traceback.print_exc()
        continue
