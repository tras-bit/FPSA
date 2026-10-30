using System;
using System.Collections.Generic;
using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Rules;
using Kordon.Game.Input;
using Kordon.Game.Maps;
using Kordon.Core.Settings;
using Kordon.Game.Settings;
using Kordon.Game.Ui;
using UnityEngine;

namespace Kordon.Game.Match
{
    /// <summary>
    /// The application: owns the content database, settings, bindings and the
    /// current match, and is the only object that moves the player between menu,
    /// prep, match and results.  Screens never talk to each other directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }

        public GameDatabase Database { get; private set; }
        public GameSettings Settings { get; private set; }
        public KeyBindings Bindings { get; private set; }
        public UiDirector Director { get; private set; }
        public LoadoutSelection Loadout { get; set; }
        public PracticeMatch Practice { get; private set; }

        /// <summary>The demolition session, when the player started that mode.</summary>
        public DemolitionMatch Demolition { get; private set; }

        /// <summary>Set by the network stage once a client/server can be created.</summary>
        public Func<IMatchConnector> ConnectorFactory;

        /// <summary>Set by the connect screen: the next session is the co-op operation.</summary>
        public bool CoopRequested;
        public IMatchConnector Connector { get; private set; }
        public string StatusLine { get; private set; } = string.Empty;

        public event Action StatusChanged;

        public static readonly string[] MapIds = { "ffa_zavod7", "coop_silent_kiln" };
        public static readonly string[] MapTitles =
        {
            "ZAVOD-7  (Free-for-All)",
            "OPERATION SILENT KILN  (Co-op)",
        };

