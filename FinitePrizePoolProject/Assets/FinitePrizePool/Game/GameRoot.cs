using System;
using System.IO;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Service;
using UnityEngine;

namespace FinitePrizePool.Game
{
    /// <summary>
    /// Composition root: the only place that knows which store, runner and log the engine runs with on a device.
    /// Everything else in the game talks to the engine through <see cref="IDealService"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameRoot : MonoBehaviour
    {
        private DealService _service;

        public IDealService Service => _service;
        public string StartupError { get; private set; }

        public static string SaveFolder => Path.Combine(Application.persistentDataPath, "FinitePrizePool");

        private void Awake()
        {
            var log = new UnityEngineLog();
            try
            {
                var config = ConfigAsset.Load();
                var store = new JsonFileStore(SaveFolder, log);
                _service = new DealService(config, store, new TaskBackgroundRunner(log), log: log);
                GameServices.Register(_service);
                Debug.Log("[FinitePrizePool] Config " + config.ConfigVersion + ", saves in " + SaveFolder);
            }
            catch (Exception e)
            {
                StartupError = e.Message;
                Debug.LogException(e);
            }
        }

        private void OnDestroy()
        {
            if (_service == null) return;
            GameServices.Unregister(_service);
            _service.Dispose();
        }
    }

    /// <summary>Lets the Editor admin window reach the running game's engine in Play mode.</summary>
    public static class GameServices
    {
        public static DealService Current { get; private set; }

        public static void Register(DealService service) => Current = service;

        public static void Unregister(DealService service)
        {
            if (Current == service) Current = null;
        }
    }
}
