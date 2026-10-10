using System;
using System.Collections.Generic;
using Skyforge.Core;
using Skyforge.UI;
using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// The headquarters: the screens around the campaign. Operations (contracts, special modes,
    /// the war over the map), the career (rank, tasks, season), the base and the collection. Each
    /// is the same list screen filled with different rows.
    /// </summary>
    public sealed partial class Game
    {
        enum Hq
        {
            None,
            Operations,
            Career,
            Base,
            Collection,
        }

        ListScreen _list;
        TransmissionScreen _transmission;
        Hq _hq;
        int _hqTab;
        // The operation the player is putting together by hand.
        int _customKind, _customLevel = 1, _customWeather;
        readonly System.Random _caseRandom = new System.Random();
        string _hqNote;

        void BuildHeadquarters()
        {
            _list = new ListScreen(transform, "Headquarters", 24);
            _list.ClosePressed += () => { Click(); CloseHeadquarters(); };
            _list.TabPressed += tab => { Click(); _hqTab = tab; _hqNote = null; ShowHeadquarters(false); };
            _transmission = new TransmissionScreen(transform);
            _transmission.ContinuePressed += () => { Click(); _transmission.Visible = false; };
            _hideOperations = () => { _list.Visible = false; _hq = Hq.None; };

            _menu.OperationsPressed += () => OpenHeadquarters(Hq.Operations);
            _menu.CareerPressed += () => OpenHeadquarters(Hq.Career);
            _menu.BasePressed += () => OpenHeadquarters(Hq.Base);
            _menu.CollectionPressed += () => OpenHeadquarters(Hq.Collection);

            // The war moves on while the player is away: income for held sectors, and counter-attacks.
            _today = Daily.Today(_save, DateTime.Now);
            Tasks.Refresh(_save, _today);
            Seasons.Roll(_save, _today);
            WarNews news = War.Refresh(_save, _today);
            if (news != null && (news.Income > 0 || news.Lost.Count > 0))
                Toast(Loc.T("war.news", news.Income, news.Lost.Count));
        }

        void OpenHeadquarters(Hq which)
        {
            Click();
            _sfx.Play(Sound.Open, 0.6f);
            _hq = which;
            _hqTab = 0;
            _hqNote = null;
            ShowHeadquarters(false);
        }

        void CloseHeadquarters()
        {
            _list.Visible = false;
            _hq = Hq.None;
            _store.Save(_save);
            ShowMenuScreen();
        }

        string Wallet() => Loc.T("menu.credits", _save.Credits) + "    " + Loc.T("menu.parts", _save.Parts);

        static string Cost(int credits, int parts) =>
            parts > 0 ? Loc.T("hq.cost.both", credits, parts) : Loc.T("hq.cost", credits);

        /// <summary>Does something that costs or pays, says what happened in the small print, and redraws.</summary>
        void Did(bool ok, string note)
        {
            _sfx.Play(ok ? Sound.Success : Sound.Warning, ok ? 0.7f : 1f);
            _hqNote = note;
            if (ok)
            {
                Announce(Achievements.Collect(_save));
                _store.Save(_save);
            }
            ShowHeadquarters(true);
        }

        void ShowHeadquarters(bool keepPage)
        {
            _today = Daily.Today(_save, DateTime.Now);
            Tasks.Refresh(_save, _today);
            Seasons.Roll(_save, _today);
            var rows = new List<ListRow>();
            string title, footer = _hqNote;
            string[] tabs;
            switch (_hq)
            {
                case Hq.Operations:
                    title = Loc.T("hq.operations");
                    tabs = new[] { Loc.T("hq.tab.contracts"), Loc.T("hq.tab.special"), Loc.T("hq.tab.war"), Loc.T("hq.tab.custom") };
                    if (_hqTab == 0) ContractRows(rows, ref footer);
                    else if (_hqTab == 1) SpecialRows(rows, ref footer);
                    else if (_hqTab == 2) WarRows(rows, ref footer);
                    else CustomRows(rows, ref footer);
                    break;
                case Hq.Career:
                    title = Loc.T("hq.career");
                    tabs = new[] { Loc.T("hq.tab.rank"), Loc.T("hq.tab.daily"), Loc.T("hq.tab.weekly"), Loc.T("hq.tab.season") };
                    if (_hqTab == 0) RankRows(rows, ref footer);
                    else if (_hqTab == 1) TaskRows(rows, false, ref footer);
                    else if (_hqTab == 2) TaskRows(rows, true, ref footer);
                    else SeasonRows(rows, ref footer);
                    break;
                case Hq.Base:
                    title = Loc.T("hq.base");
                    tabs = new[] { Loc.T("hq.tab.airfield"), Loc.T("hq.tab.armoury") };
                    UpgradeRows(rows, _hqTab == 1, ref footer);
                    break;
                case Hq.Collection:
                    title = Loc.T("hq.collection");
                    tabs = new[] { Loc.T("hq.tab.paint"), Loc.T("hq.tab.cases"), Loc.T("hq.tab.titles") };
                    if (_hqTab == 0) PaintRows(rows, ref footer);
                    else if (_hqTab == 1) CaseRows(rows, ref footer);
                    else TitleRows(rows, ref footer);
                    break;
                default:
                    return;
            }
            _list.Show(title, Wallet(), tabs, _hqTab, rows, footer, keepPage);
        }

        // ---------------------------------------------------------------- operations

        static string OperationLine(MissionDefinition op) =>
            Loc.T("op." + op.Kind.ToString().ToLowerInvariant() + ".name") + "  ·  " + Loc.T(WeatherCatalog.Get(op.WeatherId).NameKey) +
            "  ·  " + Loc.T("hq.targets", op.Targets.Count) + "  ·  " + Loc.T("hq.pays", op.BaseReward);

        void ContractRows(List<ListRow> rows, ref string footer)
        {
            int level = Career.ContractLevel(_save);
            // Three on offer; flying one brings another in its place.
            for (int i = 0; i < 3; i++)
            {
                int seed = _save.Career.ContractsDone * 3 + i + RegionCatalog.IndexOf(_region.Id) * 1000;
                MissionDefinition op = Operations.Contract(_island, _region.Id, seed, Mathf.Max(1, level + i - 1));
                if (op == null) continue;
                rows.Add(new ListRow
                {
                    Title = "«" + op.Title.ToUpperInvariant() + "»   <color=" + UiKit.CyanHex + ">" + Loc.T("hq.level", op.Level) + "</color>",
                    Note = OperationLine(op), Button = Loc.T("hq.fly"), Press = () => { Click(); StartOperation(op); },
                });
            }
            // The tower: one floor after another, each harder than the last, the same for everyone.
            int floor = _save.Career.TowerFloor + 1;
            MissionDefinition next = Operations.Contract(_island, _region.Id, 50000 + floor, Mathf.Min(Operations.MaxLevel, floor));
            if (next != null)
            {
                rows.Add(new ListRow
                {
                    Title = Loc.T("hq.tower", floor), Note = Loc.T("hq.tower.note") + "  ·  " + OperationLine(next),
                    Button = Loc.T("hq.climb"), Press = () => { Click(); StartOperation(next, -1, floor); }, Lit = true,
                });
            }
            footer = footer ?? Loc.T("hq.contracts.note", Career.RankName(Career.Rank(_save.Career.Xp)), level, Loc.T(_region.NameKey));
        }

        void SpecialRows(List<ListRow> rows, ref string footer)
        {
            int level = Career.ContractLevel(_save);
            rows.Add(new ListRow
            {
                Title = Loc.T("op.survival.title"), Note = Loc.T("hq.survival.note", _save.Career.SurvivalBest, BaseRules.Turrets(_save)),
                Button = Loc.T("hq.fly"), Press = () => { Click(); StartOperation(Operations.Survival(_island, _region.Id, BaseRules.Turrets(_save))); },
            });
            int raidSeed = _save.Career.ContractsDone + 7;
            rows.Add(new ListRow
            {
                Title = Loc.T("op.raid.title"), Note = Loc.T("hq.raid.note", level),
                Button = Loc.T("hq.fly"), Press = () => { Click(); StartOperation(Operations.Raid(_island, _region.Id, raidSeed, level)); },
            });
            rows.Add(new ListRow
            {
                Title = Loc.T("op.range.title"), Note = Loc.T("hq.range.note"),
                Button = Loc.T("hq.fly"), Press = () => { Click(); StartOperation(Operations.Range(_island, _region.Id)); },
            });
            // The ring course against the clock is where it always was.
            rows.Add(new ListRow
            {
                Title = Loc.T("hq.race"), Note = Loc.T("hq.race.note", _save.RingBest > 0f ? Format.Time(_save.RingBest) : "—"),
                Button = Loc.T("hq.fly"), Press = () => { Click(); _hideOperations(); StartFreeFlight(); },
            });
            footer = footer ?? Loc.T("hq.special.note");
        }

        void WarRows(List<ListRow> rows, ref string footer)
        {
            int level = Career.ContractLevel(_save), held = War.HeldIn(_save, _region.Id);
            for (int i = 0; i < War.SectorsPerRegion; i++)
            {
                int sector = i;
                bool ours = War.Held(_save, _region.Id, sector);
                var row = new ListRow
                {
                    Title = Loc.T("war.sector", sector + 1) + "   " + (ours
                        ? "<color=#59D973>" + Loc.T("war.held") + "</color>"
                        : "<color=#F24D40>" + Loc.T("war.enemy") + "</color>"),
                    Note = ours ? Loc.T("war.held.note", War.IncomePerSector) : Loc.T("war.enemy.note", level),
                    Lit = ours,
                };
                if (!ours)
                {
                    row.Button = Loc.T("war.attack");
                    row.Press = () =>
                    {
                        Click();
                        StartOperation(Operations.Contract(_island, _region.Id, 900 + sector + _save.Career.ContractsDone, level, null, sector), sector);
                    };
                }
                rows.Add(row);
            }
            footer = footer ?? Loc.T("war.note", Loc.T(_region.NameKey), held, War.SectorsPerRegion);
        }

        void CustomRows(List<ListRow> rows, ref string footer)
        {
            var kind = (OperationKind)(_customKind % Operations.KindCount);
            string[] weathers = { "clear", "sunset", "overcast", "rain", "fog", "storm", "dust", "snow", "night" };
            string weather = weathers[_customWeather % weathers.Length];
            int max = Mathf.Max(1, Career.ContractLevel(_save) + 3);
            _customLevel = Mathf.Clamp(_customLevel, 1, max);
            rows.Add(new ListRow
            {
                Title = Loc.T("custom.kind") + ":  " + Loc.T("op." + kind.ToString().ToLowerInvariant() + ".name"),
                Note = Loc.T("op." + kind.ToString().ToLowerInvariant() + ".brief"), Button = Loc.T("custom.change"),
                Press = () => { Click(); _customKind++; ShowHeadquarters(true); },
            });
            rows.Add(new ListRow
            {
                Title = Loc.T("custom.level") + ":  " + _customLevel, Note = Loc.T("custom.level.note", max),
                Progress = _customLevel / (float)max, Button = Loc.T("custom.change"),
                Press = () => { Click(); _customLevel = _customLevel >= max ? 1 : _customLevel + 1; ShowHeadquarters(true); },
            });
            rows.Add(new ListRow
            {
                Title = Loc.T("custom.weather") + ":  " + Loc.T(WeatherCatalog.Get(weather).NameKey), Note = Loc.T("custom.weather.note"),
                Button = Loc.T("custom.change"), Press = () => { Click(); _customWeather++; ShowHeadquarters(true); },
            });
            int level = _customLevel;
            rows.Add(new ListRow
            {
                Title = Loc.T("custom.fly"), Note = Loc.T("custom.fly.note", Loc.T(_region.NameKey)), Button = Loc.T("hq.fly"), Lit = true,
                Press = () => { Click(); StartOperation(Operations.Contract(_island, _region.Id, 31 + _customKind * 13 + level, level, kind, -1, weather)); },
            });
            footer = footer ?? Loc.T("custom.note");
        }

        // ---------------------------------------------------------------- career

        void RankRows(List<ListRow> rows, ref string footer)
        {
            CareerData career = _save.Career;
            int rank = Career.Rank(career.Xp);
            int from = Career.XpFor(rank), to = rank < Career.Ranks ? Career.XpFor(rank + 1) : from + 1;
            string stars = career.Prestige > 0 ? "  " + new string('*', career.Prestige) : "";
            rows.Add(new ListRow
            {
                Title = Loc.T("title." + career.Emblem).ToUpperInvariant() + "  ·  " + Career.RankName(rank) + stars, Lit = true,
                Note = rank < Career.Ranks ? Loc.T("hq.rank.note", career.Xp - from, to - from, Career.RankReward(rank + 1).Describe()) : Loc.T("hq.rank.top"),
                Progress = rank < Career.Ranks ? (career.Xp - from) / (float)(to - from) : 1f,
            });
            rows.Add(new ListRow
            {
                Title = Loc.T("hq.prestige", career.Prestige, Career.MaxPrestige), Note = Loc.T("hq.prestige.note"),
                Button = Loc.T("hq.prestige.take"), Enabled = Career.CanPrestige(_save),
                Press = () =>
                {
                    Reward reward = Career.TakePrestige(_save);
                    Did(reward != null, reward != null ? Loc.T("hq.prestige.done", reward.Describe()) : Loc.T("hq.prestige.refused"));
                },
            });
            DroneDefinition drone = Garage.Selected(_save);
            int kills = Mastery.Kills(_save, drone.Id), mastery = Mastery.Level(kills);
            int goal = mastery < Mastery.Levels.Length ? Mastery.Levels[mastery] : kills;
            rows.Add(new ListRow
            {
                Title = Loc.T("hq.mastery", Loc.T(drone.NameKey), mastery, Mastery.Levels.Length),
                Note = Loc.T("hq.mastery.note", kills, goal), Progress = goal > 0 ? Mathf.Clamp01(kills / (float)goal) : 1f,
            });
            rows.Add(new ListRow
            {
                Title = Loc.T("hq.records"),
                Note = Loc.T("hq.records.note", career.SurvivalBest, career.TowerFloor, career.ContractsDone, _save.Stats.Kills, career.Sectors.Count),
            });
            rows.Add(new ListRow
            {
                Title = Loc.T("hq.haptics") + ":  " + Loc.T(career.Haptics ? "settings.on" : "settings.off"), Note = Loc.T("hq.haptics.note"),
                Button = Loc.T("custom.change"), Press = () => { career.Haptics = !career.Haptics; if (career.Haptics) Buzz(); Did(true, null); },
            });
        }

        void TaskRows(List<ListRow> rows, bool weekly, ref string footer)
        {
            foreach (TaskDefinition task in Tasks.For(weekly ? _save.Career.TaskWeek : _save.Career.TaskDay, weekly))
            {
                TaskState state = Tasks.State(_save, task);
                if (state == null) continue;
                bool done = state.Progress >= task.Goal;
                TaskDefinition captured = task;
                rows.Add(new ListRow
                {
                    Title = task.Text, Note = state.Progress + " / " + task.Goal + "   ·   " + task.Reward.Describe(),
                    Progress = state.Progress / (float)task.Goal, Lit = done && !state.Claimed,
                    Button = state.Claimed ? Loc.T("hq.claimed") : Loc.T("hq.claim"), Enabled = done && !state.Claimed,
                    Press = () =>
                    {
                        var gain = new CareerGain();
                        Reward reward = Tasks.Claim(_save, captured, gain);
                        if (reward != null) gain.Rewards.Grant(_save);
                        Did(reward != null, reward != null ? reward.Describe() + (gain.News.Count > 0 ? "   " + string.Join("  ", gain.News) : "") : Loc.T("hq.not_yet"));
                    },
                });
            }
            footer = footer ?? Loc.T(weekly ? "hq.weekly.note" : "hq.daily.note");
        }

        void SeasonRows(List<ListRow> rows, ref string footer)
        {
            CareerData career = _save.Career;
            int tier = Seasons.Tier(_save), unclaimed = Seasons.Unclaimed(_save);
            rows.Add(new ListRow
            {
                Title = Loc.T("season.title", career.Season, tier, Seasons.Tiers), Lit = unclaimed > 0,
                Note = Loc.T("season.note", Seasons.DaysLeft(_today), career.SeasonXp % Seasons.XpPerTier, Seasons.XpPerTier),
                Progress = tier >= Seasons.Tiers ? 1f : (career.SeasonXp % Seasons.XpPerTier) / (float)Seasons.XpPerTier,
                Button = unclaimed > 0 ? Loc.T("season.claim", unclaimed) : Loc.T("hq.claimed"), Enabled = unclaimed > 0,
                Press = () =>
                {
                    Reward reward = Seasons.Claim(_save);
                    Did(reward != null, reward != null ? reward.Describe() : Loc.T("hq.not_yet"));
                },
            });
            for (int t = 1; t <= Seasons.Tiers; t++)
            {
                bool reached = t <= tier;
                rows.Add(new ListRow
                {
                    Title = Loc.T("season.tier", t) + (reached ? "   <color=#59D973>" + Loc.T("season.reached") + "</color>" : ""),
                    Note = Seasons.TierReward(t, career.Season).Describe(), Lit = reached && t > career.SeasonPaid,
                    Tint = reached ? (Color?)null : new Color(1f, 1f, 1f, 0.6f),
                });
            }
            footer = footer ?? Loc.T("season.footer");
        }

        // ---------------------------------------------------------------- base and armoury

        void UpgradeRows(List<ListRow> rows, bool armoury, ref string footer)
        {
            int[] levels = armoury ? _save.Career.Armoury : _save.Career.Base;
            string[] ids = armoury ? ArmouryRules.Ids : BaseRules.Ids;
            for (int i = 0; i < ids.Length; i++)
            {
                int track = i, level = levels[i];
                int credits = 0, parts = 0;
                bool maxed = level >= BaseRules.MaxLevel;
                if (!maxed)
                {
                    if (armoury) ArmouryRules.Cost(track, level, out credits, out parts);
                    else BaseRules.Cost(track, level, out credits, out parts);
                }
                string key = (armoury ? "armoury." : "base.") + ids[i];
                rows.Add(new ListRow
                {
                    Title = Loc.T(key + ".name") + "   <color=" + UiKit.CyanHex + ">" + level + " / " + BaseRules.MaxLevel + "</color>",
                    Note = Loc.T(key + ".note"), Progress = level / (float)BaseRules.MaxLevel,
                    Button = maxed ? Loc.T("hq.maxed") : Cost(credits, parts),
                    Enabled = !maxed && _save.Credits >= credits && _save.Parts >= parts,
                    Press = () =>
                    {
                        bool ok = armoury ? ArmouryRules.Upgrade(_save, track) : BaseRules.Upgrade(_save, track);
                        Did(ok, ok ? Loc.T("hq.built", Loc.T(key + ".name")) : Loc.T("hq.cannot_afford"));
                    },
                });
            }
            footer = footer ?? Loc.T(armoury ? "hq.armoury.note" : "hq.base.note");
        }

        // ---------------------------------------------------------------- collection

        static string RarityColor(int rarity) => rarity <= 0 ? "#C8D2DC" : rarity == 1 ? "#4DA6FF" : rarity == 2 ? "#C266FF" : "#FFB830";

        void PaintRows(List<ListRow> rows, ref string footer)
        {
            DroneDefinition drone = Garage.Selected(_save);
            SkinDefinition worn = Skins.Worn(_save, drone.Id);
            rows.Add(new ListRow
            {
                Title = Loc.T("paint.factory"), Note = Loc.T("paint.factory.note", Loc.T(drone.NameKey)), Lit = worn == null,
                Button = worn == null ? Loc.T("paint.worn") : Loc.T("paint.wear"), Enabled = worn != null,
                Press = () => { Skins.Wear(_save, drone.Id, null); Repaint(); Did(true, null); },
            });
            foreach (SkinDefinition skin in Skins.All)
            {
                bool owned = _save.Career.Skins.Contains(skin.Id), on = worn != null && worn.Id == skin.Id;
                SkinDefinition captured = skin;
                rows.Add(new ListRow
                {
                    Title = "<color=" + RarityColor(skin.Rarity) + ">" + Loc.T("skin." + skin.Id) + "</color>",
                    Note = Loc.T("rarity." + skin.Rarity) + (owned ? "" : "  ·  " + Loc.T(skin.Special ? "paint.earned" : "paint.from_cases")),
                    Lit = on, Tint = owned ? (Color?)null : new Color(1f, 1f, 1f, 0.55f),
                    Button = on ? Loc.T("paint.worn") : owned ? Loc.T("paint.wear") : Loc.T("paint.locked"), Enabled = owned && !on,
                    Press = () =>
                    {
                        bool ok = Skins.Wear(_save, drone.Id, captured.Id);
                        if (ok) Repaint();
                        Did(ok, ok ? null : Loc.T("paint.not_owned"));
                    },
                });
            }
            footer = footer ?? Loc.T("paint.note", Loc.T(drone.NameKey));
        }

        /// <summary>Rebuilds the aircraft on the pad in whatever it wears now.</summary>
        void Repaint()
        {
            _shownDrone = null;
            ShowDroneModel(Garage.Selected(_save));
        }

        void CaseRows(List<ListRow> rows, ref string footer)
        {
            for (int tier = 0; tier < CaseRules.Tiers; tier++)
            {
                int t = tier, have = _save.Career.Cases[tier];
                int[] odds = CaseRules.Odds[tier];
                string chances = Loc.T("case.odds", odds[0], odds[1], odds[2], odds[3], odds[4], odds[5]);
                rows.Add(new ListRow
                {
                    Title = "<color=" + RarityColor(tier + 1) + ">" + Loc.T("case." + tier) + "</color>   ×" + have, Note = chances, Lit = have > 0,
                    Button = Loc.T("case.open"), Enabled = have > 0,
                    Press = () =>
                    {
                        CaseDrop drop = CaseRules.Open(_save, t, _caseRandom);
                        if (drop == null)
                        {
                            Did(false, Loc.T("case.none"));
                            return;
                        }
                        string got = drop.Skin != null
                            ? "<color=" + RarityColor(drop.Rarity) + ">" + Loc.T("rarity." + drop.Rarity) + ": " + Loc.T("skin." + drop.Skin) + "</color>" +
                              (drop.Duplicate ? "  " + Loc.T("case.duplicate", drop.Credits) : "")
                            : drop.Credits > 0 ? Loc.T("reward.credits", drop.Credits) : Loc.T("reward.parts", drop.Parts);
                        if (drop.Rarity >= 2) Buzz();
                        Did(true, Loc.T("case.got", got));
                    },
                });
                int credits = CaseRules.CreditPrice[tier], parts = CaseRules.PartPrice[tier];
                rows.Add(new ListRow
                {
                    Title = Loc.T("case.buy", Loc.T("case." + tier)), Note = Loc.T("case.buy.note"),
                    Button = Cost(credits, parts), Enabled = _save.Credits >= credits && _save.Parts >= parts,
                    Press = () =>
                    {
                        bool ok = CaseRules.Buy(_save, t);
                        Did(ok, ok ? Loc.T("case.bought") : Loc.T("hq.cannot_afford"));
                    },
                });
            }
            footer = footer ?? Loc.T("case.note");
        }

        void TitleRows(List<ListRow> rows, ref string footer)
        {
            foreach (string id in Emblems.All)
            {
                bool owned = _save.Career.Emblems.Contains(id), on = _save.Career.Emblem == id;
                string captured = id;
                rows.Add(new ListRow
                {
                    Title = Loc.T("title." + id).ToUpperInvariant(), Note = Loc.T("title." + id + ".how"), Lit = on,
                    Tint = owned ? (Color?)null : new Color(1f, 1f, 1f, 0.55f),
                    Button = on ? Loc.T("paint.worn") : owned ? Loc.T("paint.wear") : Loc.T("paint.locked"), Enabled = owned && !on,
                    Press = () =>
                    {
                        if (!_save.Career.Emblems.Contains(captured))
                        {
                            Did(false, Loc.T("paint.not_owned"));
                            return;
                        }
                        _save.Career.Emblem = captured;
                        Did(true, null);
                    },
                });
            }
            footer = footer ?? Loc.T("title.note");
        }

        // ---------------------------------------------------------------- for the screenshot run

        public void AutomationHeadquarters(int which, int tab)
        {
            // Something to show: money, cases, a few skins, a rank, some sectors.
            _save.Credits = Mathf.Max(_save.Credits, 9000);
            _save.Parts = Mathf.Max(_save.Parts, 40);
            if (_save.Career.Xp < 5000) _save.Career.Xp = 5200;
            _save.Career.SeasonXp = Mathf.Max(_save.Career.SeasonXp, 1900);
            for (int i = 0; i < CaseRules.Tiers; i++) _save.Career.Cases[i] = Mathf.Max(_save.Career.Cases[i], 2 - i / 2);
            foreach (string skin in new[] { "olive", "cobalt", "ember", "obsidian" })
                if (!_save.Career.Skins.Contains(skin)) _save.Career.Skins.Add(skin);
            for (int i = 0; i < 5; i++) War.Capture(_save, _region.Id, i * 2);
            _save.Career.SurvivalBest = Mathf.Max(_save.Career.SurvivalBest, 7);
            _hq = (Hq)which;
            _hqTab = tab;
            _hqNote = null;
            ShowHeadquarters(false);
        }

        public void AutomationCloseHeadquarters()
        {
            _list.Visible = false;
            _hq = Hq.None;
        }

        /// <summary>Starts a generated operation of a kind; returns where its first hostile target stands.</summary>
        public Vector3 AutomationOperation(int kind, int level)
        {
            MissionDefinition op = kind >= 0 ? Operations.Contract(_island, _region.Id, 40 + kind, level, (OperationKind)kind)
                : kind == -1 ? Operations.Survival(_island, _region.Id, 3)
                : kind == -2 ? Operations.Raid(_island, _region.Id, 3, level)
                : Operations.Range(_island, _region.Id);
            StartOperation(op);
            foreach (TargetDefinition target in op.Targets)
                if (!target.Friendly) return target.Path.Count > 0 ? target.Path[0] : target.Position;
            return _island.Spawn;
        }

        public void AutomationAbility() => _abilityPressed = true;

        public void AutomationWear(string skin)
        {
            if (!_save.Career.Skins.Contains(skin)) _save.Career.Skins.Add(skin);
            Skins.Wear(_save, Garage.Selected(_save).Id, skin);
            Repaint();
        }

        public void AutomationTransmission(int beat)
        {
            _transmission.Show(Loc.T("story." + beat + ".speaker"), Loc.T("story." + beat + ".text"));
        }

        public void AutomationHideTransmission() => _transmission.Visible = false;

        // ---------------------------------------------------------------- the story

        /// <summary>
        /// Between operations someone has something to say: the operator, or the other side.
        /// Each transmission is shown once, when the campaign has got far enough for it.
        /// </summary>
        void TellStory()
        {
            if (_transmission == null || _transmission.Visible || SkipIntro) return;
            int done = 0;
            foreach (MissionRecord record in _save.Missions)
                if (record.Completions > 0 && !record.Id.StartsWith("vet:")) done++;
            for (int i = 0; i < Story.Beats.Length; i++)
            {
                string id = "beat" + i;
                if (done < Story.Beats[i] || _save.Career.Story.Contains(id)) continue;
                _save.Career.Story.Add(id);
                _store.Save(_save);
                _sfx.Play(Sound.Radio);
                _transmission.Show(Loc.T("story." + i + ".speaker"), Loc.T("story." + i + ".text"));
                return;
            }
        }
    }
}
