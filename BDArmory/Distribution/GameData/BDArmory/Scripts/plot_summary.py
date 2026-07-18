#!/usr/bin/env python3

# Standard Library
import argparse
import csv
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Union

# Third Party
import matplotlib.pyplot as plt
import numpy

VERSION = "6.3"

parser = argparse.ArgumentParser(description="Plot the scores of a tournament as they accumulated per round", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument("tournament", nargs="?", type=str, help="The tournament to plot (optional).")
parser.add_argument('-t', '--title', type=str, help="A title.")
parser.add_argument('-s', '--save', type=str, nargs='?', const='tmp', help="Save a PNG image instead of displaying the graph.")
parser.add_argument('--transparent', action='store_true', help='Save the PNG image with a transparent background.')
parser.add_argument("--version", action='store_true', help="Show the script version, then exit.")
parser.add_argument("-cz", '--cut-zero', action='store_true', help="Cut the y axis off at zero to avoid large negative scores.")
parser.add_argument('-i', '--ignore', type=str, help="Ignore craft matching the regular expression search term.")
args = parser.parse_args()

if args.version:
    print(f"Version: {VERSION}")
    sys.exit()


def naturalSortKey(key: Union[str, Path]):
    if isinstance(key, Path):
        key = key.name
    try:
        return int(key.rsplit(' ')[1])  # If the key ends in an integer, split that off and use that as the sort key.
    except:
        return key  # Otherwise, just use the key.


if args.tournament is None:
    tournamentFolders = sorted(list(dir for dir in ((Path(__file__).parent.parent / "Logs").resolve().glob("Tournament*")) if dir.is_dir()), key=naturalSortKey)
    tournamentDir = tournamentFolders[-1] if len(tournamentFolders) > 0 else Path('.')
else:
    tournamentDir = Path(args.tournament)

with open(tournamentDir / "summary.csv", 'r', encoding='utf-8') as f:
    data = list(csv.reader(f))
vessel_count = data.index([]) - 1
names = [data[row][0] for row in range(len(data) - vessel_count, len(data))]
scores = numpy.array([[float(v) for v in data[row][1:]] for row in range(len(data) - vessel_count, len(data))])
if args.ignore:
    keep = [re.search(args.ignore, name) == None for name in names]
    names = [name for i,name in enumerate(names) if keep[i]]
    scores = scores[keep]
plt.figure(figsize=(16, 10), dpi=200)
plt.plot(scores.transpose(), linewidth=5)
plt.axhline(color='black')
plt.xlabel('round')
plt.ylabel('score')
if len(names) > 16:  # Roughly half the plot height, put them outside the graph
    plt.legend(names, loc='upper left', bbox_to_anchor=(1, 1))
else:
    plt.legend(names, loc='upper left')
plt.autoscale(enable=True, tight=True)
plt.tight_layout()
if args.cut_zero:
    y0, y1 = plt.ylim()
    plt.ylim(max(y0, 0), y1)
if args.title is not None:
    plt.title(args.title)
if args.save:
    if args.save == 'tmp':
        fd, filename = tempfile.mkstemp(suffix='.png')
    else:
        filename = args.save
    plt.savefig(filename, dpi='figure', bbox_inches='tight', transparent=args.transparent)
    print(f"Image saved to {filename}")
    try:
        subprocess.run(['display', filename])
    except:
        pass
else:
    plt.show(block=True)
