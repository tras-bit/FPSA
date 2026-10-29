using Kordon.Core.Data;
using Kordon.Core.Settings;
using Kordon.Game.Input;
using Kordon.Game.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Basic training.  The steps come from Kordon.Core's tutorial script, so the
    /// order and the wording of what the game teaches live with the rules, not in a
    /// canvas.  Actions are shown with the player's *current* binding, which is the
    /// only way a rebindable control scheme can teach anything.
    /// </summary>
    public sealed class TutorialScreen : UiScreenBase
    {
        public override string Name => "tutorial";

        private TutorialScript _script;
        private int _index;
        private Text _title;
        private Text _body;
        private Text _binding;
        private Text _progress;

        private const float PanelWidth = 1100f;
        private const float PanelHeight = 780f;
        private Text _stepsLeft;
        private Text _stepsRight;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image panel = UiBuilder.Panel(Root, "Panel", UiBuilder.Ink, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));

            UiStack stack = new UiStack(panel.transform, "Stack", PanelWidth - 64f, PanelHeight,
                paddingTop: 24f, paddingBottom: 22f, spacing: 8f);

            _title = stack.Text(string.Empty, 32, UiBuilder.Paper, 40f, TextAnchor.MiddleLeft);
            if (_title != null)
            {
                _title.fontStyle = FontStyle.Bold;
            }

            _progress = stack.Text(string.Empty, 17, UiBuilder.Accent, 22f, TextAnchor.MiddleLeft);
            stack.Rule(1f);
            _body = stack.Text(string.Empty, 19, UiBuilder.Paper, 150f, TextAnchor.UpperLeft);
            _binding = stack.Text(string.Empty, 21, UiBuilder.Warning, 30f, TextAnchor.MiddleLeft);

            stack.Section("steps");

            // 14 steps in one 180 px box used to run off the bottom; two columns fit.
            RectTransform stepsRow = stack.RawRow(152f);
            if (stepsRow != null)
            {
                _stepsLeft = UiBuilder.Label(stepsRow, "StepsLeft", string.Empty, 15,
                    UiBuilder.Dim, TextAnchor.UpperLeft);
                _stepsLeft.rectTransform.anchorMin = new Vector2(0f, 0f);
                _stepsLeft.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                _stepsLeft.rectTransform.offsetMin = new Vector2(8f, 0f);
                _stepsLeft.rectTransform.offsetMax = Vector2.zero;

                _stepsRight = UiBuilder.Label(stepsRow, "StepsRight", string.Empty, 15,
                    UiBuilder.Dim, TextAnchor.UpperLeft);
                _stepsRight.rectTransform.anchorMin = new Vector2(0.5f, 0f);
                _stepsRight.rectTransform.anchorMax = new Vector2(1f, 1f);
                _stepsRight.rectTransform.offsetMin = new Vector2(16f, 0f);
                _stepsRight.rectTransform.offsetMax = Vector2.zero;
            }

            stack.Space(2f);
            stack.Pair("PREVIOUS", () => Step(-1), "NEXT", () => Step(1), 46f, 12f, 17);
            stack.Pair("BACK", () => Back(), "START PRACTICE", () => Flow.StartPractice(), 46f,
                12f, 16);
        }

        public override void OnShow()
        {
            _script = Flow.Database.Tutorial;
            _index = Mathf.Clamp(_index, 0, Mathf.Max(0, _script.Steps.Count - 1));
            Refresh();
        }

        private void Step(int delta)
        {
            if (_script == null || _script.Steps.Count == 0)
            {
                return;
            }

            _index = Mathf.Clamp(_index + delta, 0, _script.Steps.Count - 1);
            Refresh();
        }

        private void Refresh()
        {
            if (_script == null || _script.Steps.Count == 0)
            {
                _title.text = "Basic training";
                _body.text = "The tutorial script is missing from the content database.";
                return;
            }

            TutorialStep step = _script.Steps[_index];
            _title.text = step.Title;
            _body.text = step.Body;
            _progress.text = _script.Name + "   -   step " + (_index + 1) + " / " +
                             _script.Steps.Count;

            if (step.RequiredAction == InputAction.None)
            {
                _binding.text = string.Empty;
            }
            else
            {
                KeyBindings bindings = KordonInput.Bindings;
                _binding.text = "press " + bindings.GetKeyName(step.RequiredAction) +
                                (step.RequiredCount > 1 ? "   x" + step.RequiredCount : string.Empty);
            }

            int half = (_script.Steps.Count + 1) / 2;
            _stepsLeft.text = StepColumn(0, half);
            _stepsRight.text = StepColumn(half, _script.Steps.Count);
        }

        /// <summary>One column of the step list, current step marked.</summary>
        private string StepColumn(int from, int to)
        {
            System.Text.StringBuilder list = new System.Text.StringBuilder();
            for (int i = from; i < to && i < _script.Steps.Count; i++)
            {
                list.Append(i == _index ? "> " : "   ")
                    .Append(i + 1)
                    .Append(". ")
                    .Append(_script.Steps[i].Title)
                    .Append('\n');
            }

            return list.ToString();
        }
    }
}
