using System.Collections.Generic;
using Kordon.Core.Rules;
using Kordon.Game.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// End of match.  The result comes from the authoritative controller, never
    /// from the client: winner, why the match ended, every standing, and a rematch
    /// button that returns to the prep screen with the same kit availability.
    /// </summary>
    public sealed class ResultsScreen : UiScreenBase
    {
        public override string Name => "results";

        private Text _headline;
        private Text _reason;
        private RectTransform _standings;
        private MatchResults _results;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.03f, 0.036f, 0.042f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image panel = UiBuilder.Panel(Root, "Panel", UiBuilder.Ink, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1000f, 840f));

            _headline = UiBuilder.LabelAt(panel.transform, "Headline", "MATCH COMPLETE", 42,
                UiBuilder.Accent, new Vector2(0.5f, 1f), new Vector2(0f, -56f),
                new Vector2(920f, 52f), TextAnchor.MiddleCenter);
            _headline.fontStyle = FontStyle.Bold;

            _reason = UiBuilder.LabelAt(panel.transform, "Reason", "", 18, UiBuilder.Dim,
                new Vector2(0.5f, 1f), new Vector2(0f, -98f), new Vector2(920f, 26f),
                TextAnchor.MiddleCenter);

            Image listPanel = UiBuilder.Panel(panel.transform, "Standings", new Color(0f, 0f, 0f, 0.25f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(920f, 540f));
            UiBuilder.Column(listPanel.transform, "Rows", 3f, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 250f), new Vector2(880f, 20f));
            _standings = (RectTransform)listPanel.transform.Find("Rows");

            UiBuilder.Button(panel.transform, "REMATCH", () => Rematch(),
                new Vector2(-230f, -350f), new Vector2(300f, 52f), 18);
            UiBuilder.Button(panel.transform, "MAIN MENU", () => Flow.ShowMainMenu(),
                new Vector2(130f, -350f), new Vector2(300f, 52f), 18);
        }

        public void Fill(MatchResults results, string modeLabel)
        {
            _results = results;
            if (results == null)
            {
                _headline.text = "MATCH OVER";
                _reason.text = string.Empty;
                return;
            }

            _headline.text = results.WinnerName + " WINS";
            _reason.text = string.Format("{0}   |   {1} kills   |   {2}   |   {3}",
                modeLabel, results.WinnerKills, ReasonText(results.Reason),
                FormatDuration(results.DurationSeconds));
            RefreshStandings(results.Standings);
        }

        private void RefreshStandings(List<ScoreboardRow> rows)
        {
            UiBuilder.Clear(_standings);
            if (rows == null)
            {
                return;
            }

            foreach (ScoreboardRow row in rows)
            {
                HorizontalLayoutGroup line = UiBuilder.Row(_standings, "Row", 10f,
                    new Vector2(880f, 26f));
                Color color = row.IsWinner ? UiBuilder.Accent : UiBuilder.Paper;
                if (row.IsBot)
                {
                    color = UiBuilder.Warning;
                }

                UiBuilder.Cell(line.transform, "Rank", "#" + row.Rank, 60f, 18, UiBuilder.Dim);
                string name = row.IsBot ? row.DisplayName + "  [bot]" : row.DisplayName;
                UiBuilder.Cell(line.transform, "Name", name, 380f, 19, color);
                UiBuilder.Cell(line.transform, "Kills", row.Kills.ToString(), 90f, 19, color,
                    TextAnchor.MiddleRight);
                UiBuilder.Cell(line.transform, "Deaths", row.Deaths.ToString(), 90f, 19,
                    UiBuilder.Dim, TextAnchor.MiddleRight);
                UiBuilder.Cell(line.transform, "Damage", Mathf.RoundToInt(row.Damage).ToString(),
                    120f, 18, UiBuilder.Dim, TextAnchor.MiddleRight);
                UiBuilder.Cell(line.transform, "To", row.KillsToObjective + " to go", 120f, 16,
                    UiBuilder.Dim, TextAnchor.MiddleRight);
            }
        }

        private static string ReasonText(MatchEndReason reason)
        {
            switch (reason)
            {
                case MatchEndReason.KillTargetReached: return "kill target reached";
                case MatchEndReason.TimeLimitReached: return "time limit";
                case MatchEndReason.ServerAborted: return "server aborted the match";
                case MatchEndReason.AllPlayersLeft: return "everybody left";
                default: return reason.ToString();
            }
        }

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.RoundToInt(Mathf.Max(0f, seconds));
            return (total / 60) + "m " + (total % 60).ToString("00") + "s";
        }

        private void Rematch()
        {
            _results = null;
            Flow.Rematch();
        }
    }
}
