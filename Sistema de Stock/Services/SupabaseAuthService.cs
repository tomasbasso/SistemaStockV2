using Npgsql;

namespace Sistema_de_Stock.Services;

/// <summary>
/// Servicio de autenticación contra Supabase Auth.
/// El registro de nuevos tenants es manual (el admin los crea en el dashboard de Supabase).
/// </summary>
public class SupabaseAuthService
{
    private readonly Supabase.Client _client;

    public SupabaseAuthService()
    {
        var options = new Supabase.SupabaseOptions
        {
            AutoRefreshToken        = true,
            AutoConnectRealtime     = false,
        };
        _client = new Supabase.Client(SupabaseConfig.Url, SupabaseConfig.AnonKey, options);
    }

    /// <summary>
    /// Inicia sesión con email y contraseña en Supabase Auth.
    /// Devuelve (TenantId, NombreNegocio) o lanza excepción si las credenciales son incorrectas.
    /// </summary>
    public async Task<(Guid TenantId, string NombreNegocio)> LoginAsync(string email, string password)
    {
        await _client.InitializeAsync();

        Supabase.Gotrue.Session? session;
        try
        {
            session = await _client.Auth.SignIn(email, password);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Credenciales incorrectas. Verificá tu email y contraseña.", ex);
        }

        if (session?.User?.Id is null)
            throw new InvalidOperationException("No se pudo iniciar sesión. Intentá de nuevo.");

        var userId = Guid.Parse(session.User.Id);

        // Buscar el tenant asociado al usuario via conexión directa PostgreSQL
        var (tenantId, tenantName) = await GetTenantForUserAsync(userId);

        return (tenantId, tenantName);
    }

    /// <summary>
    /// Cierra la sesión en Supabase Auth.
    /// </summary>
    public async Task SignOutAsync()
    {
        try { await _client.Auth.SignOut(); }
        catch { /* ignorar errores de red en logout */ }
    }

    // ── Privado ───────────────────────────────────────────────────────────────

    private async Task<(Guid TenantId, string NombreNegocio)> GetTenantForUserAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(SupabaseConfig.PoolerConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.id, t.name
            FROM   public.tenants t
            JOIN   public.tenant_usuarios tu ON tu.tenant_id = t.id
            WHERE  tu.user_id = @uid
            LIMIT  1";
        cmd.Parameters.AddWithValue("uid", userId);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            throw new InvalidOperationException(
                "El usuario no tiene un negocio asignado. Contactá al administrador.");

        return (reader.GetGuid(0), reader.GetString(1));
    }
}
