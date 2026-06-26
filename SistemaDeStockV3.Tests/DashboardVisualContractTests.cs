namespace SistemaDeStockV3.Tests;

public class DashboardVisualContractTests
{
    [Fact]
    public void AppCss_Uses_ReferenceInspiredDashboardPalette()
    {
        var css = ReadRepoFile("SistemaDeStockV3", "wwwroot", "css", "app.css");

        // Paleta corporativa azul — aprobada 2026-04
        // Usamos Regex para tolerar espacios de alineación entre token y valor
        Assert.Matches(@"--color-primary:\s+#1E293B;", css);
        Assert.Matches(@"--color-primary-dark:\s+#0F172A;", css);
        Assert.Matches(@"--color-bg:\s+#F8FAFC;", css);
        Assert.Matches(@"--color-surface:\s+#FFFFFF;", css);
        Assert.Matches(@"--color-border:\s+#E2E8F0;", css);
        Assert.Matches(@"--color-text-primary:\s+#1E293B;", css);
        Assert.Matches(@"--color-text-secondary:\s+#64748B;", css);
        Assert.Matches(@"--color-success:\s+#16A34A;", css);
        Assert.Matches(@"--color-warning:\s+#D97706;", css);
        Assert.Matches(@"--color-danger:\s+#DC2626;", css);
        Assert.Matches(@"--color-neutral:\s+#94A3B8;", css);
        Assert.Contains(".dashboard-search-shell", css);
        Assert.Contains(".dashboard-surface", css);
    }

    [Fact]
    public void HomeDashboard_Declares_NewVisualSections()
    {
        var home = ReadRepoFile("SistemaDeStockV3", "Components", "Pages", "Home.razor");

        Assert.Contains("dashboard-shell", home);
        Assert.Contains("dashboard-hero", home);
        Assert.Contains("dashboard-search-shell", home);
        Assert.Contains("dashboard-stat-card", home);
        Assert.Contains("dashboard-highlight-card", home);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var target = Path.Combine(new[] { repoRoot }.Concat(segments).ToArray());
        return File.ReadAllText(target);
    }
}
