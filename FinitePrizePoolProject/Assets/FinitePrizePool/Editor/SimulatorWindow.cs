using System;
using System.Collections.Generic;
using System.Linq;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Reports;
using FinitePrizePool.Game;
using UnityEditor;
using UnityEngine;

namespace FinitePrizePool.EditorTools
{
    /// <summary>
    /// Auto-play simulator (stretch goal, §6): plays whole deals through the real engine in memory, without
    /// animation or saves to disk, and reports the actual RTP of each deal.
    /// </summary>
    public sealed class SimulatorWindow : EditorWindow
    {
        private PoolConfig _config;
        private string _error;
        private int _modeIndex;
        private int _betIndex;
        private int _deals = 10;
        private readonly List<SimulationResult> _results = new List<SimulationResult>();
        private Vector2 _scroll;

        // Real-world play: both players, random modes, bets and session lengths.
        private readonly RealWorldSettings _realWorld = new RealWorldSettings();
        private int _minBetIndex;
        private int _maxBetIndex;
        private int _startingDollars = 10000;
        private RealWorldResult _realWorldResult;

        [MenuItem("Tools/Finite Prize Pool/Simulator")]
        public static void Open() => GetWindow<SimulatorWindow>("Deal Simulator");

        private void OnEnable()
        {
            try
            {
                _config = ConfigAsset.Load();
                _betIndex = Math.Max(0, _config.BetLevelsCents.ToList().IndexOf(100));
                _maxBetIndex = _config.BetLevelsCents.Count - 1;
            }
            catch (Exception e)
            {
                _error = e.Message;
            }
        }

        private void OnGUI()
        {
            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Config", _config.ConfigVersion);
            _modeIndex = EditorGUILayout.Popup("Mode", _modeIndex, _config.ModeNames.ToArray());
            _betIndex = EditorGUILayout.Popup("Bet", _betIndex, _config.BetLevelsCents.Select(Money.Format).ToArray());
            _deals = Mathf.Clamp(EditorGUILayout.IntField("Complete deals", _deals), 1, 1000);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run")) Run(_config.ModeNames[_modeIndex], _config.BetLevelsCents[_betIndex], _deals);
            if (GUILayout.Button("Assignment report (10 Inner $1 + 1 Outer $1)")) RunAssignmentReport();
            GUI.enabled = _results.Count > 0 || _realWorldResult != null;
            if (GUILayout.Button("Save report...")) EditorFiles.SaveWithDialog("Save simulation report", "SimulationReport.md", ReportText());
            if (GUILayout.Button("Clear"))
            {
                _results.Clear();
                _realWorldResult = null;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            DrawRealWorldSettings();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_realWorldResult != null) DrawRealWorldResult(_realWorldResult);
            foreach (var result in _results) DrawResult(result);
            EditorGUILayout.EndScrollView();
        }

        private void DrawRealWorldSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Real-world play: every test player, random modes, bets and session lengths, sharing each deal", EditorStyles.boldLabel);
            var bets = _config.BetLevelsCents.Select(Money.Format).ToArray();

