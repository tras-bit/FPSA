using Kordon.Game.Input;
using Kordon.Game.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Boot menu. Title, operator name, the two ways into the game (networked
    /// Free-for-All and offline practice), settings and quit. The footer always
    /// states which build and region this is, and warns when content validation
    /// found problems - no screen in KORDON hides an error behind a pretty button.
    /// </summary>
    public sealed class MainMenuScreen : UiScreenBase
    {
        public override string Name => "main";

        private Text _nameLabel;
        private Text _status;
        private Text _contentStatus;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            // Left: identity and the way into a match.
            Image left = UiBuilder.Panel(Root, "LeftPanel", new Color(0.05f, 0.06f, 0.07f, 0.55f),
                new Vector2(0f, 0.5f), new Vector2(330f, 0f), new Vector2(560f, 820f));

            Text title = UiBuilder.LabelAt(left.transform, "Title", "KORDON", 92,
                UiBuilder.Paper, new Vector2(0.5f, 1f), new Vector2(0f, -120f),
                new Vector2(520f, 110f), TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;

            UiBuilder.LabelAt(left.transform, "Subtitle", "VERANSK CORRIDOR  /  4TH ARMISTICE", 20,
                UiBuilder.Accent, new Vector2(0.5f, 1f), new Vector2(0f, -196f),
                new Vector2(520f, 30f), TextAnchor.MiddleLeft);
            UiBuilder.LabelAt(left.transform, "Blurb",
                "Free-for-All, 20 operators, first to 50 kills.\nNo teams. No respawn resupply. Your kit is locked when the round starts.",
                17, UiBuilder.Dim, new Vector2(0.5f, 1f), new Vector2(0f, -232f),
                new Vector2(520f, 70f), TextAnchor.UpperLeft);

            UiBuilder.Button(left.transform, "PLAY - FREE-FOR-ALL", () =>
            {
                Flow.CoopRequested = false;
                Flow.OpenConnect();
            }, new Vector2(0f, 68f), new Vector2(480f, 58f));
            UiBuilder.Button(left.transform, "CO-OP OPERATION (2-4)", () =>
            {
                Flow.CoopRequested = true;
                Flow.OpenConnect();
            }, new Vector2(0f, 6f), new Vector2(480f, 50f), 18);
            UiBuilder.Button(left.transform, "PRACTICE (OFFLINE, NO BOTS)", () => Flow.OpenPrep(),
                new Vector2(0f, -52f), new Vector2(480f, 50f), 18);
            UiBuilder.Button(left.transform, "SETTINGS & CONTROLS", () => Flow.OpenSettings(),
                new Vector2(0f, -110f), new Vector2(480f, 50f), 18);
            UiBuilder.Button(left.transform, "QUIT", () => Flow.Quit(),
                new Vector2(0f, -168f), new Vector2(480f, 46f), 16);

            // Right: operator identity and honest build information.
            Image right = UiBuilder.Panel(Root, "RightPanel", new Color(0.05f, 0.06f, 0.07f, 0.55f),
                new Vector2(1f, 0.5f), new Vector2(-330f, 0f), new Vector2(560f, 820f));

            UiBuilder.LabelAt(right.transform, "OperatorHeader", "OPERATOR", 18, UiBuilder.Dim,
                new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(480f, 24f),
                TextAnchor.MiddleLeft);

            InputField field = UiBuilder.Field(right.transform, "callsign",
                new Vector2(0f, -60f), new Vector2(480f, 52f), PlayerProfile.Name);
            field.onEndEdit.AddListener(value =>
            {
                PlayerProfile.Name = value;
                RefreshName();
            });
            field.onValueChanged.AddListener(value =>
            {
                PlayerProfile.Name = value;
                RefreshName();
            });

            _nameLabel = UiBuilder.LabelAt(right.transform, "NameLabel", "", 20, UiBuilder.Paper,
                new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(480f, 26f),
                TextAnchor.MiddleLeft);

            UiBuilder.LabelAt(right.transform, "ContentHeader", "CONTENT", 18, UiBuilder.Dim,
                new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(480f, 24f),
                TextAnchor.MiddleLeft);

            _contentStatus = UiBuilder.LabelAt(right.transform, "ContentStatus", "", 17,
                UiBuilder.Dim, new Vector2(0.5f, 1f), new Vector2(0f, -230f),
                new Vector2(480f, 240f), TextAnchor.UpperLeft);

            UiBuilder.LabelAt(right.transform, "Controls",
                "WASD move   Shift sprint   Ctrl crouch   Z prone\nQ/E lean   R tap reload / hold inspect\n1/2/4 slots   Tab scoreboard   Esc pause",
                16, UiBuilder.Dim, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(480f, 80f), TextAnchor.LowerLeft);

            _status = UiBuilder.LabelAt(Root, "Status", "", 18, UiBuilder.Warning,
                new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(1600f, 26f),
                TextAnchor.MiddleCenter);

            RefreshName();
            RefreshContentStatus();
        }

        public override void OnShow()
        {
            Flow.StatusChanged += RefreshStatus;
            RefreshName();
            RefreshContentStatus();
            RefreshStatus();
        }

        public override void OnHide()
        {
            Flow.StatusChanged -= RefreshStatus;
        }

        private void RefreshName()
        {
            if (_nameLabel != null)
            {
                _nameLabel.text = "shows as: " + PlayerProfile.Name;
            }
        }

        private void RefreshStatus()
        {
            if (_status != null)
            {
                _status.text = Flow.StatusLine;
            }
        }

        private void RefreshContentStatus()
        {
            if (_contentStatus == null)
            {
                return;
            }

            int weapons = Flow.Database.Definitions.Weapons.Count;
            int magazines = Flow.Database.Definitions.Magazines.Count;
            int missions = Flow.Database.Missions.Count;
            System.Collections.Generic.List<string> problems = Flow.DatabaseProblems;
            string text = string.Format(
                "{0} weapons / {1} magazines / {2} missions\nmaps: {3}\nregion: {4}",
                weapons, magazines, missions, string.Join(", ", GameFlow.MapIds),
                Flow.Database.Net.DefaultRegionId);
            if (problems.Count > 0)
            {
                text += "\n\nCONTENT PROBLEMS:\n- " + string.Join("\n- ", problems.ToArray());
            }

            _contentStatus.text = text;
            _contentStatus.color = problems.Count > 0 ? UiBuilder.Danger : UiBuilder.Dim;
        }
    }
}
