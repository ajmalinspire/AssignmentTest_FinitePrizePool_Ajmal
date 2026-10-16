using FinitePrizePool.Engine.Reports;
using FinitePrizePool.Game;
using UnityEditor;
using UnityEngine;

namespace FinitePrizePool.EditorTools
{
    /// <summary>
    /// Writes the submission documents straight to Docs, with no dialog, so they can be produced from a menu,
    /// from the Unity CLI against a running Editor, or in batch mode with -executeMethod.
    /// </summary>
    public static class ReportExports
    {
        [MenuItem("Tools/Finite Prize Pool/Write Reports/Deal Size Table CSV")]
        public static void WriteDealSizeTable()
        {
            var config = ConfigAsset.Load();
            EditorFiles.WriteToDocs("DealSizeTable.csv", DealSizeTable.ToCsv(DealSizeTable.Build(config), config));
        }

        /// <summary>The §7.4 report: 10 complete Inner $1.00 deals and one complete Outer $1.00 deal.</summary>
        [MenuItem("Tools/Finite Prize Pool/Write Reports/Simulation Report")]
        public static void WriteSimulationReport()
        {
            var config = ConfigAsset.Load();
            var results = new[]
            {
                Simulator.PlayCompleteDeals(config, "Inner", 100, 10),
                Simulator.PlayCompleteDeals(config, "Outer", 100, 1)
            };
            foreach (var result in results)
            {
                Debug.Log("[FinitePrizePool] Simulated " + result.Mode + " " + result.BetCents + "c: "
                          + result.Deals.Count + " deal(s), " + result.TotalSpins + " spins");
            }
            EditorFiles.WriteToDocs("SimulationReport.md",
                "# Simulation report\n\n" + SimulationReport.ToMarkdown(results, "ReportExports.WriteSimulationReport (same engine code as the game)"));
        }

        [MenuItem("Tools/Finite Prize Pool/Write Reports/All")]
        public static void WriteAll()
        {
            WriteDealSizeTable();
            WriteSimulationReport();
        }
    }
}
