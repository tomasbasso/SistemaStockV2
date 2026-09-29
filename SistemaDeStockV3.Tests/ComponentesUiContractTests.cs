using System.Text.RegularExpressions;

namespace SistemaDeStockV3.Tests;

/// <summary>
/// En Windows, WebView2 dibuja la lista desplegable del &lt;select&gt; nativo con fondo blanco e
/// ignora el CSS: sobre el tema oscuro las opciones quedan casi invisibles. Es una limitación
/// de la plataforma, así que todas las listas tienen que usar AppSelect (desplegable propio).
/// </summary>
public class ComponentesUiContractTests
{
    [Fact]
    public void Componentes_NoUsanSelectNativo_SoloAppSelect()
    {
        var componentes = Path.Combine(RepoRoot(), "SistemaDeStockV3", "Components");

        var conSelectNativo = Directory.EnumerateFiles(componentes, "*.razor", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("AppSelect.razor", StringComparison.OrdinalIgnoreCase))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"<(select|InputSelect)\b"))
            .Select(f => Path.GetRelativePath(componentes, f))
            .ToList();

        Assert.Empty(conSelectNativo);
    }

    private static string RepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
