using Microsoft.Data.Sqlite;
using SistemaDeStockV3.Models;
using System.Text.RegularExpressions;

namespace SistemaDeStockV3.Services
{
    /// <summary>
    /// Copia y restauración de la base SQLite, sin dependencias de MAUI para poder testearlas.
    /// Usa la API de backup de SQLite en vez de copiar el archivo a mano: la copia incluye lo
    /// que todavía está en el -wal y la restauración no puede quedar "pisada" por un -wal viejo.
    /// </summary>
    public static class RespaldoSqlite
    {
        // Solo rotan los respaldos con fecha (Backup_Stock_yyyyMMdd_HHmm.db). El de cierre y
        // las copias previas a una restauración no entran en la limpieza.
        private static readonly Regex RespaldoRotativo = new(@"^Backup_Stock_\d{8}_\d{4}\.db$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Copia consistente de <paramref name="dbPath"/> en <paramref name="destinoPath"/>.
        /// Se escribe a un temporal y se reemplaza al final: si falla o se corta a mitad de
        /// camino, el respaldo anterior con ese nombre queda intacto.
        /// </summary>
        public static void CrearCopia(string dbPath, string destinoPath)
        {
            var temporal = destinoPath + ".tmp";
            BorrarConArchivosAuxiliares(temporal);
            try
            {
                using (var origen = Abrir(dbPath, SqliteOpenMode.ReadWrite))
                using (var destino = Abrir(temporal, SqliteOpenMode.ReadWriteCreate))
                    origen.BackupDatabase(destino);

                File.Move(temporal, destinoPath, overwrite: true);
            }
            finally
            {
                BorrarConArchivosAuxiliares(temporal);
            }
        }

        /// <summary>
        /// Reemplaza el contenido de <paramref name="dbPath"/> por el del respaldo, guardando antes
        /// la base actual en <paramref name="copiaPreviaPath"/> para poder deshacerlo.
        /// El respaldo nunca se abre ni se modifica: se trabaja sobre una copia, así el mismo
        /// archivo se puede volver a restaurar cuantas veces haga falta.
        /// </summary>
        public static Result<string> Restaurar(string respaldoPath, string dbPath, string copiaPreviaPath)
        {
            var trabajo = dbPath + ".restaurando";
            File.Copy(respaldoPath, trabajo, overwrite: true);
            try
            {
                var error = Validar(trabajo);
                if (error != null) return Result<string>.Fail(error);

                if (File.Exists(dbPath))
                    CrearCopia(dbPath, copiaPreviaPath);

                using (var origen = Abrir(trabajo, SqliteOpenMode.ReadWrite))
                using (var destino = Abrir(dbPath, SqliteOpenMode.ReadWriteCreate))
                {
                    origen.BackupDatabase(destino);

                    // Volcar el -wal al archivo principal para que la base quede autocontenida
                    using var cmd = destino.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    cmd.ExecuteNonQuery();
                }
                return Result<string>.Ok();
            }
            finally
            {
                BorrarConArchivosAuxiliares(trabajo);
            }
        }

        /// <summary>
        /// True si la base no tiene datos cargados, como una instalación nueva todavía sin restaurar.
        /// Ante cualquier duda devuelve false: nunca se deja de respaldar una base con datos.
        /// </summary>
        public static bool EstaVacia(string dbPath)
        {
            try
            {
                using var conn = Abrir(dbPath, SqliteOpenMode.ReadWrite);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT EXISTS (SELECT 1 FROM Productos) OR EXISTS (SELECT 1 FROM Categorias)
                                        OR EXISTS (SELECT 1 FROM Clientes) OR EXISTS (SELECT 1 FROM Ventas);";
                return Convert.ToInt64(cmd.ExecuteScalar()) == 0;
            }
            catch (SqliteException)
            {
                return false;
            }
        }

        /// <summary>Borra los respaldos con fecha más viejos, conservando los <paramref name="conservar"/> más recientes.</summary>
        public static void LimpiarRespaldosViejos(string carpeta, int conservar)
        {
            var viejos = Directory.EnumerateFiles(carpeta, "Backup_Stock_*.db")
                .Where(f => RespaldoRotativo.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase) // la fecha va en el nombre
                .Skip(conservar)
                .ToList();

            foreach (var archivo in viejos)
            {
                try { File.Delete(archivo); } catch { /* en uso: se reintenta en el próximo respaldo */ }
            }
        }

        private static string? Validar(string path)
        {
            try
            {
                using var conn = Abrir(path, SqliteOpenMode.ReadWrite);
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "PRAGMA quick_check;";
                if (cmd.ExecuteScalar() as string != "ok")
                    return "El respaldo está dañado y no se puede restaurar.";

                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Productos', 'Ventas');";
                if (Convert.ToInt64(cmd.ExecuteScalar()) != 2)
                    return "El archivo no es un respaldo de este sistema.";

                return null;
            }
            catch (SqliteException)
            {
                return "El archivo no es una base de datos válida.";
            }
        }

        private static SqliteConnection Abrir(string path, SqliteOpenMode modo)
        {
            // Pooling=False: al cerrar se libera el archivo y se puede mover o borrar enseguida
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = modo, Pooling = false };
            var conn = new SqliteConnection(cs.ToString());
            conn.Open();
            return conn;
        }

        private static void BorrarConArchivosAuxiliares(string path)
        {
            foreach (var archivo in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
                if (File.Exists(archivo)) File.Delete(archivo);
        }
    }
}
