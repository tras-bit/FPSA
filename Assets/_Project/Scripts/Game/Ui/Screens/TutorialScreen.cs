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
        private Text _stepList;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image panel = UiBuilder.Panel(Root, "Panel", UiBuilder.Ink, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1100f, 720f));

            _title = UiBuilder.LabelAt(panel.transform, "Title", "", 34, UiBuilder.Paper,
                new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1000f, 44f),
                TextAnchor.MiddleLeft);
            _title.fontStyle = FontStyle.Bold;

            _progress = UiBuilder.LabelAt(panel.transform, "Progress", "", 18, UiBuilder.Accent,
                new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(1000f, 24f),
                TextAnchor.MiddleLeft);

            _body = UiBuilder.LabelAt(panel.transform, "Body", "", 20, UiBuilder.Paper,
                new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(1000f, 180f),
                TextAnchor.UpperLeft);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;

            _binding = UiBuilder.LabelAt(panel.transform, "Binding", "", 22, UiBuilder.Warning,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1000f, 34f),
                TextAnchor.MiddleLeft);

            _stepList = UiBuilder.LabelAt(panel.transform, "Steps", "", 16, UiBuilder.Dim,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(1000f, 180f),
                TextAnchor.UpperLeft);

            UiBuilder.Button(panel.transform, "BACK", () => Back(), new Vector2(-380f, -300f),
                new Vector2(200f, 48f), 17);
            UiBuilder.Button(panel.transform, "PREVIOUS", () => Step(-1), new Vector2(-140f, -300f),
                new Vector2(200f, 48f), 17);
            UiBuilder.Button(panel.transform, "NEXT", () => Step(1), new Vector2(100f, -300f),
                new Vector2(200f, 48f), 17);
            UiBuilder.Button(panel.transform, "START PRACTICE", () => Flow.StartPractice(),
                new Vector2(360f, -300f), new Vector2(260f, 48f), 16);
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

            System.Text.StringBuilder list = new System.Text.StringBuilder();
            for (int i = 0; i < _script.Steps.Count; i++)
            {
                list.Append(i == _index ? "> " : "   ")
                    .Append(i + 1)
                    .Append(". ")
                    .Append(_script.Steps[i].Title)
                    .Append('\n');
            }

            _stepList.text = list.ToString();
        }
    }
}
