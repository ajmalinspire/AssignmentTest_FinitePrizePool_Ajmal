using System.IO;
using System.Linq;
using FinitePrizePool.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FinitePrizePool.EditorTools
{
    /// <summary>
    /// The parts of the game scene that do not depend on the UI: a camera and one object holding the engine
    /// (GameRoot). The Canvas UI builder (Tools > Finite Prize Pool > Build Canvas UI) starts from this and adds the
    /// Canvas and the EventSystem.
    /// </summary>
    public static class GameSceneCreator
    {
        public const string ScenePath = "Assets/FinitePrizePool/Scenes/Game.unity";
        public const string GameObjectName = "Finite Prize Pool";

        /// <summary>Asks before replacing an existing scene or discarding unsaved changes. True to go ahead.</summary>
        public static bool ConfirmReplace()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Build Canvas UI", ScenePath + " already exists. Replace it?", "Replace", "Cancel"))
                return false;
            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        /// <summary>Opens a new scene with the camera and the game object (GameRoot), and returns that object.</summary>
        public static GameObject NewScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f);

            var game = new GameObject(GameObjectName);
            game.AddComponent<GameRoot>();
            return game;
        }

        public static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AddToBuildSettings();
        }

        private static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == ScenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
