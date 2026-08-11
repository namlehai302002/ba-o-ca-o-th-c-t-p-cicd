namespace WMS.Tests;

public sealed class Tier1ScorecardEvidenceTests
{
    [Fact]
    public void BenchmarkScorecard_ShouldUseLatestRepoLocalEvidenceAndCorrectVisualArtifacts()
    {
        var root = FindRepositoryRoot();
        var scorecard = File.ReadAllText(Path.Combine(root, "FINAL_WMS_ENTERPRISE_QA_REPORT.md"));

        Assert.Contains("Pass, 697/697", scorecard, StringComparison.Ordinal);
        Assert.Contains("EV-OCR-001", scorecard, StringComparison.Ordinal);
        Assert.Contains("OCR multi-document", scorecard, StringComparison.Ordinal);
        Assert.DoesNotContain("584/584", scorecard, StringComparison.Ordinal);
        Assert.DoesNotContain("589/589", scorecard, StringComparison.Ordinal);
        Assert.DoesNotContain("603/603", scorecard, StringComparison.Ordinal);
        Assert.DoesNotContain("666/666", scorecard, StringComparison.Ordinal);
        Assert.DoesNotContain("671/671", scorecard, StringComparison.Ordinal);
        Assert.DoesNotContain("670/670", scorecard, StringComparison.Ordinal);
        Assert.DoesNotContain("675/675", scorecard, StringComparison.Ordinal);

        foreach (var relativePath in new[]
        {
            "test-results/.last-run.json",
            "artifacts/visual-public/test-results/.last-run.json",
            "artifacts/visual-no-device/test-results/.last-run.json",
            "artifacts/visual-mobile-deep/test-results/.last-run.json"
        })
        {
            Assert.Contains(relativePath, scorecard, StringComparison.Ordinal);
        }

        Assert.Contains("repo/local enterprise readiness", scorecard, StringComparison.Ordinal);
        Assert.Contains("96/100", scorecard, StringComparison.Ordinal);
        Assert.Contains("Tier-1 production equivalence: 89-91%", scorecard, StringComparison.Ordinal);
        Assert.Contains("Production Tier-1 remains 89-91%", scorecard, StringComparison.Ordinal);
    }

    [Fact]
    public void Tier1ProductionChecklist_ShouldKeepExternalEvidenceBoundaryExplicit()
    {
        var root = FindRepositoryRoot();
        var checklist = File.ReadAllText(Path.Combine(root, "docs", "TIER1_PRODUCTION_EVIDENCE_CHECKLIST_2026_05_29.md"));

        foreach (var token in new[]
        {
            "Repo/Local Evidence Snapshot",
            "Pass, 697/697",
            "FINAL_WMS_ENTERPRISE_QA_REPORT.md",
            "external evidence ID",
            "ngày chạy thật, owner và artifact đã redact"
        })
        {
            Assert.Contains(token, checklist, StringComparison.Ordinal);
        }

        foreach (var evidenceId in new[]
        {
            "HW-RF-001",
            "HW-SCAN-001",
            "HW-PRINT-001",
            "LOAD-001",
            "DR-001",
            "INT-ERP-001",
            "OBS-001"
        })
        {
            Assert.Contains(evidenceId, checklist, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Tier1Roadmap_ShouldDefineHonestHundredPercentBoundaryAndGlobalBenchmarks()
    {
        var root = FindRepositoryRoot();
        var roadmap = File.ReadAllText(Path.Combine(root, "WMS_TIER1_100_PERCENT_ROADMAP.md"));

        foreach (var token in new[]
        {
            "95-96/100",
            "88-91/100",
            "100% khong co nghia la \"khong bao gio con bug\"",
            "Oracle WMS Cloud 26B",
            "Microsoft Dynamics 365 SCM Warehouse",
            "SAP EWM",
            "Manhattan Active WM",
            "RF scanner",
            "LOAD-001",
            "DR-001",
            "INT-ERP-001",
            "OBS-001",
            "Production 100%"
        })
        {
            Assert.Contains(token, roadmap, StringComparison.Ordinal);
        }

        Assert.Contains("Khong sua `appsettings.json`", roadmap, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WMS.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate WMS.sln.");
    }
}
