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
            "scan", "recon", "photo", "tail", "race", "delivery", "search", "stealth", "strike", "escort", "storm",
        };

        public static readonly string[] PropKinds =
        {
            "antenna", "crates", "camp", "vehicle", "boat", "transmitter", "threat", "pad", "radar",
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
                TitleKey = "mission." + data.id + ".title",
                BriefKey = "mission." + data.id + ".brief",
                WeatherId = string.IsNullOrEmpty(data.weather) ? WeatherCatalog.Clear : data.weather,
                TimeGoalSeconds = data.timeGoal,
                BaseReward = data.baseReward,
                StarReward = data.starReward,
                BatteryStart = data.batteryStart,
                Payload = data.payload,
                BasePosition = island.Spawn,
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
                bool needsPlace = type != ObjectiveType.TakeOff && type != ObjectiveType.Mark && type != ObjectiveType.Arrive;
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
                }
                mission.Objectives.Add(objective);
            }

            foreach (ActorDefinition actor in mission.Actors)
                if (actor.StartObjective < 0 || actor.StartObjective >= Mathf.Max(1, mission.Objectives.Count))
                    Error("actor '" + actor.Id + "' starts at an objective that does not exist");

            return errors.Count == before ? mission : null;
        }
    }
}
