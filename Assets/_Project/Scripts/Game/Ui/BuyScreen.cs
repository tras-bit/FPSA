using System.Collections.Generic;
using Kordon.Core.Rules;
using Kordon.Core.Ui;
using Kordon.Game.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// The demolition shop. Drawn from <see cref="BuyMenuModel"/>, so what is clickable
    /// here is exactly what the server would accept: the screen never decides a price, an
    /// affordability rule or a team restriction on its own.
    ///
    /// It is a plain overlay rather than a screen in the menu stack, because it opens and
    /// closes inside a live round and the player keeps their body: opening the shop does not
    /// pause the match, it only frees the cursor.
    /// </summary>
    public sealed class BuyScreen : MonoBehaviour
    {
        private DemolitionMatch _session;
        private Canvas _canvas;
        private RectTransform _root;
        private Text _moneyLine;
        private Text _hintLine;
        private bool _built;
        private float _refreshTimer;

        private readonly Dictionary<string, Button> _rowButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Text> _rowLabels = new Dictionary<string, Text>();

        public void Bind(DemolitionMatch session)
        {
            _session = session;
            Build();
        }

        private void Build()
        {
            if (_built || _session == null)
            {
                return;
            }

            _built = true;
            _canvas = UiBuilder.CreateCanvas("BuyCanvas", 40);
            _canvas.transform.SetParent(transform, false);
            _root = UiBuilder.FullRect(_canvas.transform, "BuyRoot");

            Image backdrop = UiBuilder.Panel(_root, "Backdrop",
                new Color(0.03f, 0.035f, 0.04f, 0.93f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(2000f, 2000f));
            backdrop.raycastTarget = true;

            Image frame = UiBuilder.Panel(_root, "Frame",
                new Color(0.06f, 0.07f, 0.08f, 0.98f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1180f, 720f));

            UiBuilder.LabelAt(frame.transform, "Title", "REQUISITION", 34, UiBuilder.Paper,
                new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(1100f, 40f),
                TextAnchor.MiddleLeft);

            _moneyLine = UiBuilder.LabelAt(frame.transform, "Money", "$0", 26, UiBuilder.Accent,
                new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(1100f, 30f),
                TextAnchor.MiddleLeft);

            _hintLine = UiBuilder.LabelAt(frame.transform, "Hint",
                "click to buy  /  B or ESC to close", 15, UiBuilder.Dim,
                new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(1100f, 22f),
                TextAnchor.MiddleLeft);

            BuildShelves(frame.transform);
            _root.gameObject.SetActive(false);
        }

        private void BuildShelves(Transform frame)
        {
            // The shelves come from the same model the clickability check uses, so a
            // category can never be drawn from one list and validated from another.
            List<BuyMenuSection> sections = BuyMenuModel.Build(_session.Match.Catalog, 0,
                _session.LocalTeam, _session.LocalBuy);
            int half = (sections.Count + 1) / 2;
            BuildColumn(frame, sections.GetRange(0, half), -300f);
            BuildColumn(frame, sections.GetRange(half, sections.Count - half), 300f);
        }

        private void BuildColumn(Transform frame, List<BuyMenuSection> sections, float offsetX)
        {
            float y = -130f;
            foreach (BuyMenuSection section in sections)
            {
                Text header = UiBuilder.LabelAt(frame, "Shelf" + section.Category, section.Title,
                    15, UiBuilder.Accent, new Vector2(0.5f, 1f),
                    new Vector2(offsetX, y), new Vector2(520f, 20f), TextAnchor.MiddleLeft);
                header.raycastTarget = false;
                y -= 26f;

                foreach (BuyMenuRow row in section.Rows)
                {
                    BuyMenuRow captured = row;
                    Button button = UiBuilder.Button(frame,
                        row.DisplayName + "   $" + row.Price,
                        () => Purchase(captured.ItemId), new Vector2(offsetX, y),
                        new Vector2(520f, 34f), 15);
                    _rowButtons[row.ItemId] = button;
                    _rowLabels[row.ItemId] = button.GetComponentInChildren<Text>();
                    y -= 38f;
                }

                y -= 10f;
            }
        }

        private void Purchase(string itemId)
        {
            if (_session == null)
            {
                return;
            }

            _session.Buy(itemId);
            Refresh();
        }

        private void Update()
        {
            if (_session == null || !_built)
            {
                return;
            }

            bool open = _session.BuyMenuOpen;
            if (_root.gameObject.activeSelf != open)
            {
                _root.gameObject.SetActive(open);
            }

            if (!open)
            {
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer > 0f)
            {
                return;
            }

            _refreshTimer = 0.25f;
            Refresh();
        }

        /// <summary>Rebuild the flags: money, what is owned, what the wallet cannot reach.</summary>
        private void Refresh()
        {
            if (_session == null || !_built)
            {
                return;
            }

            int money = _session.LocalEconomy != null ? _session.LocalEconomy.Money : 0;
            _moneyLine.text = "$" + money + "    " +
                              (_session.InBuyZone() ? "in buy zone" : "not in a buy zone");

            List<BuyMenuSection> sections = BuyMenuModel.Build(_session.Match.Catalog, money,
                _session.LocalTeam, _session.LocalBuy);

            foreach (BuyMenuSection section in sections)
            {
                foreach (BuyMenuRow row in section.Rows)
                {
                    if (!_rowButtons.TryGetValue(row.ItemId, out Button button))
                    {
                        continue;
                    }

                    bool selectable = BuyMenuModel.IsSelectable(row);
                    button.interactable = selectable;

                    if (_rowLabels.TryGetValue(row.ItemId, out Text label))
                    {
                        label.color = !row.Affordable
                            ? UiBuilder.Danger
                            : row.BlockedByTeam ? UiBuilder.Dim
                            : row.Owned ? UiBuilder.Warning
                            : UiBuilder.Paper;
                        label.text = row.DisplayName + "   $" + row.Price
                                     + (row.Owned ? "   (carried)" : string.Empty)
                                     + (row.BlockedByTeam ? "   (other side only)" : string.Empty);
                    }
                }
            }
        }
    }
}
