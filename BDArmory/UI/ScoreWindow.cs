using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Settings;
using BDArmory.Utils;
using BDArmory.VesselSpawning;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ScoreWindow : MonoBehaviour
    {
        #region Fields
        public static ScoreWindow Instance;
        public bool _ready = false;
        public static string scoreWeightsURL = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/BDArmory/PluginData/score_weights.cfg"));

        int _buttonSize = 24;
        static int _guiCheckIndexScores = -1;
        Vector2 windowSize = new Vector2(200, 100);
        bool resizingWindow = false;
        bool autoResizingWindow = true;
        Vector2 scoreScrollPos = default;
        bool showTeamScores = false;
        public enum Mode { Tournament, ContinuousSpawn }
        static Mode mode = Mode.Tournament;
        public static void SetMode(Mode scoreMode) => Instance.SetMode_(scoreMode);
        void SetMode_(Mode scoreMode)
        {
            mode = scoreMode;
            LoadWeights();
            ResetWindowSize(true);
        }
        #endregion

        #region Styles
        bool stylesConfigured = false;
        GUIStyle leftLabel;
        GUIStyle rightLabel;
        #endregion

        private void Awake()
        {
            if (Instance)
                Destroy(this);
            Instance = this;
        }

        private void Start()
        {
            _ready = false;
            StartCoroutine(WaitForBdaSettings());
            SetMode(Mode.Tournament);
            showTeamScores = BDArmorySettings.VESSEL_SPAWN_NUMBER_OF_TEAMS != 0;
        }

        private IEnumerator WaitForBdaSettings()
        {
            yield return new WaitUntil(() => BDArmorySetup.Instance is not null);

            if (_guiCheckIndexScores < 0) _guiCheckIndexScores = GUIUtils.RegisterGUIRect(new Rect());
            if (_guiCheckIndexWeights < 0) _guiCheckIndexWeights = GUIUtils.RegisterGUIRect(new Rect());
            _ready = true;
            AdjustWindowRect(BDArmorySetup.WindowRectScores.size, true);
        }

        void ConfigureStyles()
        {
            stylesConfigured = true;
            leftLabel = new GUIStyle
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                fontSize = BDArmorySettings.SCORES_FONT_SIZE
            };
            leftLabel.normal.textColor = Color.white;
            rightLabel = new GUIStyle(leftLabel)
            {
                alignment = TextAnchor.MiddleRight,
                wordWrap = false
            };
        }

        void AdjustFontSize(bool up)
        {
            if (up) ++BDArmorySettings.SCORES_FONT_SIZE;
            else --BDArmorySettings.SCORES_FONT_SIZE;
            if (up)
            {
                leftLabel.fontSize = BDArmorySettings.SCORES_FONT_SIZE;
                rightLabel.fontSize = BDArmorySettings.SCORES_FONT_SIZE;
            }
            else
            {
                leftLabel.fontSize = BDArmorySettings.SCORES_FONT_SIZE;
                rightLabel.fontSize = BDArmorySettings.SCORES_FONT_SIZE;
            }
            ResetWindowSize();
        }

        private void OnGUI()
        {
            if (!(_ready && BDArmorySettings.SHOW_SCORE_WINDOW && (BDArmorySetup.GAME_UI_ENABLED || BDArmorySettings.SCORES_PERSIST_UI) && HighLogic.LoadedSceneIsFlight))
                return;

            if (!stylesConfigured) ConfigureStyles();

            if (resizingWindow && Event.current.type == EventType.MouseUp) { resizingWindow = false; }
            AdjustWindowRect(windowSize);
            BDArmorySetup.SetGUIOpacity();
            var guiMatrix = GUI.matrix;
            if (BDArmorySettings.UI_SCALE_ACTUAL != 1) GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, BDArmorySetup.WindowRectScores.position);
            BDArmorySetup.WindowRectScores = GUILayout.Window(
                GUIUtility.GetControlID(FocusType.Passive),
                BDArmorySetup.WindowRectScores,
                WindowScores,
                StringUtils.Localize("#LOC_BDArmory_BDAScores_Title"),//"BDA Scores"
                BDArmorySetup.BDGuiSkin.window
            );
            if (weightsVisible)
            {
                if (BDArmorySettings.UI_SCALE_ACTUAL != 1) { GUI.matrix = guiMatrix; GUIUtility.ScaleAroundPivot(BDArmorySettings.UI_SCALE_ACTUAL * Vector2.one, weightsWindowRect.position); }
                weightsWindowRect = GUILayout.Window(
                    GUIUtility.GetControlID(FocusType.Passive),
                    weightsWindowRect,
                    WindowWeights,
                    StringUtils.Localize("#LOC_BDArmory_BDAScores_Weights"), // "Score Weights"
                    BDArmorySetup.BDGuiSkin.window
                );
            }
            BDArmorySetup.SetGUIOpacity(false);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectScores, _guiCheckIndexScores);
        }

        #region Scores
        private void AdjustWindowRect(Vector2 size, bool force = false)
        {
            if (!autoResizingWindow && resizingWindow || force)
            {
                size.x = Mathf.Clamp(size.x, 150, Screen.width - BDArmorySetup.WindowRectScores.x);
                size.y = Mathf.Clamp(size.y, 70, Screen.height - BDArmorySetup.WindowRectScores.y); // The ScrollView won't let us go smaller than this.
                BDArmorySetup.WindowRectScores.size = size;
            }
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectScores, windowSize.y);
            windowSize = BDArmorySetup.WindowRectScores.size;
        }

        private void WindowScores(int id)
        {
            if (GUI.Button(new Rect(0, 0, _buttonSize, _buttonSize), "UI", BDArmorySettings.SCORES_PERSIST_UI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)) { BDArmorySettings.SCORES_PERSIST_UI = !BDArmorySettings.SCORES_PERSIST_UI; }
            if (GUI.Button(new Rect(_buttonSize, 0, _buttonSize, _buttonSize), "-", BDArmorySetup.BDGuiSkin.button)) AdjustFontSize(false);
            if (GUI.Button(new Rect(2 * _buttonSize, 0, _buttonSize, _buttonSize), "+", BDArmorySetup.BDGuiSkin.button)) AdjustFontSize(true);
            GUI.DragWindow(new Rect(3 * _buttonSize, 0, windowSize.x - _buttonSize * 6, _buttonSize));
            if (GUI.Button(new Rect(windowSize.x - 3 * _buttonSize, 0, _buttonSize, _buttonSize), "T", showTeamScores ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)) { showTeamScores = !showTeamScores; ResetWindowSize(); }
            if (GUI.Button(new Rect(windowSize.x - 2 * _buttonSize, 0, _buttonSize, _buttonSize), "W", weightsVisible ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)) SetWeightsVisible(!weightsVisible);
            if (GUI.Button(new Rect(windowSize.x - _buttonSize, 0, _buttonSize, _buttonSize), " X", BDArmorySetup.CloseButtonStyle)) SetVisible(false);

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(autoResizingWindow));
            switch (mode)
            {
                case Mode.Tournament:
                    {
                        var progress = BDATournament.Instance.GetTournamentProgress();
                        GUILayout.Label($"{StringUtils.Localize("#LOC_BDArmory_BDAScores_Round")} {progress.Item1} / {progress.Item2}, {StringUtils.Localize("#LOC_BDArmory_BDAScores_Heat")} {progress.Item3} / {progress.Item4}", leftLabel);
                        if (!autoResizingWindow) scoreScrollPos = GUILayout.BeginScrollView(scoreScrollPos);
                        int rank = 0;
                        using var scoreField = showTeamScores ? BDATournament.Instance.GetRankedTeamScores.GetEnumerator() : BDATournament.Instance.GetRankedScores.GetEnumerator();
                        while (scoreField.MoveNext())
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"{++rank,3:D}", leftLabel, GUILayout.Width(BDArmorySettings.SCORES_FONT_SIZE * 2));
                            GUILayout.Label(scoreField.Current.Key, leftLabel);
                            GUILayout.Label($"{scoreField.Current.Value,7:F3}", rightLabel);
                            GUILayout.EndHorizontal();
                        }
                        if (!autoResizingWindow) GUILayout.EndScrollView();
                    }
                    break;
                case Mode.ContinuousSpawn:
                    {
                        // Show rank, vessel name, lives, score
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_Settings_ContinuousSpawning"), leftLabel, GUILayout.ExpandWidth(true));
                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_BDAScores_Lives"), rightLabel, GUILayout.Width(50));
                        GUILayout.Label(StringUtils.Localize("#LOC_BDArmory_BDAScores_Score"), rightLabel, GUILayout.Width(70));
                        GUILayout.EndHorizontal();
                        if (!autoResizingWindow) scoreScrollPos = GUILayout.BeginScrollView(scoreScrollPos);
                        int rank = 0;
                        using var scoreField = ContinuousSpawning.Instance.Scores.GetEnumerator();
                        while (scoreField.MoveNext())
                        {
                            var (name, deaths, score) = scoreField.Current;
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"{++rank,3:D}", leftLabel, GUILayout.Width(BDArmorySettings.SCORES_FONT_SIZE * 2));
                            GUILayout.Label(name, leftLabel, GUILayout.ExpandWidth(true));
                            GUILayout.Label(BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL == 0 ? StringUtils.Localize("#LOC_BDArmory_BDAScores_Unlimited") : $"{BDArmorySettings.VESSEL_SPAWN_LIVES_PER_VESSEL - deaths}", rightLabel, GUILayout.Width(50));
                            GUILayout.Label($"{score,7:F2}", rightLabel, GUILayout.Width(70));
                            GUILayout.EndHorizontal();
                        }
                        if (!autoResizingWindow) GUILayout.EndScrollView();
                    }
                    break;
            }
            GUILayout.EndVertical();

            #region Resizing
            var resizeRect = new Rect(windowSize.x - 16, windowSize.y - 16, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1) // Right click - reset to auto-resizing the height.
                {
                    resizingWindow = false;
                    ResetWindowSize(true);
                }
                else
                {
                    autoResizingWindow = false;
                    resizingWindow = true;
                }
            }
            if (resizingWindow && Event.current.type == EventType.Repaint)
            { windowSize += Mouse.delta / BDArmorySettings.UI_SCALE_ACTUAL; }
            #endregion
            GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectScores);
        }

        public void SetVisible(bool visible)
        {
            BDArmorySettings.SHOW_SCORE_WINDOW = visible;
            GUIUtils.SetGUIRectVisible(_guiCheckIndexScores, visible);
        }
        public bool IsVisible => BDArmorySettings.SHOW_SCORE_WINDOW;

        /// <summary>
        /// Reset the window size so that the height is tight.
        /// </summary>
        public void ResetWindowSize(bool force = false)
        {
            if (force) autoResizingWindow = true;
            if (autoResizingWindow)
            {
                BDArmorySetup.WindowRectScores.height = 50; // Don't reset completely to 0 as that then covers half the title.
            }
        }
        #endregion

        #region Weights
        internal static int _guiCheckIndexWeights = -1;
        bool weightsVisible = false;
        Rect weightsWindowRect = new(0, 0, 300, 600);
        Vector2 weightsScrollPos = default;
        Dictionary<string, float> weights; // Reference to the set of weights we're using (Tournament or ContinuousSpawning).
        Dictionary<string, NumericInputField> scoreWeightFields; // The numeric input fields.
        void LoadWeights()
        {
            switch (mode)
            {
                case Mode.Tournament:
                    TournamentScores.LoadWeights();
                    weights = TournamentScores.weights;
                    weightsWindowRect.height = 600;
                    break;
                case Mode.ContinuousSpawn:
                    ContinuousSpawning.LoadWeights();
                    weights = ContinuousSpawning.weights;
                    weightsWindowRect.height = 450;
                    break;
                default:
                    weights = null;
                    scoreWeightFields = null;
                    return;
            }
            scoreWeightFields = weights.ToDictionary(kvp => kvp.Key, kvp => gameObject.AddComponent<NumericInputField>().Initialise(0, kvp.Value));
        }
        void SaveWeights()
        {
            switch (mode)
            {
                case Mode.Tournament:
                    TournamentScores.SaveWeights();
                    break;
                case Mode.ContinuousSpawn:
                    ContinuousSpawning.SaveWeights();
                    break;
            }
            RecomputeScores();
        }
        void RecomputeScores()
        {
            switch (mode)
            {
                case Mode.Tournament:
                    BDATournament.Instance.RecomputeScores();
                    break;
                case Mode.ContinuousSpawn:
                    ContinuousSpawning.Instance.RecomputeScores();
                    break;
            }
        }

        void SetWeightsVisible(bool visible)
        {
            if (weights == null) return;
            weightsVisible = visible;
            GUIUtils.SetGUIRectVisible(_guiCheckIndexWeights, visible);
            if (visible)
            {
                weightsWindowRect.y = BDArmorySetup.WindowRectScores.y;
                if (BDArmorySetup.WindowRectScores.x + BDArmorySettings.UI_SCALE_ACTUAL * (windowSize.x + weightsWindowRect.width) <= Screen.width)
                    weightsWindowRect.x = BDArmorySetup.WindowRectScores.x + BDArmorySettings.UI_SCALE_ACTUAL * windowSize.x;
                else
                    weightsWindowRect.x = BDArmorySetup.WindowRectScores.x - BDArmorySettings.UI_SCALE_ACTUAL * weightsWindowRect.width;
            }
            else
            {
                foreach (var weight in scoreWeightFields)
                {
                    weight.Value.tryParseValueNow();
                    weights[weight.Key] = (float)weight.Value.currentValue;
                }
                SaveWeights();
            }
        }
        void WindowWeights(int id)
        {
            GUI.DragWindow(new Rect(0, 0, weightsWindowRect.width - _buttonSize, _buttonSize));
            if (GUI.Button(new Rect(weightsWindowRect.width - _buttonSize, 0, _buttonSize, _buttonSize), " X", BDArmorySetup.CloseButtonStyle)) SetWeightsVisible(false);
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            weightsScrollPos = GUILayout.BeginScrollView(weightsScrollPos, GUI.skin.box);
            foreach (var weight in scoreWeightFields)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(weight.Key);
                weight.Value.tryParseValue(GUILayout.TextField(weight.Value.possibleValue, 10, weight.Value.style, GUILayout.Width(80)));
                if (weights[weight.Key] != (float)weight.Value.currentValue)
                {
                    weights[weight.Key] = (float)weight.Value.currentValue;
                    RecomputeScores();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUIUtils.RepositionWindow(ref weightsWindowRect);
            GUIUtils.UpdateGUIRect(weightsWindowRect, _guiCheckIndexWeights);
            GUIUtils.UseMouseEventInRect(weightsWindowRect);
        }
        #endregion
    }
}