            _realWorld.TargetCompletedDeals = Mathf.Clamp(EditorGUILayout.IntField("Complete deals (n)", _realWorld.TargetCompletedDeals), 1, 10000);
            EditorGUILayout.BeginHorizontal();
            _realWorld.IncludeInner = EditorGUILayout.ToggleLeft("Inner", _realWorld.IncludeInner, GUILayout.Width(70));
            _realWorld.IncludeOuter = EditorGUILayout.ToggleLeft("Outer", _realWorld.IncludeOuter, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _minBetIndex = EditorGUILayout.Popup("Bets from", _minBetIndex, bets);
            _maxBetIndex = EditorGUILayout.Popup("to", Math.Max(_minBetIndex, _maxBetIndex), bets);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _realWorld.MinSessionSpins = Mathf.Clamp(EditorGUILayout.IntField("Session spins from", _realWorld.MinSessionSpins), 1, 10000);
            _realWorld.MaxSessionSpins = Mathf.Clamp(EditorGUILayout.IntField("to", _realWorld.MaxSessionSpins), _realWorld.MinSessionSpins, 10000);
            EditorGUILayout.EndHorizontal();
            _startingDollars = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Starting balance ($)", "Each player's balance. 0 uses the balances in the config."), _startingDollars));
            _realWorld.MaxSpins = Math.Max(1, EditorGUILayout.LongField(new GUIContent("Stop after spins", "Safety limit: Outer deals at high bets run to tens of thousands of spins."), _realWorld.MaxSpins));

            GUI.enabled = _realWorld.IncludeInner || _realWorld.IncludeOuter;
            if (GUILayout.Button("Run real-world play")) RunRealWorld();
            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        private void RunRealWorld()
        {
            _realWorld.MinBetCents = _config.BetLevelsCents[_minBetIndex];
            _realWorld.MaxBetCents = _config.BetLevelsCents[Math.Max(_minBetIndex, _maxBetIndex)];
            _realWorld.StartingBalanceCents = _startingDollars * 100L;
            try
            {
                var target = _realWorld.TargetCompletedDeals;
                _realWorldResult = RealWorldSimulator.Run(_config, _realWorld, (spins, completed) =>
                    !EditorUtility.DisplayCancelableProgressBar("Real-world play",
                        spins.ToString("N0") + " spins, " + completed + " of " + target + " deals completed", Mathf.Clamp01((float)completed / target)));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void DrawRealWorldResult(RealWorldResult result)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var completed = result.CompletedDeals.ToList();
            EditorGUILayout.LabelField("Real-world play: " + result.Spins.ToString("N0") + " spins, " + result.Sessions + " sessions, "
                                       + result.Elapsed.TotalSeconds.ToString("0.00") + " s", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Stopped", result.StopReason);
            foreach (var p in result.Players)
            {
                EditorGUILayout.LabelField(p.Name + (p.RanOut ? " (ran out)" : ""), p.Spins.ToString("N0") + " spins   wagered " + Money.Format(p.WagerCents)
                                                                                       + "   won " + Money.Format(p.PayoutCents) + "   player RTP " + p.Rtp.Format(2));
            }
            EditorGUILayout.LabelField("Completed deals", completed.Count + ", " + completed.Count(d => d.ReachedTarget) + " at exactly 90%");
            foreach (var d in completed)
            {
                EditorGUILayout.LabelField("  " + d.DealId, d.Mode + " " + Money.Format(d.BetCents) + "   " + d.Spins.ToString("N0") + " spins   RTP " + d.Rtp.Format()
                                                            + "   " + string.Join(", ", d.SpinsByPlayer.Select(kv => result.Players.First(p => p.Id == kv.Key).Name + " " + kv.Value))
                                                            + (d.ReachedTarget ? "" : "   NOT EXACT"));
            }
            var unfinished = result.UnfinishedDeals.ToList();
            if (unfinished.Count > 0)
            {
                var rtp = new Rtp(unfinished.Sum(d => d.PayoutCents), unfinished.Sum(d => d.WagerCents));
                EditorGUILayout.LabelField("Unfinished deals", unfinished.Count + ", RTP so far " + rtp.Format(2) + " (not final)");
            }
            if (result.RejectedSlots.Count > 0) EditorGUILayout.HelpBox("Left out: " + string.Join("; ", result.RejectedSlots), MessageType.Warning);
            EditorGUILayout.EndVertical();
        }

        private void Run(string mode, long bet, int deals)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Simulating", mode + " " + Money.Format(bet) + ": " + deals + " deal(s)", 0.5f);
                _results.Add(Simulator.PlayCompleteDeals(_config, mode, bet, deals));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void RunAssignmentReport()
        {
            _results.Clear();
            _realWorldResult = null; // the §7.4 report holds exactly what the brief asks for
            Run("Inner", 100, 10);
            Run("Outer", 100, 1);
            EditorFiles.WriteToDocs("SimulationReport.md", ReportText());
        }

        private string ReportText() => "# Simulation report\n\n"
                                       + (_realWorldResult != null ? SimulationReport.ToMarkdown(_realWorldResult) + "\n" : "")
                                       + SimulationReport.ToMarkdown(_results, "the Deal Simulator window (same engine code as the game)");

        private static void DrawResult(SimulationResult result)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(result.Mode + " " + Money.Format(result.BetCents) + ": " + result.Deals.Count + " deal(s), "
                                       + result.TotalSpins.ToString("N0") + " spins in " + result.Elapsed.TotalSeconds.ToString("0.00") + " s", EditorStyles.boldLabel);
            foreach (var deal in result.Deals)
            {
                EditorGUILayout.LabelField(deal.DealId, deal.Spins + " spins   wagers " + Money.Format(deal.WagerCents) + "   payouts " + Money.Format(deal.PayoutCents)
                                                        + "   RTP " + deal.Rtp.Format() + (deal.ReachedTarget ? "" : "   NOT EXACT"));
            }
            EditorGUILayout.EndVertical();
        }
    }
}
