using System.IO;
using FinitePrizePool.EditorTools;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FinitePrizePool.CanvasUI.EditorTools
{
    /// <summary>
    /// Builds the Canvas game screen once, as assets you then edit in the Editor like any other UI:
    ///   CanvasUI/Prefabs/ChoiceButton.prefab   one player, mode or bet button
    ///   CanvasUI/Prefabs/GameCanvas.prefab     the whole screen, with a button for each player, mode and bet in the config
    ///   Scenes/Game.unity                      camera, GameRoot (the engine), GameCanvas, EventSystem
    /// Run it again only to start over: it replaces all three.
    /// </summary>
    public static class CanvasUiBuilder
    {
        public const string PrefabFolder = "Assets/FinitePrizePool/CanvasUI/Prefabs";
        public const string ButtonPrefabPath = PrefabFolder + "/ChoiceButton.prefab";
        public const string CanvasPrefabPath = PrefabFolder + "/GameCanvas.prefab";

        private static readonly Color Background = new Color(0.07f, 0.08f, 0.11f);
        private static readonly Color Panel = new Color(0.13f, 0.15f, 0.21f);
        private static readonly Color TextColor = new Color(0.93f, 0.94f, 0.97f);
        private static readonly Color Muted = new Color(0.62f, 0.66f, 0.74f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.32f);
        private static readonly Color Alert = new Color(1f, 0.55f, 0.45f);
        private static readonly Color DebugBack = new Color(0.25f, 0.08f, 0.08f, 0.92f);

        [MenuItem("Tools/Finite Prize Pool/Build Canvas UI")]
        public static void BuildFromMenu()
        {
            if (!GameSceneCreator.ConfirmReplace()) return;
            Build();
        }

        /// <summary>No dialogs, so the Unity CLI or -executeMethod can run it. Replaces the prefabs and the scene.</summary>
        public static void Build()
        {
            if (!EnsureTextMeshProResources()) return;

            Directory.CreateDirectory(PrefabFolder);
            var buttonPrefab = BuildButtonPrefab();
            var canvasPrefab = BuildCanvasPrefab(buttonPrefab);
            BuildScene(canvasPrefab);
            Debug.Log("[FinitePrizePool] Built the Canvas UI (" + CanvasPrefabPath + ") and " + GameSceneCreator.ScenePath + ". Press Play.");
        }

        // ---- TextMeshPro --------------------------------------------------------------------------------------

        /// <summary>TMP texts need TMP Essentials (default font, shaders, settings) in the project; imports them if missing.</summary>
        private static bool EnsureTextMeshProResources()
        {
            // Resources.Load, not TMP_Settings.instance: the latter opens the importer window when they are missing.
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;
            TMP_PackageResourceImporter.ImportResources(importEssentials: true, importExamples: false, interactive: false);
            AssetDatabase.Refresh();
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;
            Debug.LogWarning("[FinitePrizePool] Importing TMP Essentials. Run Tools > Finite Prize Pool > Build Canvas UI again once the import finishes.");
            return false;
        }

        // ---- prefabs ------------------------------------------------------------------------------------------

        private static Button BuildButtonPrefab()
        {
            var root = new GameObject("ChoiceButton", typeof(RectTransform));
            var image = root.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = new Color(0.22f, 0.25f, 0.33f);
            var button = root.AddComponent<Button>();
            button.colors = Tints();
            var layout = root.AddComponent<LayoutElement>();
            layout.minHeight = 44f;
            layout.preferredHeight = 48f;
            layout.flexibleWidth = 1f;
            ((RectTransform)root.transform).sizeDelta = new Vector2(145f, 44f);

            var label = Text("Label", root.transform, "Choice", 21f, FontStyles.Normal, TextColor, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.raycastTarget = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ButtonPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Button>();
        }

        private static GameObject BuildCanvasPrefab(Button buttonPrefab)
        {
            var config = ConfigAsset.Load();

            var root = new GameObject("GameCanvas", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f; // sized by height: the column is fixed-width and centred
            root.AddComponent<GraphicRaycaster>();
            var screen = root.AddComponent<CanvasSpinScreen>();

            var background = Box("Background", root.transform, Background);
            Stretch(background.rectTransform);
            background.raycastTarget = false;

            // The game column: centred, 760 wide, full height.
            var column = Rect("Column", root.transform);
            column.anchorMin = new Vector2(0.5f, 0f);
            column.anchorMax = new Vector2(0.5f, 1f);
            column.sizeDelta = new Vector2(760f, -40f);
            var stack = column.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = 6f;
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            Sized(Text("Title", column, "Finite Prize Pool", 42f, FontStyles.Bold, TextColor, TextAlignmentOptions.Left), 58f);

            Caption(column, "Player");
            var players = Row("PlayerButtons", column);
            foreach (var player in config.TestPlayers) Choice(buttonPrefab, players, player.Name);
            var balanceRow = Row("BalanceRow", column, 44f);
            var balance = Text("Balance", balanceRow, "Balance   $0.00", 30f, FontStyles.Bold, Gold, TextAlignmentOptions.MidlineLeft);
            var spinCount = Text("SpinCount", balanceRow, "0 spins", 22f, FontStyles.Bold, Muted, TextAlignmentOptions.MidlineRight);

            Caption(column, "Mode");
            var modes = Row("ModeButtons", column);
            foreach (var mode in config.ModeNames) Choice(buttonPrefab, modes, mode);

            Caption(column, "Bet");
            var bets = Rect("BetButtons", column);
            var grid = bets.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(146f, 44f);
            grid.spacing = new Vector2(7.5f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            foreach (var bet in config.BetLevelsCents) Choice(buttonPrefab, bets, Money.Format(bet));

            Rect("Space", column).gameObject.AddComponent<LayoutElement>().preferredHeight = 8f;

            var spin = Box("SpinButton", column, Gold);
            spin.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            spin.type = Image.Type.Sliced;
            var spinButton = spin.gameObject.AddComponent<Button>();
            spinButton.colors = Tints();
            spin.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;
            var spinLabel = Text("Label", spin.transform, "SPIN   $1.00", 38f, FontStyles.Bold, Background, TextAlignmentOptions.Center);
            Stretch(spinLabel.rectTransform);
            spinLabel.raycastTarget = false;

            var status = Sized(Text("Status", column, "", 20f, FontStyles.Bold, Alert, TextAlignmentOptions.Left), 30f);

            var result = Box("ResultPanel", column, Panel);
            result.raycastTarget = false;
            result.gameObject.AddComponent<LayoutElement>().preferredHeight = 214f;
            var resultStack = result.gameObject.AddComponent<VerticalLayoutGroup>();
            resultStack.padding = new RectOffset(18, 18, 14, 14);
            resultStack.spacing = 4f;
            resultStack.childAlignment = TextAnchor.MiddleCenter;
            resultStack.childControlWidth = resultStack.childControlHeight = true;
            resultStack.childForceExpandWidth = true;
            resultStack.childForceExpandHeight = false;
            var headline = Sized(Text("Headline", result.transform, "", 52f, FontStyles.Bold, Muted, TextAlignmentOptions.Center), 66f);
            var detail = Sized(Text("Detail", result.transform, "", 24f, FontStyles.Normal, TextColor, TextAlignmentOptions.Center), 34f);
            var note = Sized(Text("Note", result.transform, "Pick a player, a mode and a bet, then press SPIN (or Space).", 19f, FontStyles.Italic, Muted, TextAlignmentOptions.Center), 54f);

            // Debug panel: bottom-left corner. CanvasSpinScreen hides it in release builds.
            var debug = Box("DebugPanel", root.transform, DebugBack);
            debug.raycastTarget = false;
            debug.rectTransform.anchorMin = debug.rectTransform.anchorMax = debug.rectTransform.pivot = Vector2.zero;
            debug.rectTransform.anchoredPosition = new Vector2(16f, 16f);
            debug.rectTransform.sizeDelta = new Vector2(540f, 190f);
            var debugStack = debug.gameObject.AddComponent<VerticalLayoutGroup>();
            debugStack.padding = new RectOffset(14, 14, 10, 10);
            debugStack.spacing = 4f;
            debugStack.childControlWidth = debugStack.childControlHeight = true;
            debugStack.childForceExpandWidth = true;
            debugStack.childForceExpandHeight = false;
            Sized(Text("Title", debug.transform, "DEBUG  (development builds only, F1 hides)", 16f, FontStyles.Bold, Alert, TextAlignmentOptions.Left), 22f);
            var debugText = Sized(Text("Rows", debug.transform, "Deal ID<pos=36%>-", 17f, FontStyles.Normal, TextColor, TextAlignmentOptions.TopLeft), 140f);

            var wiring = new SerializedObject(screen);
            Set(wiring, "choiceButtonPrefab", buttonPrefab);
            Set(wiring, "playerButtons", players);
            Set(wiring, "modeButtons", modes);
            Set(wiring, "betButtons", bets);
            Set(wiring, "balanceText", balance);
            Set(wiring, "spinCountText", spinCount);
            Set(wiring, "spinButton", spinButton);
            Set(wiring, "spinButtonLabel", spinLabel);
            Set(wiring, "statusText", status);
            Set(wiring, "headlineText", headline);
            Set(wiring, "detailText", detail);
            Set(wiring, "noteText", note);
            Set(wiring, "debugPanel", debug.gameObject);
            Set(wiring, "debugText", debugText);
            wiring.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, CanvasPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---- scene --------------------------------------------------------------------------------------------

        private static void BuildScene(GameObject canvasPrefab)
        {
            var game = GameSceneCreator.NewScene();

            var canvas = (GameObject)PrefabUtility.InstantiatePrefab(canvasPrefab);
            var screenWiring = new SerializedObject(canvas.GetComponent<CanvasSpinScreen>());
            Set(screenWiring, "gameRoot", game.GetComponent<GameRoot>());
            screenWiring.ApplyModifiedPropertiesWithoutUndo();

            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();

            GameSceneCreator.Save();
        }

        // ---- helpers ------------------------------------------------------------------------------------------

        private static ColorBlock Tints()
        {
            var tints = ColorBlock.defaultColorBlock;
            tints.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
            tints.pressedColor = new Color(0.72f, 0.72f, 0.72f);
            tints.selectedColor = Color.white; // no lasting highlight after a click
            tints.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.55f);
            return tints;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image Box(string name, Transform parent, Color color)
        {
            var image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string text, float size, FontStyles style, Color color, TextAlignmentOptions alignment)
        {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        private static T Sized<T>(T component, float height) where T : Component
        {
            component.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return component;
        }

        private static void Caption(Transform parent, string text) =>
            Sized(Text(text + "Caption", parent, text, 20f, FontStyles.Bold, Muted, TextAlignmentOptions.BottomLeft), 28f);

        private static RectTransform Row(string name, Transform parent, float height = 48f)
        {
            var row = Rect(name, parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false; // true would make the row flexible, and the column would stretch it
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return row;
        }

        private static void Choice(Button prefab, Transform parent, string label)
        {
            var button = (Button)PrefabUtility.InstantiatePrefab(prefab, parent);
            button.name = "Choice " + label;
            button.GetComponentInChildren<TMP_Text>().text = label;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Set(SerializedObject target, string field, Object value)
        {
            var property = target.FindProperty(field);
            if (property == null) throw new System.ArgumentException(target.targetObject.GetType().Name + " has no field " + field);
            property.objectReferenceValue = value;
        }
    }
}
