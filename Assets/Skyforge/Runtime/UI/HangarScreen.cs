using System;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>
    /// The hangar: browse the fleet, buy and select drones, upgrade them and fit modules.
    /// Every purchase takes two taps on the same button, so nothing is bought by accident.
    /// </summary>
    public sealed class HangarScreen
    {
        /// <summary>The player is looking at another drone; the game shows its model.</summary>
        public event Action<DroneDefinition> Viewed;
        /// <summary>Something was bought, selected or fitted; the game saves and re-reads the loadout.</summary>
        public event Action Changed;
        public event Action BackPressed;
        /// <summary>A button was pressed; true when the press was refused.</summary>
        public event Action<bool> Pressed;

        const int Rows = 6;
        static readonly string[] StatKeys = { "stat.speed", "stat.agility", "stat.battery", "stat.range", "stat.optics", "stat.stealth" };

        sealed class Row
        {
            public GameObject Root;
            public Text Title, Note, ButtonLabel;
            public Image Button;
        }

        readonly GameObject _root;
        readonly Text _wallet, _name, _role, _note, _mainLabel, _status, _weapons;
        readonly Image _mainButton;
        readonly RectTransform[] _statFills = new RectTransform[6];
        readonly Image[] _tabs = new Image[2];
        readonly Text[] _tabLabels = new Text[2];
        readonly Row[] _rows = new Row[Rows];

        SaveData _save;
        int _index;
        int _tab;
        /// <summary>The button waiting for its confirming second tap: "main" or "row3", or null.</summary>
        string _pending;

        public HangarScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Hangar", 15);
            _root = canvas.gameObject;
            RectTransform safe = UiKit.SafeArea(canvas.transform);
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);

            Text back = UiKit.Button(safe, "Back", UiKit.SolidPanelColor, 30, () => BackPressed?.Invoke());
            back.text = Loc.T("hangar.back");
            UiKit.Place((RectTransform)back.transform.parent, topLeft, new Vector2(36f, -26f), new Vector2(280f, 84f));

            Image panel = UiKit.Box(safe, "Panel", UiKit.SolidPanelColor);
            panel.rectTransform.anchorMin = new Vector2(1f, 0f);
            panel.rectTransform.anchorMax = new Vector2(1f, 1f);
            panel.rectTransform.pivot = new Vector2(1f, 0.5f);
            panel.rectTransform.anchoredPosition = Vector2.zero;
            panel.rectTransform.sizeDelta = new Vector2(1060f, 0f);
            Transform p = panel.transform;

            Text title = UiKit.Label(p, 44, TextAnchor.UpperLeft);
            title.text = Loc.T("hangar.title");
            title.color = UiKit.Accent;
            UiKit.Place(title.rectTransform, topLeft, new Vector2(50f, -30f), new Vector2(400f, 56f));
            _wallet = UiKit.Label(p, 28, TextAnchor.UpperRight);
            UiKit.Place(_wallet.rectTransform, topRight, new Vector2(-50f, -40f), new Vector2(620f, 40f));

            _name = UiKit.Label(p, 60, TextAnchor.UpperLeft);
            UiKit.Place(_name.rectTransform, topLeft, new Vector2(48f, -92f), new Vector2(390f, 72f));
            UiKit.FitWidth(_name, 34);
            _role = UiKit.Label(p, 28, TextAnchor.UpperLeft);
            _role.color = UiKit.Accent;
            UiKit.Place(_role.rectTransform, topLeft, new Vector2(52f, -166f), new Vector2(700f, 38f));
            UiKit.FitWidth(_role, 18);
            // Two short lines beside the role, clear of the browse buttons on the right.
            _weapons = UiKit.Label(p, 21, TextAnchor.UpperLeft, true);
            UiKit.Light(_weapons);
            _weapons.color = new Color(1f, 1f, 1f, 0.85f);
            UiKit.Place(_weapons.rectTransform, topLeft, new Vector2(450f, -98f), new Vector2(320f, 62f));
            UiKit.FitWidth(_weapons, 15);

            Text previous = UiKit.Button(p, "Previous", UiKit.Faint, 44, () => Browse(-1));
            previous.text = "<";
            UiKit.Place((RectTransform)previous.transform.parent, topRight, new Vector2(-170f, -100f), new Vector2(110f, 96f));
            Text next = UiKit.Button(p, "Next", UiKit.Faint, 44, () => Browse(1));
            next.text = ">";
            UiKit.Place((RectTransform)next.transform.parent, topRight, new Vector2(-50f, -100f), new Vector2(110f, 96f));

            _note = UiKit.Label(p, 26, TextAnchor.UpperLeft, true);
            UiKit.Light(_note);
            UiKit.Place(_note.rectTransform, topLeft, new Vector2(52f, -210f), new Vector2(960f, 70f));

            // Six stats in two columns of three.
            for (int i = 0; i < 6; i++)
            {
                float x = 52f + (i / 3) * 490f, y = -290f - (i % 3) * 38f;
                Text label = UiKit.Label(p, 22, TextAnchor.MiddleLeft);
                label.text = Loc.T(StatKeys[i]);
                label.color = new Color(1f, 1f, 1f, 0.75f);
                UiKit.Place(label.rectTransform, topLeft, new Vector2(x, y), new Vector2(180f, 30f));
                Image bar = UiKit.Box(p, "Bar", UiKit.Faint);
                UiKit.Place(bar.rectTransform, topLeft, new Vector2(x + 190f, y - 10f), new Vector2(260f, 10f));
                Image fill = UiKit.Box(bar.transform, "Fill", Color.white);
                UiKit.Stretch(fill.rectTransform);
                _statFills[i] = fill.rectTransform;
            }

            _mainLabel = UiKit.Button(p, "Main", UiKit.Accent, 34, () => MainPressed());
            var mainRect = (RectTransform)_mainLabel.transform.parent;
            UiKit.Place(mainRect, topLeft, new Vector2(50f, -416f), new Vector2(470f, 88f));
            _mainButton = mainRect.GetComponent<Image>();
            _status = UiKit.Label(p, 24, TextAnchor.MiddleLeft, true);
            _status.color = UiKit.Bad;
            UiKit.Place(_status.rectTransform, topLeft, new Vector2(540f, -416f), new Vector2(470f, 88f));

            for (int i = 0; i < 2; i++)
            {
                int tab = i;
                _tabLabels[i] = UiKit.Button(p, "Tab" + i, UiKit.Faint, 28, () => SelectTab(tab));
                _tabLabels[i].text = Loc.T(i == 0 ? "hangar.tab.upgrades" : "hangar.tab.modules");
                var rect = (RectTransform)_tabLabels[i].transform.parent;
                UiKit.Place(rect, topLeft, new Vector2(50f + i * 340f, -522f), new Vector2(326f, 64f));
                _tabs[i] = rect.GetComponent<Image>();
            }

            for (int i = 0; i < Rows; i++)
            {
                int row = i;
                var r = new Row();
                RectTransform root = UiKit.Rect("Row" + i, p);
                UiKit.Place(root, topLeft, new Vector2(50f, -600f - i * 78f), new Vector2(960f, 72f));
                r.Root = root.gameObject;
                r.Title = UiKit.Label(root, 28, TextAnchor.UpperLeft);
                UiKit.Place(r.Title.rectTransform, topLeft, new Vector2(0f, 0f), new Vector2(620f, 36f));
                r.Note = UiKit.Label(root, 21, TextAnchor.UpperLeft);
                UiKit.Light(r.Note);
                r.Note.color = new Color(1f, 1f, 1f, 0.7f);
                UiKit.Place(r.Note.rectTransform, topLeft, new Vector2(0f, -38f), new Vector2(620f, 30f));
                r.ButtonLabel = UiKit.Button(root, "Button", UiKit.Faint, 24, () => RowPressed(row));
                var buttonRect = (RectTransform)r.ButtonLabel.transform.parent;
                UiKit.Place(buttonRect, topRight, new Vector2(0f, -2f), new Vector2(320f, 66f));
                r.Button = buttonRect.GetComponent<Image>();
                _rows[i] = r;
            }
        }

        public bool Visible
        {
            get => _root.activeSelf;
            set => _root.SetActive(value);
        }

        public void Show(SaveData save)
        {
            _save = save;
            _index = DroneCatalog.IndexOf(save.SelectedDrone);
            _pending = null;
            Visible = true;
            Refresh();
            Viewed?.Invoke(Current);
        }

        /// <summary>Opens on a given drone and tab; used by the screenshot run.</summary>
        public void Show(SaveData save, string droneId, int tab)
        {
            Show(save);
            _index = DroneCatalog.IndexOf(droneId);
            _tab = tab;
            Refresh();
            Viewed?.Invoke(Current);
        }

        DroneDefinition Current => DroneCatalog.All[_index];

        void Browse(int step)
        {
            int count = DroneCatalog.All.Count;
            _index = (_index + step + count) % count;
            _pending = null;
            Pressed?.Invoke(false);
            Refresh();
            Viewed?.Invoke(Current);
        }

        void SelectTab(int tab)
        {
            _tab = tab;
            _pending = null;
            Pressed?.Invoke(false);
            Refresh();
        }

        /// <summary>First tap arms a purchase, second tap on the same button makes it.</summary>
        bool Confirmed(string key)
        {
            if (_pending == key)
            {
                _pending = null;
                return true;
            }
            _pending = key;
            return false;
        }

        void MainPressed()
        {
            DroneDefinition drone = Current;
            if (Garage.Owns(_save, drone.Id))
            {
                _pending = null;
                Garage.Select(_save, drone.Id);
                Finish(PurchaseResult.Ok);
                return;
            }
            PurchaseResult possible = Garage.CanBuyDrone(_save, drone);
            if (possible != PurchaseResult.Ok)
            {
                Finish(possible);
                return;
            }
            if (!Confirmed("main"))
            {
                Pressed?.Invoke(false);
                Refresh();
                return;
            }
            Finish(Garage.BuyDrone(_save, drone));
        }

        void RowPressed(int row)
        {
            DroneDefinition drone = Current;
            if (_tab == 0)
            {
                var track = (UpgradeTrack)row;
                PurchaseResult possible = Garage.CanUpgrade(_save, drone, track);
                if (possible != PurchaseResult.Ok)
                {
                    Finish(possible);
                    return;
                }
                if (!Confirmed("row" + row))
                {
                    Pressed?.Invoke(false);
                    Refresh();
                    return;
                }
                Finish(Garage.Upgrade(_save, drone, track));
                return;
            }

            string module = Loadout.Modules[row].Id;
            if (Garage.OwnsModule(_save, module))
            {
                _pending = null;
                Finish(Garage.ToggleModule(_save, drone.Id, module));
                return;
            }
            if (_save.Credits < Loadout.Modules[row].Price)
            {
                Finish(PurchaseResult.NotEnoughCredits);
                return;
            }
            if (!Confirmed("row" + row))
            {
                Pressed?.Invoke(false);
                Refresh();
                return;
            }
            Finish(Garage.BuyModule(_save, module));
        }

        void Finish(PurchaseResult result)
        {
            bool refused = result != PurchaseResult.Ok;
            if (refused) _pending = null;
            Pressed?.Invoke(refused);
            Refresh();
            if (refused) _status.text = Reason(result, Current);
            if (!refused)
            {
                Changed?.Invoke();
                Viewed?.Invoke(Current);
            }
        }

        string Reason(PurchaseResult result, DroneDefinition drone)
        {
            switch (result)
            {
                case PurchaseResult.NotEnoughCredits: return Loc.T("hangar.need_credits");
                case PurchaseResult.NotEnoughParts: return Loc.T("hangar.need_parts");
                case PurchaseResult.NotEnoughStars: return Loc.T("hangar.need_stars", drone.StarsRequired, Garage.TotalStars(_save));
                case PurchaseResult.NotOwned: return Loc.T("hangar.not_owned");
                default: return "";
            }
        }

        void Refresh()
        {
            DroneDefinition drone = Current;
            bool owned = Garage.Owns(_save, drone.Id);
            DroneSpec spec = Garage.Spec(_save, drone);

            _wallet.text = Loc.T("menu.credits", _save.Credits) + "     " + Loc.T("menu.parts", _save.Parts);
            _name.text = Loc.T(drone.NameKey);
            _role.text = Loc.T(drone.RoleKey);
            // What it carries besides rockets and bombs, said in words after the note.
            var gear = new System.Collections.Generic.List<string>();
            if (drone.Spec.Cannon > 0f) gear.Add(Loc.T("gear.cannon"));
            if (drone.Spec.Cluster) gear.Add(Loc.T("gear.cluster"));
            if (drone.Spec.Ability != Ability.None) gear.Add(Loc.T("gear." + drone.Spec.Ability.ToString().ToLowerInvariant()));
            _note.text = Loc.T(drone.NoteKey) + (gear.Count > 0 ? " <color=" + UiKit.CyanHex + ">" + Loc.T("gear.fitted", string.Join(", ", gear)) + "</color>" : "");
            _note.supportRichText = true;
            // What it fights with, straight from the numbers the game uses.
            DroneSpec arms = drone.Spec;
            _weapons.text = arms.OneWay > 0
                ? Loc.T("hangar.oneway", arms.OneWay)
                : Loc.T("hangar.weapons", arms.Rockets, Mathf.RoundToInt(arms.RocketDamage), arms.Bombs,
                    Mathf.RoundToInt(arms.BombDamage), Mathf.RoundToInt(arms.LockRange));
            _status.text = "";

            float[] stats =
            {
                spec.MaxSpeed / 32f,
                spec.TiltResponse / 10f,
                spec.BatterySeconds / 1200f,
                spec.SignalRange / 2600f,
                spec.OpticsRange / 1.8f * 0.6f + spec.MaxZoom / 8f * 0.4f,
                1f - spec.DetectScale / 1.7f,
            };
            for (int i = 0; i < 6; i++) _statFills[i].anchorMax = new Vector2(Mathf.Clamp(stats[i], 0.03f, 1f), 1f);

            bool selected = _save.SelectedDrone == drone.Id;
            if (owned)
            {
                _mainLabel.text = Loc.T(selected ? "hangar.selected" : "hangar.select");
                _mainButton.color = selected ? UiKit.Faint : UiKit.Accent;
                _mainLabel.color = selected ? Color.white : new Color(0.08f, 0.06f, 0.02f);
            }
            else
            {
                bool can = Garage.CanBuyDrone(_save, drone) == PurchaseResult.Ok;
                _mainLabel.text = Loc.T(_pending == "main" ? "hangar.confirm" : "hangar.buy", drone.Price);
                _mainButton.color = can ? UiKit.Accent : UiKit.Faint;
                _mainLabel.color = can ? new Color(0.08f, 0.06f, 0.02f) : Color.white;
                if (Garage.TotalStars(_save) < drone.StarsRequired)
                    _status.text = Loc.T("hangar.need_stars", drone.StarsRequired, Garage.TotalStars(_save));
            }

            for (int i = 0; i < 2; i++)
            {
                _tabs[i].color = i == _tab ? UiKit.Lit : UiKit.Faint;
            }

            for (int i = 0; i < Rows; i++)
            {
                if (_tab == 0) RefreshUpgradeRow(i, drone, owned);
                else RefreshModuleRow(i, drone, owned);
            }
        }

        void RefreshUpgradeRow(int index, DroneDefinition drone, bool owned)
        {
            Row row = _rows[index];
            bool used = index < Loadout.Tracks;
            row.Root.SetActive(used);
            if (!used) return;

            var track = (UpgradeTrack)index;
            int level = Garage.Level(_save, drone.Id, track);
            row.Title.text = Loc.T("hangar.level", Loc.T("upgrade." + track), level);
            row.Note.text = Loc.T("upgrade." + track + ".note");

            if (level >= Loadout.MaxLevel)
            {
                SetButton(row, Loc.T("hangar.max"), false, false);
                return;
            }
            Loadout.UpgradeCost(drone, level, out int credits, out int parts);
            string cost = parts > 0 ? Loc.T("hangar.cost_parts", credits, parts) : Loc.T("hangar.cost", credits);
            bool can = owned && Garage.CanUpgrade(_save, drone, track) == PurchaseResult.Ok;
            SetButton(row, _pending == "row" + index ? Loc.T("hangar.confirm", cost) : cost, can, false);
        }

        void RefreshModuleRow(int index, DroneDefinition drone, bool owned)
        {
            Row row = _rows[index];
            bool used = index < Loadout.Modules.Length;
            row.Root.SetActive(used);
            if (!used) return;

            ModuleDefinition module = Loadout.Modules[index];
            row.Title.text = Loc.T(module.NameKey);
            row.Note.text = Loc.T(module.NoteKey);

            if (!Garage.OwnsModule(_save, module.Id))
            {
                bool can = _save.Credits >= module.Price;
                SetButton(row, Loc.T(_pending == "row" + index ? "hangar.confirm" : "hangar.buy", module.Price), can, false);
                return;
            }
            bool fitted = Garage.IsEquipped(_save, drone.Id, module.Id);
            SetButton(row, Loc.T(fitted ? "hangar.remove" : "hangar.fit"), owned, fitted);
        }

        /// <param name="enabled">The press would do something; a disabled button still answers with the reason.</param>
        /// <param name="active">The row's item is in use on this drone.</param>
        static void SetButton(Row row, string label, bool enabled, bool active)
        {
            row.ButtonLabel.text = label;
            row.Button.color = active ? UiKit.Good : enabled ? UiKit.Lit : UiKit.Faint;
            row.ButtonLabel.color = enabled || active ? Color.white : new Color(1f, 1f, 1f, 0.55f);
        }
    }
}
