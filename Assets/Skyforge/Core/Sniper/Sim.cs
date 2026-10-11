using System.Collections.Generic;
using UnityEngine;

namespace Sniper
{
    public enum Mood { Calm, Alarmed, Dead, Gone }
    public enum Zone { None, Head, Body, Legs }
    public enum Outcome { Running, Won, Lost }
    public enum LostWhy { None, Escaped, Civilian, PlayerDown, OutOfRounds, TimeUp, AllyDown }

    public enum SimEventType
    {
        Shot, Hit, Kill, Miss, Alarm, Escaped, PlayerHit, EnemyShot, AllyDown, Reloaded, Explosion,
    }

    public struct SimEvent
    {
        public SimEventType Type;
        public int Actor;
        public Zone Zone;
        public Vector3 Position;
    }

    public struct ActorState
    {
        public Vector3 Position;
        public float YawDeg;
        public Mood Mood;
        /// <summary>The animation clip that shows what he is doing, and whether it repeats.</summary>
        public string Clip;
        public bool Loop;
        /// <summary>Counts up from the moment the clip changed, so the picture can start it at the right frame.</summary>
        public float ClipTime;
        public float Health;
        public bool Headshot;
        internal int Leg;
        internal float Wait, React, Aim, FireIn, Stun;
        internal bool Leaving, Reacting;
    }

    public struct Bullet
    {
        public Vector3 Position, Velocity;
        public float Damage, Age;
        public bool Piercing;
    }

    public sealed class MissionResult
    {
        public bool Won;
        public LostWhy Why;
        public int Stars, Cash, Kills, Headshots, Shots;
        public bool SecondMet, ThirdMet;
        public float Seconds;
    }

    /// <summary>
    /// One mission as it plays: the people of the scene going about their business, the rounds in
    /// flight, who has noticed what, and whether the job is done. No Unity scene is needed to run it.
    /// </summary>
    public sealed class SniperSim
    {
        public const float WalkSpeed = 1.35f, RunSpeed = 4.4f, Gravity = 9.81f;
        /// <summary>How hard the wind pushes a round sideways, per metre per second of wind.</summary>
        public const float WindPush = 0.55f;
        public const float HeadHeight = 1.63f, HeadRadius = 0.135f, BodyRadius = 0.23f, LegRadius = 0.17f;
        const float SubStep = 1f / 300f;

        public readonly MissionDef Mission;
        public readonly RifleDef Rifle;
        readonly List<Cover> _covers = new List<Cover>();
        readonly List<bool> _gone = new List<bool>();
        public const float BlastRadius = 6.5f;
        public int Blasts { get; private set; }
        readonly ActorState[] _actors;
        readonly List<Bullet> _bullets = new List<Bullet>();
        readonly System.Random _random;

        public readonly List<SimEvent> Events = new List<SimEvent>();
        public IReadOnlyList<Bullet> Bullets => _bullets;
        public int ActorCount => _actors.Length;
        public ActorState Actor(int index) => _actors[index];
        public ActorDef Def(int index) => Mission.Actors[index];

        /// <summary>Rounds not yet in the rifle, and rounds in it.</summary>
        public int Reserve { get; private set; }
        public int InMagazine { get; private set; }
        public float BoltLeft { get; private set; }
        public float ReloadLeft { get; private set; }
        /// <summary>What is left of the shooter, 1..0, once they start shooting back.</summary>
        public float Health { get; private set; } = 1f;
        public bool Alarm { get; private set; }
        public float Elapsed { get; private set; }
        public int Kills { get; private set; }
        public int Headshots { get; private set; }
        public int Shots { get; private set; }
        public int Misses { get; private set; }
        public Outcome Outcome { get; private set; }
        public LostWhy Why { get; private set; }

