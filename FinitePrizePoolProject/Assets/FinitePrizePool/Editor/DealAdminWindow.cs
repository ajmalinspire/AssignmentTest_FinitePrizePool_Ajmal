using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Reports;
using FinitePrizePool.Engine.Service;
using FinitePrizePool.Game;
using UnityEditor;
using UnityEngine;

namespace FinitePrizePool.EditorTools
{
    /// <summary>
    /// Admin window (§5): retire an active deal (§4.7), apply a changed config, and view the completed and
    /// retired deal records and the retire log. In Play mode it works on the running game; outside Play mode
    /// it shows the saved records read-only.
    /// </summary>
    public sealed class DealAdminWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Active deals", "Completed", "Retired", "Retire log" };

        private int _tab;
        private string _reason = "";
        private string _status;
        private Vector2 _scroll;
        private Ledger _savedLedger;
        private DealHistory _savedHistory;

        [MenuItem("Tools/Finite Prize Pool/Deal Admin")]
        public static void Open() => GetWindow<DealAdminWindow>("Deal Admin");

        private void OnEnable() => LoadSaved();
        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            var live = Application.isPlaying ? GameServices.Current : null;
            DrawToolbar(live);
            if (_status != null) EditorGUILayout.HelpBox(_status, MessageType.Info);

            if (live == null)
                EditorGUILayout.HelpBox("Showing the saved records (read-only). Enter Play mode to retire deals or apply a config.", MessageType.None);
            else
                DrawLiveActions(live);

