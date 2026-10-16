using System;
using System.Collections.Generic;
using FinitePrizePool.Engine.Config;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Reports;
using FinitePrizePool.Game;
using UnityEditor;
using UnityEngine;

namespace FinitePrizePool.EditorTools
{
    /// <summary>The deal-size table for every mode and bet (§4.2), computed from the current config, with CSV export.</summary>
    public sealed class DealSizeTableWindow : EditorWindow
    {
        private static readonly (string Title, float Width)[] Columns =
        {
            ("Mode", 55), ("Bet", 55), ("Payout / cycle", 95), ("Cycles", 55), ("Paid spins", 75), ("Regular", 60),
            ("Big", 45), ("Prizes", 55), ("Losses", 60), ("Wagers", 90), ("Payouts", 90), ("RTP", 80),
            ("Max losses", 75), ("Max R gap", 70), ("Max B gap", 70), ("Status", 300)
        };

        private PoolConfig _config;
        private IReadOnlyList<DealSizeRow> _rows;
        private string _error;
        private Vector2 _scroll;

        [MenuItem("Tools/Finite Prize Pool/Deal Size Table")]
        public static void Open() => GetWindow<DealSizeTableWindow>("Deal Size Table");

        private void OnEnable() => Refresh();

        private void Refresh()
        {
            try
            {
                _config = ConfigAsset.Load();
                _rows = DealSizeTable.Build(_config);
                _error = null;
            }
            catch (Exception e)
            {
                _rows = null;
                _error = e.Message;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70))) Refresh();
            GUI.enabled = _rows != null;
            if (GUILayout.Button("Export CSV...", EditorStyles.toolbarButton, GUILayout.Width(100)))
                EditorFiles.SaveWithDialog("Export deal-size table", "DealSizeTable.csv", DealSizeTable.ToCsv(_rows, _config));
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (_config != null) GUILayout.Label("Config " + _config.ConfigVersion + ", target " + Rtp.FormatBasisPoints(_config.TargetRtpBasisPoints) + ", " + _config.RtpMode, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawRow(EditorStyles.boldLabel, Columns.Length, i => Columns[i].Title);
            foreach (var row in _rows) DrawRow(EditorStyles.label, Columns.Length, i => Cell(row, i));
            EditorGUILayout.EndScrollView();
        }

        private static void DrawRow(GUIStyle style, int count, Func<int, string> text)
        {
            EditorGUILayout.BeginHorizontal();
            for (var i = 0; i < count; i++) GUILayout.Label(text(i), style, GUILayout.Width(Columns[i].Width));
            EditorGUILayout.EndHorizontal();
        }

        private static string Cell(DealSizeRow row, int column)
        {
            var plan = row.Plan;
            if (!plan.IsValid && column > 2 && column < 15) return "";
            switch (column)
            {
                case 0: return plan.Mode;
                case 1: return Money.Format(plan.BetCents);
                case 2: return Money.Format(plan.PayoutPerPairedCycleCents);
                case 3: return plan.PairedCycles.ToString();
                case 4: return plan.PaidSpins.ToString("N0");
                case 5: return plan.RegularTickets.ToString("N0");
                case 6: return plan.BigTickets.ToString("N0");
                case 7: return plan.PrizeTickets.ToString("N0");
                case 8: return plan.LosingTickets.ToString("N0");
                case 9: return Money.Format(plan.TotalWagerCents);
                case 10: return Money.Format(plan.TotalPayoutCents);
                case 11: return plan.PlannedRtp.Format();
                case 12: return row.Limits?.MaxConsecutiveLosses.ToString();
                case 13: return row.Limits?.MaxRegularGap.ToString();
                case 14: return row.Limits?.MaxBigGap.ToString();
                default: return row.Status;
            }
        }
    }
}