        public SniperSim(MissionDef mission, LocationDef location, RifleDef rifle, int seed = 1)
        {
            Mission = mission;
            Rifle = rifle;
            _random = new System.Random(seed);
            if (location != null)
            {
                foreach (Piece piece in location.Pieces)
                {
                    if (piece.Open || piece.Solid.x <= 0f || piece.Solid.y <= 0f) continue;
                    _covers.Add(new Cover { Centre = piece.At + new Vector3(0f, piece.Solid.y * 0.5f, 0f), Size = piece.Solid, YawDeg = piece.YawDeg, Thin = piece.Thin });
                }
            }
            _actors = new ActorState[mission.Actors.Count];
            for (int i = 0; i < _actors.Length; i++)
            {
                ActorDef def = mission.Actors[i];
                _actors[i] = new ActorState
                {
                    Position = def.Path != null && def.Path.Length > 0 ? def.Path[0] : Vector3.zero, YawDeg = def.YawDeg, Mood = Mood.Calm,
                    Clip = def.Idle, Loop = true, Health = def.Health, Wait = def.Wait * (0.4f + 0.6f * (float)_random.NextDouble()),
                };
            }
            InMagazine = Mathf.Min(rifle.Magazine, mission.Rounds);
            Reserve = mission.Rounds - InMagazine;
        }

        /// <summary>Scenery measured after it was built: a box that stops a round.</summary>
        public void AddCover(Cover cover) => _covers.Add(cover);

        public int CoverCount => _covers.Count;

        public int TargetsLeft
        {
            get
            {
                int left = 0;
                for (int i = 0; i < _actors.Length; i++)
                    if (Mission.Actors[i].Role == Role.Target && _actors[i].Mood != Mood.Dead) left++;
                return left;
            }
        }

        public bool CanFire => Outcome == Outcome.Running && InMagazine > 0 && BoltLeft <= 0f && ReloadLeft <= 0f;

        /// <summary>One round down the line of sight. False if the rifle is not ready.</summary>
        public bool Fire(Vector3 origin, Vector3 direction)
        {
            if (!CanFire) return false;
            InMagazine--;
            Shots++;
            BoltLeft = Rifle.BoltSeconds;
            if (InMagazine == 0 && Reserve > 0) ReloadLeft = Rifle.ReloadSeconds;
            _bullets.Add(new Bullet { Position = origin, Velocity = direction.normalized * Rifle.MuzzleSpeed, Damage = Rifle.Damage, Piercing = Rifle.Piercing });
            Events.Add(new SimEvent { Type = SimEventType.Shot, Actor = -1, Position = origin });
            // An unsuppressed rifle is heard by everyone on the square.
            if (!Rifle.Suppressed) Raise(origin, 100000f, 0.45f);
            return true;
        }

        public void Reload()
        {
            if (Outcome != Outcome.Running || ReloadLeft > 0f || Reserve <= 0 || InMagazine >= Rifle.Magazine) return;
            ReloadLeft = Rifle.ReloadSeconds;
        }

        /// <summary>Everyone calm within the radius reacts after the delay, each in his own time.</summary>
        void Raise(Vector3 at, float radius, float delay)
        {
            for (int i = 0; i < _actors.Length; i++)
            {
                if (_actors[i].Mood != Mood.Calm || _actors[i].Reacting) continue;
                if (Flat(_actors[i].Position, at) > radius) continue;
                _actors[i].Reacting = true;
                _actors[i].React = delay + (float)_random.NextDouble() * 0.7f;
            }
        }

