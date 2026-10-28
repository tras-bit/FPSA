using Kordon.Game.Input;
using Kordon.Game.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// In-match pause.  In a networked match the server is authoritative and keeps
    /// running, so "pause" here means a local menu overlay that stops local input;
    /// the screen says so instead of pretending the world stops.
    /// </summary>
    public sealed class PauseScreen : UiScreenBase
    {
        public override string Name => "pause";

        protected override int SortOrder => 40;

        private Text _modeNote;

        protected override void Build()
        {
            Image shade = UiBuilder.Panel(Root, "Shade", new Color(0f, 0f, 0f, 0.72f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000f, 4000f));
            shade.raycastTarget = true;

            Image panel = UiBuilder.Panel(Root, "Panel", UiBuilder.Ink, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560f, 560f));
            Text title = UiBuilder.LabelAt(panel.transform, "Title", "PAUSED", 40, UiBuilder.Paper,
                new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(500f, 48f),
                TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;

            _modeNote = UiBuilder.LabelAt(panel.transform, "ModeNote", "", 16, UiBuilder.Dim,
                new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(500f, 44f),
                TextAnchor.MiddleCenter);

            UiBuilder.Button(panel.transform, "RESUME", () => Back(), new Vector2(0f, 60f),
                new Vector2(440f, 56f), 20);
            UiBuilder.Button(panel.transform, "SETTINGS & CONTROLS", () => Flow.OpenSettings(),
                new Vector2(0f, -10f), new Vector2(440f, 50f), 18);
            UiBuilder.Button(panel.transform, "LEAVE MATCH", () => Flow.LeaveMatch(),
                new Vector2(0f, -80f), new Vector2(440f, 50f), 18);
        }

        public override void OnShow()
        {
            _modeNote.text = Flow.Connector != null
                ? "networked match: the server keeps running while this menu is open"
                : "practice: local simulation keeps running";
            // Offline practice genuinely stops; a networked match keeps running on
            // the server, so the client must not freeze its own simulation either.
            Time.timeScale = Flow.Connector == null ? 0f : 1f;
        }

        public override void OnHide()
        {
            Time.timeScale = 1f;
        }

        private void Update()
        {
            if (Built && KordonInput.Pressed(InputAction.Pause))
            {
                Back();
            }
        }
    }
}
