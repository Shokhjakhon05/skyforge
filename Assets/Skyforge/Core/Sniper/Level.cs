using System.Collections.Generic;
using UnityEngine;

namespace Sniper
{
    public enum Role
    {
        /// <summary>Has to die for the mission to succeed.</summary>
        Target,
        /// <summary>Armed and in the way; shoots back once he knows where the shot came from.</summary>
        Guard,
        /// <summary>Must not be hit.</summary>
        Civilian,
        /// <summary>On the player's side; the mission is lost with him.</summary>
        Ally,
    }

    /// <summary>A solid thing a bullet stops in: a box standing on the ground, turned about the vertical.</summary>
    public struct Cover
    {
        public Vector3 Centre, Size;
        public float YawDeg;
        /// <summary>Thin enough for a piercing round to go through.</summary>
        public bool Thin;
    }

    /// <summary>A piece of scenery: which model, where, turned how, how large.</summary>
    public struct Piece
    {
        public string Model;
        public Vector3 At;
        public float YawDeg;
        /// <summary>Height in metres the model is scaled to; zero keeps the size it was made at.</summary>
        public float Height;
        /// <summary>The box a bullet meets, if it is set by hand; otherwise the picture's own bounds are used.</summary>
        public Vector3 Solid;
        /// <summary>Bullets pass: a lamp post, a tree's crown, something far in the background.</summary>
        public bool Open;
        public bool Thin;
    }

    public sealed class LocationDef
    {
        public string Id, NameKey;
        /// <summary>"asphalt", "concrete", "sand", "grass": what the ground is laid with.</summary>
        public string Ground = "asphalt";
        /// <summary>"day", "dusk", "night", "overcast".</summary>
        public string Light = "day";
        public List<Piece> Pieces = new List<Piece>();
        public float HalfSize = 220f;
        /// <summary>Beyond this the ground ends in a quay wall and the sea begins; zero for a place with no shore.</summary>
        public float WaterZ;
        /// <summary>Where the camera stands behind the menu, and what it looks at.</summary>
        public Vector3 MenuEye = new Vector3(30f, 8f, -34f), MenuLook = new Vector3(0f, 2f, 8f);
    }

    public sealed class ActorDef
    {
        public string Id, Model, NameKey;
        public Role Role;
        /// <summary>Where he walks, in order; one point for a man who stands.</summary>
        public Vector3[] Path;
        /// <summary>Seconds spent at each point of the path before moving on.</summary>
        public float Wait = 3f;
        public float YawDeg;
        /// <summary>Clip played while standing: "Idle", "Idle_Gun", "Interact", "Wave".</summary>
        public string Idle = "Idle";
        public float Health = 100f;
        public int Bounty;
        /// <summary>Where he runs when the shooting starts; reaching it takes a target out of the mission alive.</summary>
        public Vector3? Escape;
        /// <summary>Seconds after the mission starts at which he sets off for his escape point on his own; zero for never.</summary>
        public float LeavesAt;
        /// <summary>For a man coming for an ally: he kills on arrival at the end of his path.</summary>
        public bool Assassin;
    }

    public sealed class MissionDef
    {
        public string Id, LocationId, TitleKey, BriefKey;
        public int Order;
        /// <summary>Where the rifle is, and where it looks when the mission opens.</summary>
        public Vector3 Perch, LookAt;
        public List<ActorDef> Actors = new List<ActorDef>();
        public int Rounds = 12;
        public float Seconds;
        public Vector3 Wind;
        public int Reward = 500;
        /// <summary>The second star: "headshots" (every kill a head), "silent" (no alarm), "clean" (no missed round).</summary>
        public string Second = "headshots";
        /// <summary>The third star: finished within this many seconds.</summary>
        public float Par = 60f;
    }
}
