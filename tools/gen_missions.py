#!/usr/bin/env python3
"""Writes the campaign's mission files.

The three regions share fifteen mission patterns; what changes is where on the island each
one happens, the weather, the pay and the clock. Edit the tables below and run from the
repository root:  python3 tools/gen_missions.py
"""
import json
import os

OUT = "Assets/Skyforge/Resources/Missions"


def at(anchor, dx=0, dy=0, dz=0):
    place = {"anchor": anchor}
    for key, value in (("dx", dx), ("dy", dy), ("dz", dz)):
        if value:
            place[key] = value
    return place


TAKEOFF = {"type": "takeoff"}
HOME = {"type": "land", "at": at("pad"), "radius": 8}


def scan(anchor, dy=18, radius=12, height=14, hold=3, text=None):
    objective = {"type": "scan", "at": at(anchor, dy=dy), "radius": radius, "height": height, "hold": hold}
    if text:
        objective["text"] = text
    return objective


# Per region: a short prefix for mission ids, a pay multiplier, and where each pattern happens.
REGIONS = {
    "arvelia": dict(
        prefix="", pay=1.0,
        scan=["beacon0", "beacon1", "beacon2"], camp="poi10", boat="sea3",
        photo=["poi0", "poi1", "poi9", "coast7"], drop="poi4", source="poi7",
        relay="poi5", radars=["poi3", "poi6"], tail=["coast7", "coast0", "coast1"],
        van="poi2", convoy=["coast6", "poi10", "poi11"], storm=["poi0", "poi10"],
        night=["poi1", "poi9"], mover=["coast0", "coast1"], haul="poi8",
        special=dict(radars=["poi3", "poi6"], papers="poi4", van="poi2"),
        weather=dict(scan="clear", recon="sunset", photo="clear", race="overcast", delivery="overcast",
                     search="fog", stealth="sunset", tail="overcast", strike="dust", escort="rain",
                     storm="storm", night="night", mover="clear", haul="rain", special="sunset"),
    ),
    "kharad": dict(
        prefix="kh_", pay=1.5,
        scan=["beacon0", "beacon2", "beacon1"], camp="poi1", boat="sea0",
        photo=["poi11", "poi10", "poi2", "coast0"], drop="poi8", source="poi4",
        relay="poi6", radars=["poi0", "poi9"], tail=["coast0", "coast7", "coast6"],
        van="poi3", convoy=["coast1", "poi1", "poi0"], storm=["poi11", "poi1"],
        night=["poi10", "poi2"], mover=["coast7", "coast6"], haul="poi7",
        special=dict(radars=["poi0", "poi9"], papers="poi10", van="poi3"),
        weather=dict(scan="clear", recon="clear", photo="sunset", race="sunset", delivery="clear",
                     search="dust", stealth="night", tail="sunset", strike="clear", escort="dust",
                     storm="dust", night="night", mover="sunset", haul="clear", special="night"),
    ),
    "nordheim": dict(
        # Steep ground: the radars sit higher so there is room to fly under them.
        prefix="nh_", pay=2.2, clock=1.25, floor=38,
        scan=["beacon1", "beacon0", "beacon2"], camp="poi0", boat="sea3",
        photo=["poi10", "poi11", "poi9", "coast7"], drop="poi6", source="poi8",
        relay="poi5", radars=["poi3", "poi6"], tail=["coast7", "coast0", "coast1"],
        van="poi9", convoy=["coast6", "poi10", "poi11"], storm=["poi10", "poi0"],
        night=["poi0", "poi2"], mover=["coast1", "coast0"], haul="poi4",
        special=dict(radars=["poi3", "poi6"], papers="poi4", van="poi2"),
        weather=dict(scan="snow", recon="overcast", photo="snow", race="overcast", delivery="snow",
                     search="fog", stealth="night", tail="snow", strike="overcast", escort="snow",
                     storm="storm", night="night", mover="overcast", haul="snow", special="night"),
    ),
}

# (pattern, id, template, time goal, base reward, star reward)
PATTERNS = [
    ("scan", "first_contact", "scan", 240, 150, 50),
    ("recon", "coast_watch", "recon", 150, 180, 60),
    ("photo", "evidence", "photo", 200, 200, 60),
    ("race", "sprint", "race", 210, 220, 70),
    ("delivery", "supply_run", "delivery", 240, 240, 70),
    ("search", "lost_signal", "search", 260, 260, 80),
    ("stealth", "silent_path", "stealth", 300, 300, 90),
    ("tail", "shadow", "tail", 150, 320, 90),
    ("strike", "clean_strike", "strike", 200, 350, 100),
    ("escort", "safe_passage", "escort", 220, 380, 100),
    ("storm", "storm_front", "storm", 170, 420, 110),
    ("night", "night_watch", "recon", 200, 440, 110),
    ("mover", "moving_target", "strike", 270, 470, 120),
    ("haul", "long_haul", "delivery", 300, 500, 120),
    ("special", "broken_arrow", "special", 360, 650, 150),
]