        static float Flat(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

        public void Step(float dt)
        {
            if (Outcome != Outcome.Running) return;
            Elapsed += dt;
            if (BoltLeft > 0f) BoltLeft -= dt;
            if (ReloadLeft > 0f)
            {
                ReloadLeft -= dt;
                if (ReloadLeft <= 0f)
                {
                    int take = Mathf.Min(Rifle.Magazine - InMagazine, Reserve);
                    InMagazine += take;
                    Reserve -= take;
                    Events.Add(new SimEvent { Type = SimEventType.Reloaded, Actor = -1 });
                }
            }

            for (float left = dt; left > 0f && _bullets.Count > 0; left -= SubStep) FlyBullets(Mathf.Min(SubStep, left));
            for (int i = 0; i < _actors.Length; i++) StepActor(i, dt);
            Judge();
        }

        // ---------------------------------------------------------------- people

        void SetClip(ref ActorState a, string clip, bool loop)
        {
            if (a.Clip == clip) return;
            a.Clip = clip;
            a.Loop = loop;
            a.ClipTime = 0f;
        }

        void StepActor(int index, float dt)
        {
            ref ActorState a = ref _actors[index];
            ActorDef def = Mission.Actors[index];
            a.ClipTime += dt;
            if (a.Mood == Mood.Dead || a.Mood == Mood.Gone) return;

            if (a.Reacting)
            {
                a.React -= dt;
                if (a.React <= 0f) Startle(index);
            }

            // A man who has just been hit does nothing for a moment but feel it.
            if (a.Stun > 0f)
            {
                a.Stun -= dt;
                return;
            }

            if (a.Mood == Mood.Calm)
            {
                // Somebody lying where he walks is noticed.
                for (int j = 0; j < _actors.Length && !a.Reacting; j++)
                {
                    if (_actors[j].Mood != Mood.Dead || Flat(_actors[j].Position, a.Position) > 7f) continue;
                    a.Reacting = true;
                    a.React = 0.8f + (float)_random.NextDouble() * 0.8f;
                }
                if (!a.Leaving && def.LeavesAt > 0f && Elapsed >= def.LeavesAt && def.Escape.HasValue) a.Leaving = true;
                if (a.Leaving)
                {
                    if (Walk(ref a, def.Escape.Value, WalkSpeed, dt)) Leave(index);
                    else SetClip(ref a, "Walk", true);
                    return;
                }
                if (def.Path == null || def.Path.Length < 2)
                {
                    SetClip(ref a, def.Idle, true);
                    return;
                }
                if (a.Wait > 0f)
                {
                    a.Wait -= dt;
                    SetClip(ref a, def.Idle, true);
                    return;
                }
                int next = (a.Leg + 1) % def.Path.Length;
                SetClip(ref a, "Walk", true);
                if (!Walk(ref a, def.Path[next], WalkSpeed, dt)) return;
                a.Leg = next;
                a.Wait = def.Wait;
                // A man sent to kill does it where his path ends.
                if (def.Assassin && next == def.Path.Length - 1) Strike(index);
                return;
            }

            // Alarmed.
            if (def.Role == Role.Civilian || (def.Escape.HasValue && def.Role != Role.Guard))
            {
                Vector3 to = def.Escape ?? Away(a.Position);
                SetClip(ref a, "Run", true);
                if (Walk(ref a, to, RunSpeed, dt)) Leave(index);
                return;
            }
            if (def.Assassin)
            {
                SetClip(ref a, "Run", true);
                if (Walk(ref a, def.Path[def.Path.Length - 1], RunSpeed, dt)) Strike(index);
                return;
            }
            if (def.Role == Role.Ally)
            {
                SetClip(ref a, "Idle", true);
                return;
            }

            // An armed man finds the rifle and answers it.
            Vector3 toPerch = Mission.Perch - a.Position;
            a.YawDeg = Mathf.Atan2(toPerch.x, toPerch.z) * Mathf.Rad2Deg;
            if (a.Aim > 0f)
            {
                a.Aim -= dt;
                SetClip(ref a, "Idle_Gun_Pointing", true);
                return;
            }
            SetClip(ref a, "Idle_Gun_Shoot", true);
            a.FireIn -= dt;
            if (a.FireIn > 0f) return;
            a.FireIn = 1.1f + (float)_random.NextDouble() * 0.9f;
            float distance = toPerch.magnitude;
            Events.Add(new SimEvent { Type = SimEventType.EnemyShot, Actor = index, Position = a.Position + Vector3.up * 1.4f });
            if (_random.NextDouble() > Mathf.Clamp(0.7f - distance / 420f, 0.12f, 0.6f)) return;
            Health = Mathf.Max(0f, Health - 0.14f);
            Events.Add(new SimEvent { Type = SimEventType.PlayerHit, Actor = index, Position = Mission.Perch });
        }

        /// <summary>The moment a man understands there is shooting.</summary>
        void Startle(int index)
        {
            ref ActorState a = ref _actors[index];
            a.Reacting = false;
            if (a.Mood != Mood.Calm) return;
            a.Mood = Mood.Alarmed;
            float distance = (Mission.Perch - a.Position).magnitude;
            a.Aim = 2.6f + distance / 110f + (float)_random.NextDouble();
            a.FireIn = 0.4f;
            if (!Alarm)
            {
                Alarm = true;
                Events.Add(new SimEvent { Type = SimEventType.Alarm, Actor = index, Position = a.Position });
            }
            // He shouts; those near him know a moment later.
            Raise(a.Position, 28f, 1.0f);
        }

        /// <summary>Directly away from the rifle, far enough to be out of the picture.</summary>
        Vector3 Away(Vector3 from)
        {
            Vector3 out_ = from - Mission.Perch;
            out_.y = 0f;
            if (out_.sqrMagnitude < 0.01f) out_ = Vector3.forward;
            return from + out_.normalized * 70f;
        }

        /// <summary>Moves a man towards a point; true once he is there.</summary>
        static bool Walk(ref ActorState a, Vector3 to, float speed, float dt)
        {
            Vector3 gap = to - a.Position;
            gap.y = 0f;
            float distance = gap.magnitude;
            if (distance < 0.08f) return true;
            a.YawDeg = Mathf.Atan2(gap.x, gap.z) * Mathf.Rad2Deg;
            float step = Mathf.Min(distance, speed * dt);
            a.Position += gap / distance * step;
            return distance - step < 0.08f;
        }

        void Leave(int index)
        {
            _actors[index].Mood = Mood.Gone;
            if (Mission.Actors[index].Role != Role.Target) return;
            Events.Add(new SimEvent { Type = SimEventType.Escaped, Actor = index, Position = _actors[index].Position });
            Lose(LostWhy.Escaped);
        }

        void Strike(int index)
        {
            for (int j = 0; j < _actors.Length; j++)
            {
                if (Mission.Actors[j].Role != Role.Ally || _actors[j].Mood == Mood.Dead) continue;
                if (Flat(_actors[j].Position, _actors[index].Position) > 6f) continue;
                Kill(j, Zone.Body, _actors[j].Position);
                return;
            }
        }

        // ---------------------------------------------------------------- rounds

        void FlyBullets(float dt)
        {
            for (int b = _bullets.Count - 1; b >= 0; b--)
            {
                Bullet bullet = _bullets[b];
                Vector3 from = bullet.Position;
                bullet.Velocity += (Vector3.down * Gravity + Mission.Wind * WindPush) * dt;
                Vector3 to = from + bullet.Velocity * dt;
                bullet.Age += dt;

                float best = 2f;
                int hitActor = -1, hitCover = -1;
                Zone zone = Zone.None;
                if (to.y <= 0f && from.y > 0f)
                {
                    best = from.y / (from.y - to.y);
                }
                for (int c = 0; c < _covers.Count; c++)
                {
                    if (c < _gone.Count && _gone[c]) continue;
                    float t = SegmentBox(from, to, _covers[c]);
                    if (t >= best) continue;
                    best = t;
                    hitCover = c;
                   
                }
                for (int i = 0; i < _actors.Length; i++)
                {
                    if (_actors[i].Mood == Mood.Dead || _actors[i].Mood == Mood.Gone) continue;
                    Vector3 p = _actors[i].Position;
                    if (Flat(p, from) > 4f + (to - from).magnitude) continue;
                    float t = SegmentSphere(from, to, p + new Vector3(0f, HeadHeight, 0f), HeadRadius);
                    if (t < best) { best = t; hitActor = i; hitCover = -1; zone = Zone.Head; }
                    t = SegmentCapsule(from, to, p + new Vector3(0f, 1.0f, 0f), p + new Vector3(0f, 1.42f, 0f), BodyRadius);
                    if (t < best) { best = t; hitActor = i; hitCover = -1; zone = Zone.Body; }
                    t = SegmentCapsule(from, to, p + new Vector3(0f, 0.18f, 0f), p + new Vector3(0f, 0.86f, 0f), LegRadius);
                    if (t < best) { best = t; hitActor = i; hitCover = -1; zone = Zone.Legs; }
                }

                if (best > 1f)
                {
                    bullet.Position = to;
                    if (bullet.Age > 3f)
                    {
                        _bullets.RemoveAt(b);
                        Missed(to);
                    }
                    else _bullets[b] = bullet;
                    continue;
                }

                Vector3 at = from + (to - from) * best;
                if (hitActor >= 0)
                {
                    _bullets.RemoveAt(b);
                    Strike(hitActor, zone, at, bullet.Damage);
                    continue;
                }
                if (hitCover >= 0 && _covers[hitCover].Explosive)
                {
                    _bullets.RemoveAt(b);
                    Explode(hitCover);
                    continue;
                }
                if (hitCover >= 0 && _covers[hitCover].Thin && bullet.Piercing)
                {
                    // Through, slower and weaker, a hand's breadth past the surface.
                    bullet.Damage *= 0.6f;
                    bullet.Position = at + bullet.Velocity.normalized * (Mathf.Min(_covers[hitCover].Size.x, _covers[hitCover].Size.z) + 0.3f);
                    _bullets[b] = bullet;
                    continue;
                }
                _bullets.RemoveAt(b);
                Missed(at);
            }
        }

        /// <summary>A drum of fuel hit: it goes up, and so does anyone standing by it. Nothing about it is quiet.</summary>
        void Explode(int cover)
        {
            while (_gone.Count <= cover) _gone.Add(false);
            _gone[cover] = true;
            Blasts++;
            Vector3 at = _covers[cover].Centre;
            Events.Add(new SimEvent { Type = SimEventType.Explosion, Actor = _covers[cover].Tag, Position = at });
            for (int i = 0; i < _actors.Length; i++)
            {
                if (_actors[i].Mood == Mood.Dead || _actors[i].Mood == Mood.Gone) continue;
                if (Flat(_actors[i].Position, at) <= BlastRadius) Kill(i, Zone.Body, _actors[i].Position + Vector3.up);
            }
            // One drum sets off the next.
            for (int c = 0; c < _covers.Count; c++)
            {
                if (!_covers[c].Explosive || (c < _gone.Count && _gone[c])) continue;
                if (Flat(_covers[c].Centre, at) <= BlastRadius * 0.7f) Explode(c);
            }
            Raise(at, 100000f, 0.3f);
            if (!Alarm)
            {
                Alarm = true;
                Events.Add(new SimEvent { Type = SimEventType.Alarm, Actor = -1, Position = at });
            }
        }

        void Missed(Vector3 at)
        {
            Misses++;
            Events.Add(new SimEvent { Type = SimEventType.Miss, Actor = -1, Position = at });
            // A round into the wall beside a man tells him as much as the report would have.
            Raise(at, 5f, 0.35f);
        }

        void Strike(int index, Zone zone, Vector3 at, float damage)
        {
            ref ActorState a = ref _actors[index];
            float dealt = zone == Zone.Head ? 100000f : zone == Zone.Legs ? damage * 0.5f : damage;
            a.Health -= dealt;
            if (a.Health <= 0f)
            {
                Kill(index, zone, at);
                return;
            }
            Events.Add(new SimEvent { Type = SimEventType.Hit, Actor = index, Zone = zone, Position = at });
            SetClip(ref a, "HitRecieve", false);
            a.Stun = 0.55f;
            a.Reacting = true;
            a.React = 0.25f;
            Raise(a.Position, 16f, 0.8f);
        }

        void Kill(int index, Zone zone, Vector3 at)
        {
            ref ActorState a = ref _actors[index];
            a.Mood = Mood.Dead;
            a.Headshot = zone == Zone.Head;
            a.Reacting = false;
            SetClip(ref a, "Death", false);
            ActorDef def = Mission.Actors[index];
            Events.Add(new SimEvent { Type = def.Role == Role.Ally ? SimEventType.AllyDown : SimEventType.Kill, Actor = index, Zone = zone, Position = at });
            if (def.Role == Role.Civilian) Lose(LostWhy.Civilian);
            else if (def.Role == Role.Ally) Lose(LostWhy.AllyDown);
            else
            {
                Kills++;
                if (a.Headshot) Headshots++;
            }
            // Whoever stood near saw him fall.
            Raise(a.Position, 13f, 1.1f);
        }

        // ---------------------------------------------------------------- geometry

        static float SegmentSphere(Vector3 a, Vector3 b, Vector3 centre, float radius)
        {
            Vector3 d = b - a, m = a - centre;
            float dd = Vector3.Dot(d, d);
            if (dd < 1e-9f) return 2f;
            float t = Mathf.Clamp01(-Vector3.Dot(m, d) / dd);
            Vector3 closest = a + d * t - centre;
            if (Vector3.Dot(closest, closest) > radius * radius) return 2f;
            // Back along the line to where it entered.
            float back = Mathf.Sqrt(Mathf.Max(0f, radius * radius - Vector3.Dot(closest, closest))) / Mathf.Sqrt(dd);
            return Mathf.Clamp01(t - back);
        }

        /// <summary>Where along a→b the segment first comes within the radius of the segment p→q; 2 if never.</summary>
        static float SegmentCapsule(Vector3 a, Vector3 b, Vector3 p, Vector3 q, float radius)
        {
            Vector3 d1 = b - a, d2 = q - p, r = a - p;
            float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            if (aa < 1e-9f) return 2f;
            float c = Vector3.Dot(d1, r), bb = Vector3.Dot(d1, d2), denom = aa * ee - bb * bb;
            float s = denom > 1e-9f ? Mathf.Clamp01((bb * f - c * ee) / denom) : 0f;
            float t = ee > 1e-9f ? (bb * s + f) / ee : 0f;
            if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / aa); }
            else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - c) / aa); }
            Vector3 gap = a + d1 * s - (p + d2 * t);
            float distance2 = Vector3.Dot(gap, gap);
            if (distance2 > radius * radius) return 2f;
            float back = Mathf.Sqrt(Mathf.Max(0f, radius * radius - distance2)) / Mathf.Sqrt(aa);
            return Mathf.Clamp01(s - back);
        }

        /// <summary>Where along a→b the segment enters the box; 2 if it does not.</summary>
        static float SegmentBox(Vector3 a, Vector3 b, Cover cover)
        {
            float yaw = -cover.YawDeg * Mathf.Deg2Rad, cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
            Vector3 la = Local(a - cover.Centre, cos, sin), lb = Local(b - cover.Centre, cos, sin);
            Vector3 d = lb - la, half = cover.Size * 0.5f;
            float enter = 0f, exit = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float start = axis == 0 ? la.x : axis == 1 ? la.y : la.z;
                float delta = axis == 0 ? d.x : axis == 1 ? d.y : d.z;
                float h = axis == 0 ? half.x : axis == 1 ? half.y : half.z;
                if (Mathf.Abs(delta) < 1e-7f)
                {
                    if (start < -h || start > h) return 2f;
                    continue;
                }
                float t1 = (-h - start) / delta, t2 = (h - start) / delta;
                if (t1 > t2) { float swap = t1; t1 = t2; t2 = swap; }
                enter = Mathf.Max(enter, t1);
                exit = Mathf.Min(exit, t2);
                if (enter > exit) return 2f;
            }
            return enter;
        }

        static Vector3 Local(Vector3 v, float cos, float sin) => new Vector3(v.x * cos + v.z * sin, v.y, -v.x * sin + v.z * cos);

        /// <summary>Whether a straight line between two points is clear of everything solid.</summary>
        public bool Clear(Vector3 from, Vector3 to)
        {
            for (int c = 0; c < _covers.Count; c++)
            {
                if (c < _gone.Count && _gone[c]) continue;
                if (SegmentBox(from, to, _covers[c]) <= 1f) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- the verdict

        void Lose(LostWhy why)
        {
            if (Outcome != Outcome.Running) return;
            Outcome = Outcome.Lost;
            Why = why;
        }

        void Judge()
        {
            if (Outcome != Outcome.Running) return;
            if (Health <= 0f)
            {
                Lose(LostWhy.PlayerDown);
                return;
            }
            if (TargetsLeft == 0)
            {
                Outcome = Outcome.Won;
                return;
            }
            if (Mission.Seconds > 0f && Elapsed >= Mission.Seconds)
            {
                Lose(LostWhy.TimeUp);
                return;
            }
            if (InMagazine + Reserve == 0 && _bullets.Count == 0) Lose(LostWhy.OutOfRounds);
        }

        public MissionResult Grade()
        {
            var result = new MissionResult
            {
                Won = Outcome == Outcome.Won, Why = Why, Kills = Kills, Headshots = Headshots, Shots = Shots, Seconds = Elapsed,
            };
            if (!result.Won) return result;
            result.SecondMet = Mission.Second == "silent" ? !Alarm : Mission.Second == "clean" ? Misses == 0 : Headshots == Kills && Kills > 0;
            result.ThirdMet = Elapsed <= Mission.Par;
            result.Stars = 1 + (result.SecondMet ? 1 : 0) + (result.ThirdMet ? 1 : 0);
            int bounty = 0;
            for (int i = 0; i < _actors.Length; i++)
                if (_actors[i].Mood == Mood.Dead && Mission.Actors[i].Role != Role.Civilian) bounty += Mission.Actors[i].Bounty;
            result.Cash = Mission.Reward + bounty + Headshots * 60 + (result.Stars - 1) * Mission.Reward / 4;
            return result;
        }
    }
}
