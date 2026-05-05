namespace Sistema_de_Stock;

/// <summary>
/// Configuración central del proyecto Supabase para el Sistema de Stock.
/// </summary>
public static class SupabaseConfig
{
    public const string Url = "https://mavyswhnqhtakaxtzyql.supabase.co";

    public const string AnonKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
        "eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1hdnlzd2hucWh0YWtheHR6eXFsIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Nzc0NTY3ODcsImV4cCI6MjA5MzAzMjc4N30." +
        "csFXVAEx2iCGsUrffhVERIEY3ycKbMAm0YH-96q5g88";

    // Contraseña de la base de datos PostgreSQL en Supabase
    private const string DbPassword = "StorageSistema26";

    // Transactional Pooler — usar en runtime de la app (soporta múltiples conexiones cortas)
    // Utilizamos el Host aws-1-us-east-1 proporcionado por Supabase para soporte IPv4.
    // Usamos el puerto 5432 (Session Pooler) en lugar de 6543 para evitar conflictos con Entity Framework Core / Npgsql.
    public static string PoolerConnectionString =>
        $"Host=aws-1-us-east-1.pooler.supabase.com;" +
        $"Port=5432;" +
        $"Database=postgres;" +
        $"Username=postgres.mavyswhnqhtakaxtzyql;" +
        $"Password={DbPassword};" +
        $"SSL Mode=Require;" +
        $"Trust Server Certificate=true";

    // Direct connection — usar para dotnet ef migrations y conexiones generales
    // Dado que tu proveedor no soporta IPv6, la conexión directa falla.
    // Usaremos el Pooler transaccional para todo.
    public static string DirectConnectionString => PoolerConnectionString;
}