            _tab = GUILayout.Toolbar(_tab, Tabs);
            var active = live != null ? live.GetActiveDeals() : SavedActiveDeals();
            var finished = live != null ? live.GetFinishedDeals() : (IReadOnlyList<FinishedDealRecord>)_savedHistory?.Finished ?? new List<FinishedDealRecord>();
            var retireLog = live != null ? live.GetRetireLog() : (IReadOnlyList<RetireRecord>)_savedHistory?.RetireLog ?? new List<RetireRecord>();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: DrawActive(active, live); break;
                case 1: DrawFinished(finished.Where(r => r.State == DealState.Completed)); break;
                case 2: DrawFinished(finished.Where(r => r.State == DealState.Retired)); break;
                default: DrawRetireLog(retireLog); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar(DealService live)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Reload saved", EditorStyles.toolbarButton, GUILayout.Width(90))) LoadSaved();
            if (GUILayout.Button("Export audit CSV...", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                var records = live != null ? live.GetFinishedDeals() : (IEnumerable<FinishedDealRecord>)_savedHistory?.Finished ?? new List<FinishedDealRecord>();
                EditorFiles.SaveWithDialog("Export completed and retired deals", "AuditDeals.csv", AuditCsv.FinishedDeals(records));
            }
            if (GUILayout.Button("Export retire log CSV...", EditorStyles.toolbarButton, GUILayout.Width(145)))
            {
                var records = live != null ? live.GetRetireLog() : (IEnumerable<RetireRecord>)_savedHistory?.RetireLog ?? new List<RetireRecord>();
                EditorFiles.SaveWithDialog("Export retire log", "RetireLog.csv", AuditCsv.RetireLog(records));
            }
            if (GUILayout.Button("Open save folder", EditorStyles.toolbarButton, GUILayout.Width(110)))
            {
                Directory.CreateDirectory(GameRoot.SaveFolder);
                EditorUtility.RevealInFinder(GameRoot.SaveFolder);
            }
            GUILayout.FlexibleSpace();
            GUI.enabled = !Application.isPlaying;
            if (GUILayout.Button("Delete save data...", EditorStyles.toolbarButton, GUILayout.Width(120))) DeleteSaves();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLiveActions(DealService live)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Running config", live.ConfigVersion, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Apply config file", GUILayout.Width(130))) ApplyConfig(live);
            EditorGUILayout.EndHorizontal();
            _reason = EditorGUILayout.TextField("Retire reason", _reason);
        }

        private void ApplyConfig(DealService live)
        {
            try
            {
                var config = ConfigAsset.Load();
                if (config.ConfigVersion == live.ConfigVersion)
                {
                    _status = "The config file still says '" + config.ConfigVersion + "'. Change configVersion so the new settings are recognised.";
                    return;
                }
                live.ApplyConfig(config);
                _status = "Config " + config.ConfigVersion + " applied. Active deals keep their version; Ready successors are rebuilt.";
            }
            catch (Exception e)
            {
                _status = "Config not applied: " + e.Message;
            }
        }

        private void DrawActive(IReadOnlyList<DealStatus> deals, DealService live)
        {
            if (deals.Count == 0) EditorGUILayout.LabelField("No active deals yet. Play a mode and bet first.");
            foreach (var deal in deals.OrderBy(d => d.Mode).ThenBy(d => d.BetCents))
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(deal.Mode + " " + Money.Format(deal.BetCents) + "   " + deal.DealId + "   (config " + deal.ConfigVersion + ")", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Tickets used", deal.Position + " / " + deal.TicketCount);
                EditorGUILayout.LabelField("Wagers / payouts", Money.Format(deal.WagerCents) + " / " + Money.Format(deal.PayoutCents)
                                                                + "   RTP so far " + new Rtp(deal.PayoutCents, deal.WagerCents).Format());
                if (live != null)
                {
                    GUI.enabled = !string.IsNullOrWhiteSpace(_reason);
                    if (GUILayout.Button("Retire this deal", GUILayout.Width(140))) Retire(live, deal);
                    GUI.enabled = true;
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void Retire(DealService live, DealStatus deal)
        {
            var question = "Retire " + deal.DealId + " (" + deal.Mode + " " + Money.Format(deal.BetCents) + ")?\n\n"
                           + (deal.TicketCount - deal.Position) + " unused tickets will be kept for audit, and play switches to a freshly verified replacement.";
            if (!EditorUtility.DisplayDialog("Retire deal", question, "Retire", "Cancel")) return;
            try
            {
                var record = live.Retire(deal.Mode, deal.BetCents, _reason.Trim());
                _status = "Retired " + record.RetiredDealId + "; now playing " + record.ReplacementDealId + ".";
                _reason = "";
            }
            catch (Exception e)
            {
                _status = "Not retired: " + e.Message;
            }
        }

        private static void DrawFinished(IEnumerable<FinishedDealRecord> records)
        {
            var list = records.OrderByDescending(r => r.FinishedUtc).ToList();
            if (list.Count == 0) EditorGUILayout.LabelField("None yet.");
            foreach (var r in list)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(r.DealId + "   " + r.Mode + " " + Money.Format(r.BetCents) + "   " + r.State + "   (config " + r.ConfigVersion + ")", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Tickets", r.TicketsUsed + " used of " + r.TicketCount + " (" + r.TicketsRemaining + " left)");
                EditorGUILayout.LabelField("Wagers / payouts", Money.Format(r.TotalWagerCents) + " / " + Money.Format(r.TotalPayoutCents));
                EditorGUILayout.LabelField("RTP", "target " + Rtp.FormatBasisPoints(r.TargetRtpBasisPoints) + ", actual " + r.ActualRtpPercent.ToString("0.0000") + "%"
                                                  + (r.ReachedTarget ? "  (reached the target)" : "  (did NOT reach the target)"));
                EditorGUILayout.LabelField("Finished (UTC)", r.FinishedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                if (!string.IsNullOrEmpty(r.Note)) EditorGUILayout.LabelField("Note", r.Note, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndVertical();
            }
        }

        private static void DrawRetireLog(IReadOnlyList<RetireRecord> records)
        {
            if (records.Count == 0) EditorGUILayout.LabelField("No deal has been retired.");
            foreach (var r in records.OrderByDescending(r => r.TimeUtc))
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(r.RetiredDealId + " -> " + (r.ReplacementDealId ?? "(none)") + "   " + r.Mode + " " + Money.Format(r.BetCents), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Reason", r.Reason, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Time (UTC)", r.TimeUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                EditorGUILayout.LabelField("Config", r.OldConfigVersion + " -> " + (r.NewConfigVersion ?? "-"));
                EditorGUILayout.LabelField("Tickets", r.TicketsUsed + " used, " + r.TicketsRemaining + " remaining");
                EditorGUILayout.LabelField("Wagers / payouts", Money.Format(r.WagerCents) + " / " + Money.Format(r.PayoutCents) + ", actual RTP " + r.ActualRtpPercent.ToString("0.0000") + "%");
                EditorGUILayout.EndVertical();
            }
        }

        private IReadOnlyList<DealStatus> SavedActiveDeals()
        {
            return _savedLedger?.Slots.Where(s => s.Active != null).Select(s => s.Active).ToList() ?? new List<DealStatus>();
        }

        private void LoadSaved()
        {
            var store = new JsonFileStore(GameRoot.SaveFolder, new UnityEngineLog());
            _savedLedger = store.LoadLedger();
            _savedHistory = store.LoadHistory();
        }

        private void DeleteSaves()
        {
            const string question = "Delete every deal, balance and spin record saved by the game on this machine? This cannot be undone.";
            if (!EditorUtility.DisplayDialog("Delete save data", question + "\n\n" + GameRoot.SaveFolder, "Delete", "Cancel")) return;
            if (Directory.Exists(GameRoot.SaveFolder)) Directory.Delete(GameRoot.SaveFolder, true);
            LoadSaved();
            _status = "Save data deleted. The next Play starts fresh.";
        }
    }
}
