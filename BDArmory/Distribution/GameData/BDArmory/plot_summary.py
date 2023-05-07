#!/usr/bin/env python3

# Standard library imports
import argparse
import csv
import sys
import tempfile
from pathlib import Path
from typing import Union

# Third party imports
import matplotlib.pyplot as plt
import numpy

VERSION = "1.4"

parser = argparse.ArgumentParser(description="Plot the scores of a tournament as they accumulated per round", formatter_class=argparse.ArgumentDefaultsHelpFormatter)
parser.add_argument("tournament", nargs="?", type=str, help="The tournament to plot (optional).")
parser.add_argument('-t', '--title', type=str, help="A title.")
parser.add_argument('-s', '--save', type=str, nargs='?', const='tmp', help="Save a PNG image instead of displaying the graph.")
parser.add_argument('--transparent', action='store_true', help='Save the PNG image with a transparent background.')
parser.add_argument("--version", action='store_true', help="Show the script version, then exit.")
parser.add_argument("-cz", '--cut-zero', action='store_true', help="Cut the y axis off at zero to avoid large negative scores.")
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
    tournamentFolders = sorted(list(dir for dir in ((Path(__file__).parent / "Logs").resolve().glob("Tournament*")) if dir.is_dir()), key=naturalSortKey)
    tournamentDir = tournamentFolders[-1] if len(tournamentFolders) > 0 else Path('.')
else:
    tournamentDir = Path(args.tournament)

with open(tournamentDir / "summary.csv", 'r') as f:
    data = list(csv.reader(f))
vessel_count = data.index([]) - 1
names = [data[row][0] for row in range(len(data) - vessel_count, len(data))]
scores = numpy.array([[float(v) for v in data[row][1:]] for row in range(len(data) - vessel_count, len(data))])
plt.figure(figsize=(16, 10), dpi=200)
plt.plot(scores.transpose(), linewidth=5)
plt.legend(names, loc='upper left')
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
else:
    plt.show(block=True)
