using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SistemaDeStockV3.Services;

namespace SistemaDeStockV3.Tests;

/// <summary>
/// Tests de respaldo y restauración sobre archivos SQLite reales (no en memoria),
/// porque los problemas históricos venían justamente del archivo: el WAL que
/// quedaba al lado de la base y "pisaba" la restauración, y copias a medio escribir.
/// </summary>
public class RespaldoSqliteTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public RespaldoSqliteTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "RespaldoSqliteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "stock.db");
    }

    // Sin SqliteConnection.ClearAllPools(): cerraría las conexiones de los otros tests que
    // corren en paralelo y sus bases en memoria desaparecerían. Acá todo usa Pooling=False.
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private string Ruta(string nombre) => Path.Combine(_dir, nombre);

    private static SqliteConnection Abrir(string path)
    {
        var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        return conn;
    }

    private static void Ejecutar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Base con el mismo modo WAL que usa EF Core y las tablas mínimas del sistema.</summary>
    private static void CrearBaseDelSistema(string path, string producto)
    {
        using var conn = Abrir(path);
        Ejecutar(conn, "PRAGMA journal_mode=WAL;");
        Ejecutar(conn, "CREATE TABLE Productos (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);");
        Ejecutar(conn, "CREATE TABLE Ventas (Id TEXT PRIMARY KEY);");
        Ejecutar(conn, $"INSERT INTO Productos VALUES ('{Guid.NewGuid()}', '{producto}');");
    }

    private static List<string> Productos(string path)
    {
        using var conn = Abrir(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Productos ORDER BY Name;";
        using var r = cmd.ExecuteReader();
        var nombres = new List<string>();
        while (r.Read()) nombres.Add(r.GetString(0));
        return nombres;
    }

    /// <summary>
    /// Deja un cambio en la base que queda solo en el archivo -wal (sin checkpoint),
    /// como pasa con la app abierta. Devuelve la conexión abierta para mantenerlo así.
    /// </summary>
    private static SqliteConnection AgregarProductoSoloEnWal(string path, string producto)
    {
        var conn = Abrir(path);
        Ejecutar(conn, "PRAGMA wal_autocheckpoint=0;");
        Ejecutar(conn, $"INSERT INTO Productos VALUES ('{Guid.NewGuid()}', '{producto}');");
        return conn;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // ──────────────────────────────────────────────────────────────────────
    // CrearCopia
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void CrearCopia_IncluyeLosCambiosQueTodaviaEstanEnElWal()
    {
        CrearBaseDelSistema(_dbPath, "Martillo");
        using var appAbierta = AgregarProductoSoloEnWal(_dbPath, "Taladro");

        RespaldoSqlite.CrearCopia(_dbPath, Ruta("copia.db"));

        Assert.Equal(new[] { "Martillo", "Taladro" }, Productos(Ruta("copia.db")));
    }

    [Fact]
    public void CrearCopia_SiFalla_NoPisaElRespaldoAnteriorConElMismoNombre()
    {
        CrearBaseDelSistema(Ruta("UltimoCierre.db"), "Respaldo bueno");
        File.WriteAllText(_dbPath, "esto no es una base de datos");

        Assert.ThrowsAny<Exception>(() => RespaldoSqlite.CrearCopia(_dbPath, Ruta("UltimoCierre.db")));

        Assert.Equal(new[] { "Respaldo bueno" }, Productos(Ruta("UltimoCierre.db")));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Restaurar
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Restaurar_TraeLosDatosDelRespaldo_AunqueLaBaseActualTengaCambiosEnElWal()
    {
        // El bug histórico: el -wal de la sesión actual se reaplicaba sobre la base
        // restaurada y la restauración "no traía datos".
        CrearBaseDelSistema(_dbPath, "Del respaldo");
        RespaldoSqlite.CrearCopia(_dbPath, Ruta("respaldo.db"));
        using (AgregarProductoSoloEnWal(_dbPath, "Cargado después"))
        {
            var result = RespaldoSqlite.Restaurar(Ruta("respaldo.db"), _dbPath, Ruta("previa.db"));
            Assert.True(result.Success, result.Message);
        }

        Assert.Equal(new[] { "Del respaldo" }, Productos(_dbPath));
    }

    [Fact]
    public void Restaurar_ElMismoRespaldoSePuedeUsarOtraVez_Y_NoSeModifica()
    {
        CrearBaseDelSistema(_dbPath, "Original");
        RespaldoSqlite.CrearCopia(_dbPath, Ruta("respaldo.db"));
        var hashAntes = Hash(Ruta("respaldo.db"));

        using (AgregarProductoSoloEnWal(_dbPath, "Cambio 1")) { }
        Assert.True(RespaldoSqlite.Restaurar(Ruta("respaldo.db"), _dbPath, Ruta("previa1.db")).Success);
        using (AgregarProductoSoloEnWal(_dbPath, "Cambio 2")) { }
        Assert.True(RespaldoSqlite.Restaurar(Ruta("respaldo.db"), _dbPath, Ruta("previa2.db")).Success);

        Assert.Equal(new[] { "Original" }, Productos(_dbPath));
        Assert.Equal(hashAntes, Hash(Ruta("respaldo.db")));
    }

    [Fact]
    public void Restaurar_GuardaAntesUnaCopiaDeLaBaseActual_QueTambienSePuedeRestaurar()
    {
        // Si se restaura el respaldo equivocado, se puede volver atrás con la copia previa
        CrearBaseDelSistema(_dbPath, "Viejo");
        RespaldoSqlite.CrearCopia(_dbPath, Ruta("respaldo.db"));
        using (AgregarProductoSoloEnWal(_dbPath, "Del día")) { }

        Assert.True(RespaldoSqlite.Restaurar(Ruta("respaldo.db"), _dbPath, Ruta("previa.db")).Success);
        Assert.Equal(new[] { "Del día", "Viejo" }, Productos(Ruta("previa.db")));

        Assert.True(RespaldoSqlite.Restaurar(Ruta("previa.db"), _dbPath, Ruta("previa2.db")).Success);
        Assert.Equal(new[] { "Del día", "Viejo" }, Productos(_dbPath));
    }

    [Fact]
    public void Restaurar_ArchivoQueNoEsUnaBase_FallaSinTocarLaBaseActual()
    {
        CrearBaseDelSistema(_dbPath, "Actual");
        File.WriteAllText(Ruta("foto.db"), "esto no es una base de datos");

        var result = RespaldoSqlite.Restaurar(Ruta("foto.db"), _dbPath, Ruta("previa.db"));

        Assert.False(result.Success);
        Assert.Equal(new[] { "Actual" }, Productos(_dbPath));
    }

    [Fact]
    public void Restaurar_BaseDeOtroPrograma_FallaSinTocarLaBaseActual()
    {
        CrearBaseDelSistema(_dbPath, "Actual");
        using (var otra = Abrir(Ruta("otra.db")))
            Ejecutar(otra, "CREATE TABLE Contactos (Id INTEGER);");

        var result = RespaldoSqlite.Restaurar(Ruta("otra.db"), _dbPath, Ruta("previa.db"));

        Assert.False(result.Success);
        Assert.Equal(new[] { "Actual" }, Productos(_dbPath));
    }

    // ──────────────────────────────────────────────────────────────────────
    // LimpiarRespaldosViejos
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void LimpiarRespaldosViejos_ConservaLosMasRecientes_Y_NoTocaCierreNiCopiasPrevias()
    {
        for (int dia = 1; dia <= 5; dia++)
            File.WriteAllText(Ruta($"Backup_Stock_2026090{dia}_1200.db"), "x");
        File.WriteAllText(Ruta("Backup_Stock_UltimoCierre.db"), "x");
        File.WriteAllText(Ruta("Backup_Stock_AntesDeRestaurar_20260101_120000.db"), "x");
        File.WriteAllText(Ruta("otro_archivo.db"), "x");

        RespaldoSqlite.LimpiarRespaldosViejos(_dir, conservar: 3);

        var quedan = Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[]
        {
            "Backup_Stock_20260903_1200.db",
            "Backup_Stock_20260904_1200.db",
            "Backup_Stock_20260905_1200.db",
            "Backup_Stock_AntesDeRestaurar_20260101_120000.db",
            "Backup_Stock_UltimoCierre.db",
            "otro_archivo.db",
        }, quedan);
    }
}
