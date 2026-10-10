#!/usr/bin/env python3
"""Writes the campaign's mission files.

The three regions share fifteen mission patterns, twelve of them combat. What changes from
region to region is how much the enemy brings (the tier), the weather, the pay and the clock.
The inland anchors sit at the same angles on every island, so the same places serve all three.
Edit the tables below and run from the repository root:  python3 tools/gen_missions.py
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


def target(tid, kind, anchor, dx=0, dz=0, **extra):
    entry = {"id": tid, "kind": kind, "at": at(anchor, dx=dx, dz=dz)}
    entry.update(extra)
    return entry


def mover(tid, kind, path, speed, delay=0, **extra):
    """A target that drives a route; `delay` seconds after its cue it sets off, which spaces a column out."""
    entry = {"id": tid, "kind": kind, "path": [at(*p) if isinstance(p, tuple) else at(p) for p in path], "speed": speed,
             "startObjective": 1}
    if delay:
        entry["delay"] = delay
    entry.update(extra)
    return entry


def destroy(targets, text=None):
    objective = {"type": "destroy", "targets": [t["id"] if isinstance(t, dict) else t for t in targets]}
    if text:
        objective["text"] = text
    return objective


# Per region: a prefix for mission ids, how hard the enemy is (0..2), pay and clock multipliers.
REGIONS = {
    "arvelia": dict(
        prefix="", tier=0, pay=1.0,
        weather=dict(first_strike="clear", convoy="overcast", recon="sunset", hunt="clear", race="overcast",
                     kamikaze="dust", delivery="overcast", sead="sunset", stealth="sunset", demolition="rain",
                     defend="overcast", airfield="night", eliminate="clear", naval="rain", special="sunset"),
    ),
    "kharad": dict(
        prefix="kh_", tier=1, pay=1.5,
        weather=dict(first_strike="clear", convoy="sunset", recon="clear", hunt="dust", race="sunset",
                     kamikaze="clear", delivery="clear", sead="dust", stealth="night", demolition="clear",
                     defend="dust", airfield="night", eliminate="sunset", naval="clear", special="night"),
    ),
    "nordheim": dict(
        # Steep ground: the radars sit higher so there is room to fly under them.
        prefix="nh_", tier=2, pay=2.2, clock=1.25, floor=38,
        weather=dict(first_strike="snow", convoy="overcast", recon="overcast", hunt="snow", race="overcast",
                     kamikaze="overcast", delivery="snow", sead="fog", stealth="night", demolition="snow",
                     defend="storm", airfield="night", eliminate="overcast", naval="storm", special="night"),
    ),
}

# (pattern, id, template, time goal, base reward, star reward)
PATTERNS = [
    ("first_strike", "first_strike", "assault", 100, 150, 50),
    ("convoy", "convoy_ambush", "convoy", 200, 190, 60),
    ("recon", "coast_watch", "recon", 150, 210, 60),
    ("hunt", "tank_hunt", "hunt", 220, 240, 70),
    ("race", "sprint", "race", 290, 240, 70),
    ("kamikaze", "fpv_strike", "kamikaze", 160, 270, 80),
    ("delivery", "supply_run", "delivery", 240, 280, 80),
    ("sead", "blind_them", "sead", 160, 320, 90),
    ("stealth", "silent_path", "stealth", 340, 340, 90),
    ("demolition", "hammer_fall", "demolition", 340, 380, 100),
    ("defend", "hold_the_line", "defend", 230, 420, 110),
    ("airfield", "night_raid", "airfield", 240, 450, 110),
    ("eliminate", "headhunter", "eliminate", 140, 480, 120),
    ("naval", "iron_tide", "naval", 180, 520, 120),
    ("special", "broken_arrow", "special", 420, 680, 150),
]

SOFT = ["truck", "truck_army", "truck_army"]
SCOUT = ["jeep", "humvee", "humvee"]
ARMOUR = ["tank_light", "tank", "tank_heavy"]


def build(pattern, r):
    """Returns the parts of a mission that depend on the pattern."""
    tier = r["tier"]
    floor = r.get("floor", 26)

    if pattern == "first_strike":
        # The opening minute: an unguarded supply point a short hop from the pad.
        targets = [target("truck1", "truck", "poi0", dx=-9), target("truck2", "truck_cargo", "poi0", dx=9, dz=4),
                   target("fuel", "truck_fuel", "poi0", dz=-12)]
        if tier >= 1:
            targets.append(target("guard", "jeep", "poi0", dx=-16, dz=12))
        if tier >= 2:
            targets.append(target("tents", "tent", "poi0", dx=16, dz=-14))
        return dict(targets=targets, props=[{"id": "bags", "kind": "sandbags", "at": at("poi0", dx=3, dz=14)},
                                            {"id": "drums", "kind": "barrels", "at": at("poi0", dx=-4, dz=-20)}],
                    objectives=[TAKEOFF, destroy(targets, "objective.destroy_supply"), HOME])

    if pattern == "convoy":
        road = ["coast6", "coast7", "coast0", "coast1"]
        column = [mover("lead", SCOUT[tier], road, 4.5), mover("truck1", SOFT[tier], road, 4.5, delay=5),
                  mover("truck2", "truck_cargo", road, 4.5, delay=10), mover("fuel", "truck_fuel", road, 4.5, delay=15)]
        if tier >= 1:
            column.append(mover("apc", "apc", road, 4.5, delay=20))
        if tier >= 2:
            column.append(mover("tank", "tank_light", road, 4.5, delay=25))
        return dict(targets=column, objectives=[TAKEOFF, destroy(column, "objective.destroy_convoy"), HOME])

    if pattern == "recon":
        boats = [target("boat1", "boat", "sea3"), target("boat2", "boat", "sea3", dx=34, dz=22)]
        if tier >= 2:
            boats.append(target("boat3", "boat", "sea3", dx=-30, dz=30))
        guards = [target("squad", "squad", "poi10", dx=12, dz=8)] if tier >= 1 else []
        return dict(props=[{"id": "camp", "kind": "camp", "at": at("poi10")}], targets=boats + guards,
                    objectives=[TAKEOFF, {"type": "observe", "target": "camp", "range": 100, "hold": 4},
                                destroy(boats, "objective.destroy_boats"), HOME])

    if pattern == "hunt":
        tanks = [target("tank1", "tank_light", "poi1", dx=-12, dz=6), target("tank2", ARMOUR[min(2, tier + 1)], "poi1", dx=10, dz=-8)]
        if tier >= 1:
            tanks.append(target("tank3", "tank", "poi1", dx=2, dz=18))
        if tier >= 2:
            tanks.append(target("tank4", "tank_heavy", "poi1", dx=-18, dz=-14))
        return dict(targets=tanks, props=[{"id": "bags", "kind": "sandbags", "at": at("poi1", dx=18, dz=10)}],
                    objectives=[TAKEOFF, destroy(tanks, "objective.destroy_armour"), HOME])

    if pattern == "race":
        gates = [{"type": "reach", "at": at("ring%d" % i), "radius": 7, "gate": True} for i in range(10)]
        return dict(objectives=[TAKEOFF] + gates + [HOME])

    if pattern == "kamikaze":
        # Each strike drone is one hit; there is one more of them than there are things to hit.
        marks = [target("heavy", "tank_heavy", "poi9", dx=-8), target("bunker", "bunker", "poi9", dx=12, dz=8)]
        # Nothing here outranges a rifle: a strike drone has no armour to get through air defence with.
        if tier >= 1:
            marks.append(target("howitzer", "artillery", "poi9", dx=-4, dz=-18))
        if tier >= 2:
            marks.append(target("tank", "tank", "poi9", dx=18, dz=-10))
        guards = [target("squad", "squad", "poi9", dx=2, dz=20)]
        return dict(strikeDrones=len(marks) + 1, targets=marks + guards,
                    objectives=[TAKEOFF, destroy(marks, "objective.destroy_kamikaze")])

    if pattern == "delivery":
        return dict(payload=0.7, props=[{"id": "dropzone", "kind": "pad", "at": at("poi4")},
                                        {"id": "tent", "kind": "tent", "at": at("poi4", dx=14, dz=6)}],
                    objectives=[TAKEOFF, {"type": "land", "text": "objective.deliver", "target": "dropzone", "radius": 6},
                                {"type": "takeoff", "text": "objective.takeoff_again"}, HOME])

    if pattern == "sead":
        guns = [target("radar", "radar", "poi3"), target("gun1", "aagun", "poi3", dx=-16, dz=8),
                target("gun2", "gatling", "poi3", dx=14, dz=-12)]
        if tier == 1:
            guns.append(target("sam", "sam", "poi3", dx=6, dz=20))
        if tier >= 2:
            # In the mountains there is no flying under a missile battery: a third gun instead.
            guns.append(target("gun3", "aagun", "poi3", dx=-10, dz=-22))
        return dict(targets=guns, props=[{"id": "bags", "kind": "sandbags", "at": at("poi3", dx=-16, dz=14)}],
                    objectives=[TAKEOFF, destroy(guns, "objective.destroy_airdefence"), HOME])

    if pattern == "stealth":
        return dict(props=[{"id": "relay", "kind": "antenna", "at": at("poi5")}],
                    zones=[{"at": at(a), "radius": 170, "floor": floor} for a in ("poi3", "poi6")],
                    objectives=[TAKEOFF, scan("poi5", hold=4, text="objective.scan_relay"), HOME])

    if pattern == "demolition":
        marks = [target("hq", "hq", "poi7"), target("depot", "depot", "poi7", dx=24, dz=-6)]
        guards = [target("squad", "squad", "poi7", dx=-14, dz=12), target("gun", "gatling", "poi7", dx=-6, dz=-20)]
        if tier >= 1:
            guards.append(target("bunker", "bunker", "poi7", dx=10, dz=20))
        if tier >= 2:
            guards.append(target("tank", "tank", "poi7", dx=-22, dz=-4))
        return dict(targets=marks + guards, objectives=[TAKEOFF, destroy(marks, "objective.destroy_hq"), HOME])

    if pattern == "defend":
        base = target("outpost", "outpost", "pad", dx=62, dz=34, friendly=True)
        road = ["coast6", "poi10", ("pad", 100, 0, 70)]
        wave = [mover("raider1", "jeep", road, 7, attacks="outpost"), mover("raider2", SCOUT[tier], road, 7, delay=9, attacks="outpost"),
                mover("raider3", "apc", road, 6, delay=20, attacks="outpost")]
        if tier >= 1:
            wave.append(mover("raider4", "tank_light", road, 5.5, delay=30, attacks="outpost"))
        if tier >= 2:
            wave.append(mover("raider5", "tank", road, 5.5, delay=42, attacks="outpost"))
        return dict(targets=[base] + wave, objectives=[TAKEOFF, destroy(wave, "objective.defend"), HOME])

    if pattern == "airfield":
        parked = [target("heli1", "heli", "poi2", dx=-14), target("heli2", "heli_transport", "poi2", dx=12, dz=10),
                  target("fuel", "truck_fuel", "poi2", dx=2, dz=-18)]
        if tier >= 1:
            parked.append(target("heli3", "heli", "poi2", dx=-6, dz=22))
        guards = [target("gun", "gatling", "poi2", dx=24, dz=-8)]
        if tier >= 1:
            # One of them is already up, circling the strip.
            circuit = [("poi2", -70, 0, -70), ("poi2", 70, 0, -70), ("poi2", 70, 0, 70), ("poi2", -70, 0, 70), ("poi2", -70, 0, -70)]
            guards.append(mover("patrol", "gunship", circuit, 9, loop=True, altitude=38, startObjective=0))
        if tier >= 2:
            guards.append(target("gun2", "aagun", "poi2", dx=-24, dz=-12))
        return dict(targets=parked + guards, objectives=[TAKEOFF, destroy(parked, "objective.destroy_airfield"), HOME])

    if pattern == "eliminate":
        road = ["coast1", "poi2", "poi1", "coast0"]
        escort = [mover("escort1", SCOUT[tier], road, 7), mover("escort2", "apc" if tier else "jeep", road, 7, delay=10)]
        if tier >= 2:
            escort.append(mover("escort3", "tank_light", road, 7, delay=15))
        commander = mover("commander", "staff_car", road, 7, delay=5)
        return dict(targets=[commander] + escort, objectives=[TAKEOFF, destroy([commander], "objective.eliminate"), HOME])

    if pattern == "naval":
        ship = target("ship", "warship", "sea0")
        boats = [target("boat1", "boat", "sea0", dx=-46, dz=30), target("boat2", "boat", "sea0", dx=44, dz=-34)]
        if tier >= 2:
            boats.append(target("boat3", "boat", "sea0", dx=10, dz=60))
        return dict(targets=[ship] + boats, objectives=[TAKEOFF, destroy([ship] + boats, "objective.destroy_fleet"), HOME])

    if pattern == "special":
        # The last position stands just clear of the radars: it is attacked from above their floor.
        # No missile battery here: the way in passes within its reach, and there is no flying low all of it.
        marks = [target("gun", "aagun", "poi2", dx=14, dz=12), target("hq", "hq", "poi2", dx=-6), target("tank", ARMOUR[tier], "poi2", dx=4, dz=-20)]
        guards = [target("gun2", "gatling", "poi2", dx=22, dz=-10)] if tier >= 1 else []
        # The papers lie at the foot of the first radar, well under its floor. The circles are as wide
        # as each island allows while leaving the position at poi2 outside them.
        reach = (170, 150, 140)[tier]
        return dict(props=[{"id": "papers", "kind": "crates", "at": at("poi3", dx=12, dz=-8)}],
                    zones=[{"at": at(a), "radius": reach, "floor": floor} for a in ("poi3", "poi6")],
                    targets=marks + guards,
                    objectives=[TAKEOFF, {"type": "photo", "target": "papers", "range": 45},
                                destroy(marks, "objective.destroy_final"), HOME])
    raise ValueError(pattern)


def main():
    count = 0
    for region, r in REGIONS.items():
        folder = os.path.join(OUT, region)
        os.makedirs(folder, exist_ok=True)
        written = set()
        for order, (pattern, name, template, goal, base, star) in enumerate(PATTERNS, start=1):
            mission = {
                "id": r["prefix"] + name,
                "region": region,
                "order": order,
                "template": template,
                "weather": r["weather"][pattern],
                # Mountains make every route longer, and a stronger enemy takes longer to beat.
                "timeGoal": int(round(goal * r.get("clock", 1.0) * (1.0 + 0.12 * r["tier"]) / 10.0)) * 10,
                "baseReward": int(round(base * r["pay"] / 10.0)) * 10,
                "starReward": int(round(star * r["pay"] / 10.0)) * 10,
            }
            mission.update(build(pattern, r))
            with open(os.path.join(folder, mission["id"] + ".json"), "w") as handle:
                handle.write(json.dumps(mission, indent=2) + "\n")
            count += 1
            written.add(mission["id"] + ".json")
        # A mission that is no longer in the table goes, with its .meta; the others keep theirs.
        for name in os.listdir(folder):
            if name.endswith(".json") and name not in written:
                os.remove(os.path.join(folder, name))
                if os.path.exists(os.path.join(folder, name + ".meta")):
                    os.remove(os.path.join(folder, name + ".meta"))
    print("wrote", count, "missions")


if __name__ == "__main__":
    main()
