using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    // The classes below mirror the mission JSON files one to one. Places are given as named
    // anchors of the island plus an offset, never as raw coordinates.

    [Serializable]
    public sealed class PlaceData
    {
        public string anchor;
        public float dx, dy, dz;
    }

    [Serializable]
    public sealed class ObjectiveData
    {
        public string type;
        public string text;
        public PlaceData at;
        /// <summary>Id of a prop or an actor the objective is about, instead of <see cref="at"/>.</summary>
        public string target;
        public float radius;
        public float height;
        public float hold;
        public float range;
        public float min, max;
        public float minAltitude;
        public int shots;
        /// <summary>Mark: ids of the props that are the threats.</summary>
        public string[] targets;
        /// <summary>Mark and Arrive: id of the escorted actor.</summary>
        public string actor;
        public bool gate;
    }

    [Serializable]
    public sealed class PropData
    {
        public string id;
        public string kind;
        public PlaceData at;
    }

    [Serializable]
    public sealed class ActorData
    {
        public string id;
        public string kind;
        public PlaceData[] path;
        public float speed;
        public int startObjective;
        public float detectRange;
    }

    [Serializable]
    public sealed class TargetData
    {
        public string id;
        public string kind;
        public PlaceData at;
        /// <summary>Two or more points make the target a mover; then <see cref="at"/> is not needed.</summary>
        public PlaceData[] path;
        public float speed;
        public int startObjective;
        public float delay;
        public bool loop;
        public float altitude;
        public bool friendly;
        /// <summary>Id of the friendly target this one attacks when it gets near.</summary>
        public string attacks;
        /// <summary>Id of the target this one is mounted on; then <see cref="at"/> is not needed and ox, oy, oz place it on its parent.</summary>
        public string parent;
        public float ox, oy, oz;
    }

    [Serializable]
    public sealed class ZoneData
    {
        public PlaceData at;
        public float radius;
        public float floor;
    }

    [Serializable]
    public sealed class MissionData
    {
        public string id;
        public string region;
        public int order;
        public string template;
        public string weather;
        public float timeGoal;
        public int baseReward;
        public int starReward;
        public float batteryStart = 1f;
        public float payload;
        public PropData[] props;
        public ActorData[] actors;
        public ZoneData[] zones;
        public TargetData[] targets;
        /// <summary>Above zero: the mission is flown with this many strike drones.</summary>
        public int strikeDrones;
        public ObjectiveData[] objectives;
    }

    /// <summary>
    /// Turns mission JSON into a playable <see cref="MissionDefinition"/> and reports everything
    /// wrong with it. A mission with errors is never offered to the player.
    /// </summary>
    public static class MissionBuilder
    {
        public static readonly string[] Templates =
        {
            "recon", "race", "delivery", "stealth", "special", "assault", "convoy", "hunt", "sead", "demolition", "defend", "kamikaze", "eliminate", "airfield",
            "naval",
        };

        public static readonly string[] PropKinds =
        {
            "antenna", "crates", "camp", "vehicle", "boat", "transmitter", "threat", "pad", "radar", "sandbags", "barrels",
            "tent", "wreck",
        };

        public static readonly string[] ActorKinds = { "truck", "car", "boat" };

        public static MissionDefinition Parse(string json, Island island, List<string> errors)
        {
            MissionData data;
            try
            {
                data = JsonUtility.FromJson<MissionData>(json);
            }
            catch (ArgumentException e)
            {
                errors.Add("not valid JSON: " + e.Message);
                return null;
            }
            if (data == null)
            {
                errors.Add("empty mission file");
                return null;
            }
            return Build(data, island, errors);
        }

        public static MissionDefinition Build(MissionData data, Island island, List<string> errors)
        {
            int before = errors.Count;
            string name = string.IsNullOrEmpty(data.id) ? "(no id)" : data.id;
            void Error(string message) => errors.Add(name + ": " + message);

            if (string.IsNullOrEmpty(data.id)) Error("id is missing");
            if (Array.IndexOf(Templates, data.template) < 0) Error("unknown template '" + data.template + "'");
            if (data.timeGoal <= 0f) Error("timeGoal must be positive");
            if (data.baseReward <= 0 || data.starReward < 0) Error("rewards must be positive");
            if (data.batteryStart <= 0f || data.batteryStart > 1f) Error("batteryStart must be in (0, 1]");
            if (data.payload < 0f || data.payload > 1f) Error("payload must be in [0, 1]");
            if (!string.IsNullOrEmpty(data.weather) && WeatherCatalog.Get(data.weather).Id != data.weather)
                Error("unknown weather '" + data.weather + "'");

            var mission = new MissionDefinition
            {
                Id = data.id,
                Order = data.order,
                Template = data.template,
                Region = data.region,
                TitleKey = "mission." + data.id + ".title",
                // A mission without a briefing of its own uses the one written for its kind.
                BriefKey = Loc.Has("mission." + data.id + ".brief") ? "mission." + data.id + ".brief" : "brief." + data.template,
                WeatherId = string.IsNullOrEmpty(data.weather) ? WeatherCatalog.Clear : data.weather,
                TimeGoalSeconds = data.timeGoal,
                BaseReward = data.baseReward,
                StarReward = data.starReward,
                BatteryStart = data.batteryStart,
                Payload = data.payload,
                BasePosition = island.Spawn,
                StrikeDrones = Mathf.Max(0, data.strikeDrones),
            };

            bool Place(PlaceData place, string what, out Vector3 position)
            {
                position = Vector3.zero;
                if (place == null || string.IsNullOrEmpty(place.anchor))
                {
                    Error(what + " has no place");
                    return false;
                }
                if (!island.Anchors.TryGetValue(place.anchor, out Vector3 anchor))
                {
                    Error(what + " refers to unknown anchor '" + place.anchor + "'");
                    return false;
                }
                position = anchor + new Vector3(place.dx, place.dy, place.dz);
                return true;
            }

            var propPositions = new Dictionary<string, Vector3>();
            foreach (PropData prop in data.props ?? new PropData[0])
            {
                if (string.IsNullOrEmpty(prop.id) || propPositions.ContainsKey(prop.id))
                {
                    Error("prop id missing or repeated: '" + prop.id + "'");
                    continue;
                }
                if (Array.IndexOf(PropKinds, prop.kind) < 0) Error("prop '" + prop.id + "' has unknown kind '" + prop.kind + "'");
                if (!Place(prop.at, "prop '" + prop.id + "'", out Vector3 position)) continue;
                propPositions[prop.id] = position;
                mission.Props.Add(new PropDefinition { Id = prop.id, Kind = prop.kind, Position = position });
            }

            var actorIds = new HashSet<string>();
            foreach (ActorData actor in data.actors ?? new ActorData[0])
            {
                if (string.IsNullOrEmpty(actor.id) || !actorIds.Add(actor.id) || propPositions.ContainsKey(actor.id))
                {
                    Error("actor id missing or repeated: '" + actor.id + "'");
                    continue;
                }
                if (Array.IndexOf(ActorKinds, actor.kind) < 0) Error("actor '" + actor.id + "' has unknown kind '" + actor.kind + "'");
                if (actor.speed <= 0f) Error("actor '" + actor.id + "' needs a positive speed");
                if (actor.path == null || actor.path.Length < 2) Error("actor '" + actor.id + "' needs a path of two or more points");

                var definition = new ActorDefinition
                {
                    Id = actor.id, Kind = actor.kind, Speed = actor.speed,
                    StartObjective = actor.startObjective, DetectRange = actor.detectRange,
                };
                foreach (PlaceData point in actor.path ?? new PlaceData[0])
                    if (Place(point, "path of actor '" + actor.id + "'", out Vector3 position)) definition.Path.Add(position);
                mission.Actors.Add(definition);
            }

            foreach (ZoneData zone in data.zones ?? new ZoneData[0])
            {
                if (zone.radius <= 0f || zone.floor <= 0f) Error("a zone needs a positive radius and floor");
                if (Place(zone.at, "a zone", out Vector3 position))
                    mission.Zones.Add(new ZoneDefinition { Position = position, Radius = zone.radius, Floor = zone.floor });
            }

            var targetIndex = new Dictionary<string, int>();
            TargetData[] targets = data.targets ?? new TargetData[0];
            foreach (TargetData target in targets)
            {
                if (string.IsNullOrEmpty(target.id) || targetIndex.ContainsKey(target.id) || propPositions.ContainsKey(target.id) ||
                    actorIds.Contains(target.id))
                {
                    Error("target id missing or repeated: '" + target.id + "'");
                    continue;
                }
                TargetKind kind = TargetKinds.Get(target.kind);
                if (kind == null)
                {
                    Error("target '" + target.id + "' has unknown kind '" + target.kind + "'");
                    continue;
                }
                var definition = new TargetDefinition
                {
                    Id = target.id, Kind = kind, Speed = target.speed, StartObjective = target.startObjective,
                    Delay = Mathf.Max(0f, target.delay), Loop = target.loop, Altitude = Mathf.Max(0f, target.altitude), Friendly = target.friendly,
                };
                bool placed;
                if (!string.IsNullOrEmpty(target.parent))
                {
                    // Mounted: it goes wherever its parent goes. The parent must come first in the file.
                    if (targetIndex.TryGetValue(target.parent, out int parent))
                    {
                        definition.Parent = parent;
                        definition.Offset = new Vector3(target.ox, target.oy, target.oz);
                        definition.Position = mission.Targets[parent].Position;
                        placed = true;
                    }
                    else
                    {
                        Error("target '" + target.id + "' is mounted on unknown target '" + target.parent + "'");
                        placed = false;
                    }
                }
                else if (target.path != null && target.path.Length > 0)
                {
                    if (target.path.Length < 2) Error("target '" + target.id + "' needs a path of two or more points");
                    if (target.speed <= 0f) Error("target '" + target.id + "' moves and needs a positive speed");
                    foreach (PlaceData point in target.path)
                        if (Place(point, "path of target '" + target.id + "'", out Vector3 position)) definition.Path.Add(position);
                    placed = definition.Path.Count == target.path.Length;
                    if (placed) definition.Position = definition.Path[0];
                }
                else
                {
                    placed = Place(target.at, "target '" + target.id + "'", out Vector3 position);
                    definition.Position = position;
                }
                if (!placed) continue;
                targetIndex[target.id] = mission.Targets.Count;
                mission.Targets.Add(definition);
            }
            foreach (TargetData target in targets)
            {
                if (string.IsNullOrEmpty(target.attacks) || !targetIndex.TryGetValue(target.id ?? "", out int attacker)) continue;
                if (targetIndex.TryGetValue(target.attacks, out int victim) && mission.Targets[victim].Friendly)
                    mission.Targets[attacker].Attacks = victim;
                else
                    Error("target '" + target.id + "' attacks '" + target.attacks + "', which is not a friendly target");
            }
            if (mission.StrikeDrones > 0 && mission.Targets.Count == 0) Error("strike drones need targets");

            ObjectiveData[] objectives = data.objectives ?? new ObjectiveData[0];
            if (objectives.Length == 0) Error("has no objectives");
            for (int i = 0; i < objectives.Length; i++)
            {
                ObjectiveData source = objectives[i];
                string what = "objective " + i;
                if (!Enum.TryParse(source.type, true, out ObjectiveType type) || !Enum.IsDefined(typeof(ObjectiveType), type))
                {
                    Error(what + " has unknown type '" + source.type + "'");
                    continue;
                }

                var objective = new ObjectiveDefinition
                {
                    Type = type,
                    TextKey = string.IsNullOrEmpty(source.text) ? "objective." + type.ToString().ToLowerInvariant() : source.text,
                    Radius = source.radius,
                    HeightWindow = source.height,
                    HoldSeconds = source.hold,
                    Range = source.range,
                    MinDistance = source.min,
                    MaxDistance = source.max,
                    MinAltitude = source.minAltitude,
                    Shots = Mathf.Max(1, source.shots),
                    Gate = source.gate,
                };

                // Where it is: an actor, a prop, or a place.
                bool needsPlace = type != ObjectiveType.TakeOff && type != ObjectiveType.Mark && type != ObjectiveType.Arrive &&
                                  type != ObjectiveType.Destroy;
                if (needsPlace)
                {
                    if (!string.IsNullOrEmpty(source.target))
                    {
                        if (actorIds.Contains(source.target)) objective.ActorId = source.target;
                        else if (propPositions.TryGetValue(source.target, out Vector3 prop)) objective.Position = prop;
                        else Error(what + " refers to unknown target '" + source.target + "'");
                    }
                    else if (Place(source.at, what, out Vector3 position))
                    {
                        objective.Position = position;
                    }
                }

                switch (type)
                {
                    case ObjectiveType.Reach:
                    case ObjectiveType.Land:
                        if (source.radius <= 0f) Error(what + " needs a radius");
                        break;
                    case ObjectiveType.Scan:
                        if (source.radius <= 0f || source.height <= 0f || source.hold <= 0f) Error(what + " needs radius, height and hold");
                        break;
                    case ObjectiveType.Observe:
                        if (source.range <= 0f || source.hold <= 0f) Error(what + " needs range and hold");
                        break;
                    case ObjectiveType.Photo:
                        if (source.range <= 0f) Error(what + " needs a range");
                        break;
                    case ObjectiveType.Follow:
                        if (objective.ActorId == null) Error(what + " must target an actor");
                        if (source.min <= 0f || source.max <= source.min || source.hold <= 0f) Error(what + " needs min < max and hold");
                        break;
                    case ObjectiveType.Find:
                        if (source.radius <= 0f || source.range <= source.radius) Error(what + " needs a radius and a larger range");
                        break;
                    case ObjectiveType.Strike:
                        if (source.radius <= 0f || source.minAltitude <= 0f) Error(what + " needs a radius and minAltitude");
                        break;
                    case ObjectiveType.Mark:
                        if (source.range <= 0f || source.hold <= 0f) Error(what + " needs range and hold");
                        if (!actorIds.Contains(source.actor ?? "")) Error(what + " needs the escorted actor");
                        objective.ActorId = source.actor;
                        foreach (string id in source.targets ?? new string[0])
                        {
                            if (propPositions.TryGetValue(id, out Vector3 threat)) objective.Targets.Add(threat);
                            else Error(what + " refers to unknown threat '" + id + "'");
                        }
                        if (objective.Targets.Count == 0) Error(what + " has no threats");
                        break;
                    case ObjectiveType.Arrive:
                        if (!actorIds.Contains(source.actor ?? "")) Error(what + " needs an actor");
                        objective.ActorId = source.actor;
                        break;
                    case ObjectiveType.Destroy:
                        foreach (string id in source.targets ?? new string[0])
                        {
                            if (!targetIndex.TryGetValue(id, out int index)) Error(what + " refers to unknown target '" + id + "'");
                            else if (mission.Targets[index].Friendly) Error(what + " would destroy the friendly '" + id + "'");
                            else objective.TargetIndices.Add(index);
                        }
                        if (objective.TargetIndices.Count == 0) Error(what + " has no targets");
                        break;
                }
                mission.Objectives.Add(objective);
            }

            foreach (TargetDefinition target in mission.Targets)
                if (target.StartObjective < 0 || target.StartObjective >= Mathf.Max(1, mission.Objectives.Count))
                    Error("target '" + target.Id + "' starts at an objective that does not exist");
            foreach (ActorDefinition actor in mission.Actors)
                if (actor.StartObjective < 0 || actor.StartObjective >= Mathf.Max(1, mission.Objectives.Count))
                    Error("actor '" + actor.Id + "' starts at an objective that does not exist");

            return errors.Count == before ? mission : null;
        }
    }
}