        private Transform _worldRoot;
        private bool _bootstrapped;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Bootstrap();
        }

        private void Bootstrap()
        {
            if (_bootstrapped)
            {
                return;
            }

            _bootstrapped = true;
            Settings = SettingsStore.Load();
            SettingsStore.Apply(Settings);
            Bindings = KordonInput.LoadBindings();
            KordonInput.Rebind(Bindings);

            Database = GameDatabase.Load(ContentPaths.DataRoot);
            Loadout = PracticeMatch.DefaultLoadout(Database.Definitions);

            GameObject directorObject = new GameObject("UiDirector");
            directorObject.transform.SetParent(transform, false);
            Director = directorObject.AddComponent<UiDirector>();
            UiBuilder.EnsureEventSystem();

            BuildScreens();
            Director.Push("main");
        }

        private void BuildScreens()
        {
            Create<MainMenuScreen>("main");
            Create<ConnectScreen>("connect");
            Create<PrepScreen>("prep");
            Create<SettingsScreen>("settings");
            Create<ResultsScreen>("results");
            Create<PauseScreen>("pause");
            Create<TutorialScreen>("tutorial");
        }

        private void Create<T>(string name) where T : UiScreenBase
        {
            GameObject go = new GameObject("Screen_" + name);
            go.transform.SetParent(transform, false);
            T screen = go.AddComponent<T>();
            screen.Init(this);
            Director.Register(screen);
        }

        private void Update()
        {
            if (Director == null || !Director.InMatch)
            {
                return;
            }

            if (!KordonInput.Pressed(InputAction.Pause))
            {
                return;
            }

            // Esc opens the pause overlay; Esc again closes it (PauseScreen handles that).
            if (!AnyMenuOpen)
            {
                Director.Push("pause");
            }
        }

        private bool AnyMenuOpen
        {
            get
            {
                PauseScreen pause = Director.Get<PauseScreen>("pause");
                return pause != null && pause.Root.gameObject.activeSelf;
            }
        }

        // ------------------------------------------------------------- lifecycle

        public void ShowMainMenu()
        {
            LeaveWorld();
            Director.Clear();
            Director.LeaveMatch();
            Director.Push("main");
        }

        public void OpenPrep()
        {
            Director.Push("prep");
        }

        public void OpenSettings()
        {
            Director.Push("settings");
        }

        public void OpenTutorial()
        {
            Director.Push("tutorial");
        }

        public void OpenConnect()
        {
            Director.Push("connect");
        }

        public void Back()
        {
            Director.Pop();
        }

        public void SetStatus(string status)
        {
            StatusLine = status ?? string.Empty;
            StatusChanged?.Invoke();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // --------------------------------------------------------------- matches

        /// <summary>Offline practice on the FFA map, with the real magazine and recoil rules.</summary>
        public void StartPractice()
        {
            LeaveWorld();
            Director.EnterMatch();
            _worldRoot = new GameObject("PracticeWorld").transform;
            Practice = _worldRoot.gameObject.AddComponent<PracticeMatch>();
            Practice.MatchFinished += OnPracticeFinished;
            Practice.Begin(Database, "ffa_zavod7", Loadout, _worldRoot);
            Practice.Hud.gameObject.SetActive(true);
        }

        /// <summary>
        /// Demolition against labelled bots on Zavod-7. Offline, so it is the place to
        /// learn the shop, the sites and the plant/defuse timings; a networked demolition
        /// match uses the same controller and would only replace the loop's owner.
        /// </summary>
        public void StartDemolition()
        {
            LeaveWorld();
            Director.EnterMatch();
            _worldRoot = new GameObject("DemolitionWorld").transform;
            Demolition = _worldRoot.gameObject.AddComponent<DemolitionMatch>();
            Demolition.MatchFinished += OnDemolitionFinished;
            Demolition.Begin(Database, "ffa_zavod7", Loadout, _worldRoot);
            Demolition.Hud.gameObject.SetActive(true);
            BuyScreen = _worldRoot.gameObject.AddComponent<BuyScreen>();
            BuyScreen.Bind(Demolition);
        }

        /// <summary>The shop overlay, alive while a demolition session is.</summary>
        public BuyScreen BuyScreen { get; private set; }

        private void OnDemolitionFinished()
        {
            ShowResults(Demolition.BuildResults(), "DEMOLITION");
        }

        private void OnPracticeFinished()
        {
            MatchResults results = Practice.Match.BuildResults();
            ShowResults(results, "PRACTICE");
        }

        public void StartCoopMatch(string address, int port, bool host, string regionId)
        {
            CoopRequested = true;
            StartNetworkMatchInternal(address, port, host, regionId, true);
        }

        public void StartNetworkMatch(string address, int port, bool host, string regionId)
        {
            CoopRequested = false;
            StartNetworkMatchInternal(address, port, host, regionId, false);
        }

        private void StartNetworkMatchInternal(string address, int port, bool host,
            string regionId, bool coop)
        {
            if (ConnectorFactory == null)
            {
                SetStatus("Network transport unavailable in this build.");
                return;
            }

            LeaveWorld();
            Connector = ConnectorFactory();
            Connector.Status += SetStatus;

            Connector.MatchEnded += results => ShowResults(results, Connector.ModeLabel);
            if (Connector.ModeLabel == "CO-OP" != coop)
            {
                SetStatus("this build cannot start " + (coop ? "co-op" : "free-for-all") +
                          " from the current connector");
                return;
            }

            Connector.Begin(address, port, host, regionId, Loadout, PlayerProfile.Name);
            SetStatus(host ? "hosting on " + address + ":" + port : "connecting to " + address + ":" + port);
        }

        public void ShowResults(MatchResults results, string modeLabel)
        {
            ResultsScreen screen = Director.Get<ResultsScreen>("results");
            if (screen == null)
            {
                return;
            }

            screen.Fill(results, modeLabel);
            Director.Push("results");
        }

        public void Rematch()
        {
            LeaveWorld();
            Director.Clear();
            Director.Push("prep");
        }

        public void LeaveWorld()
        {
            if (Connector != null)
            {
                Connector.Shutdown();
                Connector = null;
            }

            if (Practice != null)
            {
                Practice.Stop();
                Practice = null;
            }

            if (Demolition != null)
            {
                Demolition.Stop();
                Demolition = null;
            }

            BuyScreen = null;

            if (_worldRoot != null)
            {
                Destroy(_worldRoot.gameObject);
                _worldRoot = null;
            }
        }

        /// <summary>Called by the pause screen: back to the safety of the menu.</summary>
        public void LeaveMatch()
        {
            LeaveWorld();
            Director.Clear();
            Director.LeaveMatch();
            Director.Push("main");
        }

        public List<string> DatabaseProblems => Database != null
            ? Database.Validate()
            : new List<string> { "database not loaded" };
    }

    /// <summary>
    /// The seam the network stage implements.  Screens and GameFlow only ever see
    /// this, so the transport can be swapped (dedicated server, listen server,
    /// offline) without touching the UI.
    /// </summary>
    public interface IMatchConnector
    {
        /// <summary>"ONLINE" for networked FFA, "CO-OP" for the operation.</summary>
        string ModeLabel { get; }

        event Action<string> Status;
        event Action<MatchResults> MatchEnded;

        void Begin(string address, int port, bool host, string regionId,
            LoadoutSelection loadout, string displayName);
        void Shutdown();
    }
}