def build(pattern, r):
    """Returns the parts of a mission that depend on the pattern."""
    if pattern == "scan":
        return dict(objectives=[TAKEOFF] + [scan(b) for b in r["scan"]] + [HOME])
    if pattern == "recon":
        return dict(
            props=[{"id": "camp", "kind": "camp", "at": at(r["camp"])}, {"id": "boat", "kind": "boat", "at": at(r["boat"])}],
            objectives=[TAKEOFF, {"type": "observe", "target": "camp", "range": 90, "hold": 4},
                        {"type": "observe", "target": "boat", "range": 110, "hold": 4}, HOME])
    if pattern == "photo":
        kinds = ["crates", "vehicle", "antenna", "transmitter"]
        props = [{"id": "item%d" % i, "kind": kinds[i], "at": at(a)} for i, a in enumerate(r["photo"])]
        return dict(props=props, objectives=[TAKEOFF] + [{"type": "photo", "target": p["id"], "range": 45} for p in props] + [HOME])
    if pattern == "race":
        gates = [{"type": "reach", "at": at("ring%d" % i), "radius": 7, "gate": True} for i in range(10)]
        return dict(objectives=[TAKEOFF] + gates + [HOME])
    if pattern == "delivery":
        return dict(payload=0.7, props=[{"id": "dropzone", "kind": "pad", "at": at(r["drop"])}],
                    objectives=[TAKEOFF, {"type": "land", "text": "objective.deliver", "target": "dropzone", "radius": 6},
                                {"type": "takeoff", "text": "objective.takeoff_again"}, HOME])
    if pattern == "search":
        return dict(props=[{"id": "source", "kind": "transmitter", "at": at(r["source"])}],
                    objectives=[TAKEOFF, {"type": "find", "target": "source", "radius": 16, "range": 700},
                                scan(r["source"], dy=10, radius=10, height=10, text="objective.scan_source"), HOME])
    if pattern == "stealth":
        return dict(props=[{"id": "relay", "kind": "antenna", "at": at(r["relay"])}],
                    zones=[{"at": at(a), "radius": 170, "floor": r.get("floor", 26)} for a in r["radars"]],
                    objectives=[TAKEOFF, scan(r["relay"], hold=4, text="objective.scan_relay"), HOME])
    if pattern == "tail":
        return dict(actors=[{"id": "courier", "kind": "truck", "path": [at(a) for a in r["tail"]], "speed": 7,
                             "startObjective": 1, "detectRange": 28}],
                    objectives=[TAKEOFF, {"type": "follow", "target": "courier", "min": 35, "max": 110, "hold": 45}, HOME])
    if pattern == "strike":
        return dict(props=[{"id": "command_van", "kind": "vehicle", "at": at(r["van"])}],
                    objectives=[TAKEOFF, {"type": "strike", "target": "command_van", "radius": 7, "minAltitude": 40, "shots": 1}, HOME])
    if pattern == "escort":
        a, b, c = r["convoy"]
        return dict(props=[{"id": "ambush1", "kind": "threat", "at": at(b, dz=6)}, {"id": "ambush2", "kind": "threat", "at": at(c, dz=6)}],
                    actors=[{"id": "convoy", "kind": "truck", "path": [at(a), at(b), at(c), at("pad", dx=45)], "speed": 5, "startObjective": 1}],
                    objectives=[TAKEOFF, {"type": "mark", "targets": ["ambush1", "ambush2"], "actor": "convoy", "range": 80, "hold": 3},
                                {"type": "arrive", "actor": "convoy"}, HOME])
    if pattern == "storm":
        gates = [{"type": "reach", "at": at(a, dy=20), "radius": 14, "gate": True} for a in r["storm"]]
        return dict(batteryStart=0.6, objectives=[TAKEOFF] + gates + [HOME])
    if pattern == "night":
        a, b = r["night"]
        return dict(props=[{"id": "camp", "kind": "camp", "at": at(a)}, {"id": "cache", "kind": "crates", "at": at(b)}],
                    objectives=[TAKEOFF, {"type": "observe", "target": "camp", "range": 110, "hold": 4},
                                {"type": "photo", "target": "cache", "range": 70}, HOME])
    if pattern == "mover":
        return dict(actors=[{"id": "runner", "kind": "car", "path": [at(a) for a in r["mover"]], "speed": 3, "startObjective": 1}],
                    objectives=[TAKEOFF, {"type": "strike", "text": "objective.strike_moving", "target": "runner",
                                          "radius": 9, "minAltitude": 35, "shots": 2}, HOME])
    if pattern == "haul":
        return dict(payload=1.0, props=[{"id": "outpost", "kind": "pad", "at": at(r["haul"])}],
                    objectives=[TAKEOFF, {"type": "land", "text": "objective.deliver", "target": "outpost", "radius": 6},
                                {"type": "takeoff", "text": "objective.takeoff_again"}, HOME])
    if pattern == "special":
        s = r["special"]
        return dict(props=[{"id": "papers", "kind": "crates", "at": at(s["papers"])}, {"id": "command_van", "kind": "vehicle", "at": at(s["van"])}],
                    zones=[{"at": at(a), "radius": 170, "floor": r.get("floor", 26)} for a in s["radars"]],
                    objectives=[TAKEOFF, {"type": "photo", "target": "papers", "range": 45},
                                {"type": "strike", "target": "command_van", "radius": 7, "minAltitude": 40, "shots": 1}, HOME])
    raise ValueError(pattern)


def main():
    count = 0
    for region, r in REGIONS.items():
        folder = os.path.join(OUT, region)
        os.makedirs(folder, exist_ok=True)
        for name in os.listdir(folder):
            if name.endswith(".json"):
                os.remove(os.path.join(folder, name))
        for order, (pattern, name, template, goal, base, star) in enumerate(PATTERNS, start=1):
            mission = {
                "id": r["prefix"] + name,
                "region": region,
                "order": order,
                "template": template,
                "weather": r["weather"][pattern],
                # Mountains make every route longer: a region may stretch the clock.
                "timeGoal": int(round(goal * r.get("clock", 1.0) / 10.0)) * 10,
                "baseReward": int(round(base * r["pay"] / 10.0)) * 10,
                "starReward": int(round(star * r["pay"] / 10.0)) * 10,
            }
            mission.update(build(pattern, r))
            with open(os.path.join(folder, mission["id"] + ".json"), "w") as handle:
                handle.write(json.dumps(mission, indent=2) + "\n")
            count += 1
    print("wrote", count, "missions")


if __name__ == "__main__":
    main()
