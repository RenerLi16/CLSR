"""Run against the instructor's simulator. Pass the directory containing sim_v10.py."""
import argparse
import json
import sys
from copy import deepcopy
from pathlib import Path


def follow_reports(sim, use_update):
    """Rank unvisited branches by the latest visible report and send one full team.

    Within a branch, use the farthest site by public trail depth, breaking ties by
    authored map order. Do not inspect crossing costs or survivors to pick a route.
    """
    visited = set()
    pool, rescued, claimed = sim.pool, 0, 0
    for day in range(1, sim.days + 1):
        points = min(pool, sim.cap)
        if not points or len(visited) == len(sim.branches):
            break
        fresh = [b for b in sim.branches if b["root"] not in visited]
        branch = max(fresh, key=lambda b: sim.reported(b, day if use_update else 1))
        root = branch["root"]
        target = max((n for n in sim.non_camp if sim.paths[n][0] == root),
                     key=lambda n: len(sim.paths[n]))
        visited.add(root)
        _, rescued, claimed, reward, left, _ = sim.apply(day, ((target, points),), rescued, claimed, pool)
        pool = left + reward
    return sim.count(rescued)


def opening_scores(sim, targets):
    rows = dict(sim.first_day_rows())
    scores = {}
    for target in targets:
        points = sum(sim.cost(node) for node in sim.paths[target])
        plan = ((target, points),)
        immediate = sim.apply(1, plan, 0, 0, sim.pool)[0]
        scores[target] = {"points": points, "rescued_today": immediate, "best_final": rows[plan]}
    return scores


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("instructor_package", type=Path)
    args = parser.parse_args()
    sys.path.insert(0, str(args.instructor_package.resolve()))
    from sim_v10 import Sim

    maps = json.loads(Path(__file__).with_name("scenarios_v10.json").read_text())
    pairs = {"Cedar Valley": ("W3", "W4"), "Blackridge Storm Rescue": ("Lakeside Camp", "Weather Station")}
    for name, spec in maps["scenarios"].items():
        sim = Sim(spec, maps["mode"])
        plain, updated = [follow_reports(sim, enabled) for enabled in (False, True)]
        assert updated >= plain + 2, f"{name}: timed report gain below two"
        pair = pairs[name]
        alternate = deepcopy(spec)
        nodes = {node["id"]: node for node in alternate["nodes"]}
        a, b = (nodes[site] for site in pair)
        assert a["parent"] == b["parent"]
        a["surv"], b["surv"] = b["surv"], a["surv"]
        actual = opening_scores(sim, pair)
        swapped = opening_scores(Sim(alternate, maps["mode"]), pair)
        assert max(pair, key=lambda i: actual[i]["rescued_today"]) != max(pair, key=lambda i: swapped[i]["rescued_today"])
        print(name, json.dumps({"without_report": plain, "with_report_after_day_1": updated,
                                "authored": actual, "sibling_swap": swapped}, ensure_ascii=False))


if __name__ == "__main__":
    main()
