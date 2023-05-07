#!/usr/bin/env python3

# Standard library imports
import argparse
import json
import sys
from collections import Counter
from pathlib import Path

VERSION = "1.1"

parser = argparse.ArgumentParser(description="Parse results.json of a N-choose-K style tournament producing a table of who-beat-who.", formatter_class=argparse.ArgumentDefaultsHelpFormatter, epilog="Note: this also works on FFA style tournaments, but may not be meaningful.")
parser.add_argument('results', type=str, nargs='?', help="results.json file to parse.")
parser.add_argument('-o', '--output', default="n-choose-k.csv", help="File to output CSV to.")
parser.add_argument('--tsv', action='store_true', help="Output to a TSV (tab-separated values) file instead of a CSV file.")
parser.add_argument("--version", action='store_true', help="Show the script version, then exit.")
args = parser.parse_args()

if args.version:
    print(f"Version: {VERSION}")
    sys.exit()

if args.results is None:
    logsDir = Path(__file__).parent / "Logs"
    if logsDir.exists():
        tournamentFolders = list(logsDir.resolve().glob("Tournament*"))
        if len(tournamentFolders) > 0:
            tournamentFolders = sorted(list(dir for dir in tournamentFolders if dir.is_dir()))
        if len(tournamentFolders) > 0:
            args.results = tournamentFolders[-1] / "results.json"  # Results in latest tournament dir

if args.results is not None:
    results_file = Path(args.results)
    if not results_file.exists():
        print(f"File not found: {results_file}")
    else:
        with open(results_file, 'r') as f:
            data = json.load(f)
        counts = Counter([(next(iter(heat['result']['teams'].keys())), next(iter(heat['result']['dead teams'].keys()))) for Round in data.values() for heat in Round.values() if heat['result']['result'] == 'Win'])
        A = set(k[0] for k in counts.keys())
        B = set(k[1] for k in counts.keys())
        names = sorted(A.union(B))
        name_map = {n: c for c, n in enumerate(names)}
        t = [[0] * len(names) for i in range(len(names))]
        for k, c in counts.items():
            t[name_map[k[0]]][name_map[k[1]]] = c
        output_file = Path(args.output)
        if args.tsv:
            output_file = output_file.with_suffix(".tsv")
        with open(output_file, 'w') as f:
            separator = "," if not args.tsv else "\t"
            f.write("vs" + separator + separator.join(names) + separator * 2 + "sum(wins)\n")
            for i, name in enumerate(names):
                f.write(name + separator + separator.join([str(c) for c in t[i]]) + separator * 2 + str(sum(t[i])) + "\n")
            f.write(separator * (len(names) + 2))
            f.write("\nsum(losses)" + separator + separator.join([str(sum(t[i][j] for i in range(len(names)))) for j in range(len(names))]) + separator * 2 + "\n")
