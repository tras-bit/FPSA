using Kordon.Core.Config;
using Kordon.Game.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Mode select and connection screen.
    ///
    /// Laid out top to bottom by <see cref="UiStack"/>: the previous version anchored
    /// the address label to the top of the panel and the address field to its centre,
    /// which left the PORT and REGION captions floating well below their fields.
    /// </summary>
    public sealed class ConnectScreen : UiScreenBase
    {
        public override string Name => "connect";

        private const float PanelWidth = 920f;
        private const float PanelHeight = 880f;

        private Text _title;
        private Text _subtitle;
        private Text _regionLabel;
        private Text _kitLabel;
        private Text _status;
        private InputField _address;
        private InputField _port;
        private int _regionIndex;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image panel = UiBuilder.Panel(Root, "Panel", UiBuilder.Ink, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));

            UiStack stack = new UiStack(panel.transform, "Stack", PanelWidth - 64f, PanelHeight,
                paddingTop: 28f, paddingBottom: 24f, spacing: 8f);

            _title = stack.Text("FREE-FOR-ALL", 38, UiBuilder.Paper, 46f, TextAnchor.MiddleCenter);
            if (_title != null)
            {
                _title.fontStyle = FontStyle.Bold;
            }

            _subtitle = stack.Text("20 human slots - no teams - first operator to 50 kills wins",
                17, UiBuilder.Dim, 24f, TextAnchor.MiddleCenter);

            stack.Space(4f);
            stack.Pair("FREE-FOR-ALL", () => SetMode(false), "CO-OP OPERATION", () => SetMode(true),
                44f, 12f, 17);

            stack.Space(4f);
            stack.Section("server");
            InputField address = stack.Field("Server address (DNS name or IP)",
                DefaultAddress(), 50f, out _);
            _address = address;
            if (_address != null)
            {
                _address.characterLimit = 64;
            }

            BuildPortAndRegion(stack);

            stack.Space(4f);
            stack.Section("kit");
            _kitLabel = stack.Text(string.Empty, 18, UiBuilder.Paper, 26f);
            stack.Button("CHANGE KIT", () => Flow.OpenPrep(), 44f, 17);

            stack.Space(6f);
            stack.Button("HOST DEDICATED-STYLE LOBBY", () => HostMatch(), 52f, 18);
            stack.Button("JOIN", () => JoinMatch(), 56f, 21);
            stack.Pair("BACK", () => Back(), "PRACTICE OFFLINE INSTEAD",
                () => Flow.StartPractice(), 44f, 12f, 16);

            _status = stack.Text(string.Empty, 16, UiBuilder.Warning, 58f, TextAnchor.MiddleCenter);

            RefreshRegion();
            RefreshKit();
        }

        /// <summary>Port field and region stepper on one line, captions directly above.</summary>
        private void BuildPortAndRegion(UiStack stack)
        {
            RectTransform row = stack.RawRow(72f);
            if (row == null)
            {
                return;
            }

            RectTransform portBlock = UiBuilder.Node(row, "PortBlock");
            portBlock.anchorMin = new Vector2(0f, 0f);
            portBlock.anchorMax = new Vector2(0f, 1f);
            portBlock.pivot = new Vector2(0f, 0.5f);
            portBlock.offsetMin = Vector2.zero;
            portBlock.offsetMax = Vector2.zero;
            portBlock.sizeDelta = new Vector2(200f, 0f);

            UiBuilder.LabelAt(portBlock, "PortCaption", "PORT", 15, UiBuilder.Dim,
                new Vector2(0f, 1f), new Vector2(80f, -10f), new Vector2(160f, 20f),
                TextAnchor.MiddleLeft);

            Image portBackground = UiBuilder.Panel(portBlock, "PortField",
                new Color(0.1f, 0.11f, 0.12f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 24f), new Vector2(200f, 46f));
            Text portText = UiBuilder.Label(portBackground.transform, "Text",
                DefaultPort().ToString(), 20, UiBuilder.Paper, TextAnchor.MiddleLeft);
            portText.rectTransform.anchorMin = Vector2.zero;
            portText.rectTransform.anchorMax = Vector2.one;
            portText.rectTransform.offsetMin = new Vector2(14f, 0f);
            portText.rectTransform.offsetMax = new Vector2(-14f, 0f);
            portText.supportRichText = false;
            _port = portBackground.gameObject.AddComponent<InputField>();
            _port.textComponent = portText;
            _port.text = DefaultPort().ToString();
            _port.characterLimit = 5;
            _port.contentType = InputField.ContentType.IntegerNumber;

            RectTransform regionBlock = UiBuilder.Node(row, "RegionBlock");
            regionBlock.anchorMin = new Vector2(0f, 0f);
            regionBlock.anchorMax = new Vector2(1f, 1f);
            regionBlock.offsetMin = new Vector2(224f, 0f);
            regionBlock.offsetMax = Vector2.zero;

            UiBuilder.LabelAt(regionBlock, "RegionCaption", "REGION  (EU FIRST)", 15, UiBuilder.Dim,
                new Vector2(0f, 1f), new Vector2(0f, -10f), new Vector2(360f, 20f),
                TextAnchor.MiddleLeft);

            RectTransform stepper = UiBuilder.Node(regionBlock, "Stepper");
            stepper.anchorMin = new Vector2(0f, 0f);
            stepper.anchorMax = new Vector2(1f, 0f);
            stepper.pivot = new Vector2(0.5f, 0f);
            stepper.offsetMin = new Vector2(0f, 0f);
            stepper.offsetMax = new Vector2(0f, 0f);
            stepper.sizeDelta = new Vector2(0f, 46f);

            UiBuilder.ButtonIn(stepper, "<", () => CycleRegion(-1), LeftSlot(stepper, 52f), 20);
            UiBuilder.ButtonIn(stepper, ">", () => CycleRegion(1), RightSlot(stepper, 52f), 20);

            Image regionBackground = UiBuilder.Panel(stepper, "RegionValue",
                new Color(0.1f, 0.11f, 0.12f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 23f),
                new Vector2(0f, 46f));
            RectTransform regionRect = (RectTransform)regionBackground.transform;
            regionRect.anchorMin = new Vector2(0f, 0f);
            regionRect.anchorMax = new Vector2(1f, 1f);
            regionRect.offsetMin = new Vector2(64f, 0f);
            regionRect.offsetMax = new Vector2(-64f, 0f);

            _regionLabel = UiBuilder.Label(regionBackground.transform, "Label", string.Empty, 19,
                UiBuilder.Paper, TextAnchor.MiddleCenter);
            _regionLabel.rectTransform.anchorMin = Vector2.zero;
            _regionLabel.rectTransform.anchorMax = Vector2.one;
            _regionLabel.rectTransform.offsetMin = Vector2.zero;
            _regionLabel.rectTransform.offsetMax = Vector2.zero;
        }

        private static RectTransform LeftSlot(Transform parent, float width)
        {
            RectTransform rect = UiBuilder.Node(parent, "Left");
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.sizeDelta = new Vector2(width, 0f);
            return rect;
        }

        private static RectTransform RightSlot(Transform parent, float width)
        {
            RectTransform rect = UiBuilder.Node(parent, "Right");
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.sizeDelta = new Vector2(width, 0f);
            return rect;
        }

        public override void OnShow()
        {
            RefreshRegion();
            RefreshKit();
            SetMode(Flow.CoopRequested);
            if (_status != null)
            {
                _status.text = "Server list is configured by address: press HOST or JOIN.";
            }
        }

        private void SetMode(bool coop)
        {
            Flow.CoopRequested = coop;
            if (_title != null)
            {
                _title.text = coop ? "OPERATION SILENT KILN" : "FREE-FOR-ALL";
                _subtitle.text = coop
                    ? "2-4 operators - briefing, objectives, AI hostiles, evacuation"
                    : "20 human slots - no teams - first operator to 50 kills wins";
            }
        }

        private static string DefaultAddress()
        {
            ServerRegion region = Flow.Database.Net.DefaultRegion;
            return region != null ? region.Address : string.Empty;
        }

        private static int DefaultPort() => Flow.Database.Net.ServerPort;

        private void CycleRegion(int direction)
        {
            int count = Mathf.Max(1, Flow.Database.Net.Regions.Count);
            _regionIndex = ((_regionIndex + direction) % count + count) % count;
            RefreshRegion();
        }

        private void RefreshRegion()
        {
            if (_regionLabel == null)
            {
                return;
            }

            NetConfig net = Flow.Database.Net;
            if (net.Regions.Count == 0)
            {
                _regionLabel.text = net.DefaultRegionId;
                _regionLabel.color = UiBuilder.Warning;
                return;
            }

            ServerRegion region = net.Regions[Mathf.Clamp(_regionIndex, 0, net.Regions.Count - 1)];
            _regionLabel.text = region.DisplayName;
            _regionLabel.color = UiBuilder.Paper;
        }

        private void RefreshKit()
        {
            if (_kitLabel == null)
            {
                return;
            }

            _kitLabel.text = Flow.Loadout.PrimaryWeaponId.ToUpperInvariant() + " + " +
                             Flow.Loadout.SecondaryWeaponId.ToUpperInvariant() + "   " +
                             Flow.Loadout.TotalMagazines() + " magazines   " +
                             Flow.Loadout.ArmorKitId + " plates   (locked at round start)";
        }

        private string CurrentRegionId()
        {
            NetConfig net = Flow.Database.Net;
            return net.Regions.Count == 0
                ? net.DefaultRegionId
                : net.Regions[Mathf.Clamp(_regionIndex, 0, net.Regions.Count - 1)].Id;
        }

        private void HostMatch()
        {
            string address = _address != null ? _address.text.Trim() : DefaultAddress();
            int port = ParsePort();
            if (Flow.CoopRequested)
            {
                Flow.StartCoopMatch(address, port, true, CurrentRegionId());
            }
            else
            {
                Flow.StartNetworkMatch(address, port, true, CurrentRegionId());
            }

            _status.text = "hosting: " + address + ":" + port + " - waiting for players";
        }

        private void JoinMatch()
        {
            string address = _address != null ? _address.text.Trim() : string.Empty;
            if (address.Length == 0)
            {
                _status.text = "enter the server address you were given";
                return;
            }

            int port = ParsePort();
            ClientConnectionRequest request = new ClientConnectionRequest
            {
                Address = address,
                Port = port,
                PlayerName = PlayerProfile.Name,
                RegionId = CurrentRegionId(),
            };
            ConnectionValidation validation = Flow.Database.Net.ValidateClientRequest(request);
            if (!validation.Allowed)
            {
                _status.text = validation.Reason;
                return;
            }

            if (Flow.CoopRequested)
            {
                Flow.StartCoopMatch(request.Address, request.Port, false, CurrentRegionId());
            }
            else
            {
                Flow.StartNetworkMatch(request.Address, request.Port, false, CurrentRegionId());
            }

            _status.text = "connecting to " + address + ":" + port;
        }

        private int ParsePort()
        {
            return _port != null && int.TryParse(_port.text, out int port) && port > 0 && port < 65536
                ? port
                : Flow.Database.Net.ServerPort;
        }
    }
}
