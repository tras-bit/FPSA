using Kordon.Core.Config;
using Kordon.Game.Match;
using UnityEngine;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Match creation and joining.  The address always comes from this form or the
    /// region configuration - never from a hard coded loopback, and a loopback
    /// address is rejected unless the server config explicitly allows development
    /// loopback.  Regions list exactly what net_config.json declares (EU first).
    /// </summary>
    public sealed class ConnectScreen : UiScreenBase
    {
        public override string Name => "connect";

        private InputField _address;
        private InputField _port;
        private Text _regionLabel;
        private Text _status;
        private int _regionIndex;

        protected override void Build()
        {
            Image background = UiBuilder.Panel(Root, "Background",
                new Color(0.035f, 0.042f, 0.048f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(4000f, 4000f));
            background.raycastTarget = true;

            Image panel = UiBuilder.Panel(Root, "Panel", UiBuilder.Ink, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(880f, 700f));

            Text title = UiBuilder.LabelAt(panel.transform, "Title", "FREE-FOR-ALL", 40,
                UiBuilder.Paper, new Vector2(0.5f, 1f), new Vector2(0f, -54f),
                new Vector2(800f, 50f), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            UiBuilder.LabelAt(panel.transform, "Sub",
                "20 human slots - no teams - first operator to 50 kills wins", 18, UiBuilder.Dim,
                new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(800f, 26f),
                TextAnchor.MiddleCenter);

            UiBuilder.LabelAt(panel.transform, "AddressLabel",
                "SERVER ADDRESS  (DNS name or IP, port below)", 16, UiBuilder.Dim,
                new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(700f, 22f),
                TextAnchor.MiddleLeft);
            _address = UiBuilder.Field(panel.transform, "server address", new Vector2(0f, 74f),
                new Vector2(700f, 50f), DefaultAddress());

            UiBuilder.LabelAt(panel.transform, "PortLabel", "PORT", 16, UiBuilder.Dim,
                new Vector2(0.5f, 0f), new Vector2(-260f, 210f), new Vector2(160f, 22f),
                TextAnchor.MiddleLeft);
            _port = UiBuilder.Field(panel.transform, "port", new Vector2(-260f, 176f),
                new Vector2(160f, 46f), DefaultPort().ToString());

            UiBuilder.LabelAt(panel.transform, "RegionLabel", "REGION", 16, UiBuilder.Dim,
                new Vector2(0.5f, 0f), new Vector2(90f, 210f), new Vector2(260f, 22f),
                TextAnchor.MiddleLeft);
            _regionLabel = UiBuilder.LabelAt(panel.transform, "Region", "", 20, UiBuilder.Paper,
                new Vector2(0.5f, 0f), new Vector2(150f, 176f), new Vector2(300f, 46f),
                TextAnchor.MiddleCenter);
            UiBuilder.Button(panel.transform, "<", () => CycleRegion(-1),
                new Vector2(20f, 176f), new Vector2(56f, 46f), 20);
            UiBuilder.Button(panel.transform, ">", () => CycleRegion(1),
                new Vector2(330f, 176f), new Vector2(56f, 46f), 20);

            UiBuilder.LabelAt(panel.transform, "KitLabel", "KIT", 16, UiBuilder.Dim,
                new Vector2(0.5f, 0f), new Vector2(-300f, 108f), new Vector2(200f, 22f),
                TextAnchor.MiddleLeft);
            Text kit = UiBuilder.LabelAt(panel.transform, "Kit", "", 18, UiBuilder.Paper,
                new Vector2(0.5f, 0f), new Vector2(70f, 108f), new Vector2(620f, 24f),
                TextAnchor.MiddleLeft);
            kit.name = "KitSummary";
            UiBuilder.Button(panel.transform, "CHANGE KIT", () => Flow.OpenPrep(),
                new Vector2(-230f, 30f), new Vector2(240f, 48f), 18);

            UiBuilder.Button(panel.transform, "HOST DEDICATED-STYLE LOBBY",
                () => HostMatch(), new Vector2(90f, -62f), new Vector2(420f, 56f), 18);
            UiBuilder.Button(panel.transform, "JOIN", () => JoinMatch(),
                new Vector2(90f, -128f), new Vector2(420f, 56f), 20);
            UiBuilder.Button(panel.transform, "BACK", () => Back(),
                new Vector2(-230f, -128f), new Vector2(200f, 56f), 18);

            UiBuilder.Button(panel.transform, "PRACTICE OFFLINE INSTEAD", () => Flow.StartPractice(),
                new Vector2(0f, -210f), new Vector2(420f, 46f), 17);

            _status = UiBuilder.LabelAt(panel.transform, "Status", "", 17, UiBuilder.Warning,
                new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(800f, 60f),
                TextAnchor.MiddleCenter);

            RefreshRegion();
            RefreshKit();
        }

        public override void OnShow()
        {
            RefreshRegion();
            RefreshKit();
            _status.text = "Server list is configured by address: press HOST or JOIN.";
        }

        private void CycleRegion(int direction)
        {
            NetConfig net = Flow.Database.Net;
            int count = Mathf.Max(1, net.Regions.Count);
            _regionIndex = ((_regionIndex + direction) % count + count) % count;
            RefreshRegion();
        }

        private void RefreshRegion()
        {
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
            Text kit = (Text)Root.Find("Panel/KitSummary");
            if (kit == null)
            {
                return;
            }

            kit.text = Flow.Loadout.PrimaryWeaponId.ToUpperInvariant() + " + " +
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

        private string DefaultAddress()
        {
            NetConfig net = Flow.Database.Net;
            ServerRegion region = net.DefaultRegion;
            return region != null ? region.Address : string.Empty;
        }

        private int DefaultPort() => Flow.Database.Net.ServerPort;

        private void HostMatch()
        {
            string address = _address.text;
            int port = ParsePort();
            Flow.StartNetworkMatch(address, port, true, CurrentRegionId());
            _status.text = "hosting: " + address + ":" + port + " - waiting for players";
        }

        private void JoinMatch()
        {
            string address = _address.text;
            if (string.IsNullOrEmpty(address) || address.Trim().Length == 0)
            {
                _status.text = "enter the server address you were given";
                return;
            }

            int port = ParsePort();
            ClientConnectionRequest request = new ClientConnectionRequest
            {
                Address = address.Trim(),
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

            Flow.StartNetworkMatch(request.Address, request.Port, false, CurrentRegionId());
            _status.text = "connecting to " + address.Trim() + ":" + port;
        }

        private int ParsePort()
        {
            return int.TryParse(_port.text, out int port) && port > 0 && port < 65536
                ? port
                : Flow.Database.Net.ServerPort;
        }
    }
}
