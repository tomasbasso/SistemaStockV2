using Microsoft.Maui.Storage;

namespace Sistema_de_Stock.Services;

/// <summary>
/// Servicio singleton que mantiene la sesión del tenant (negocio) activo.
/// Persiste el TenantId en SecureStorage para soportar arranque offline.
/// </summary>
public class TenantService
{
    private const string KeyTenantId   = "ss_tenant_id";
    private const string KeyTenantName = "ss_tenant_name";
    private const string KeyEmail      = "ss_tenant_email";

    public Guid   CurrentTenantId { get; private set; } = Guid.Empty;
    public string TenantName      { get; private set; } = string.Empty;
    public string CurrentEmail    { get; private set; } = string.Empty;

    /// <summary>
    /// Devuelve true si hay un tenant autenticado en la sesión actual.
    /// </summary>
    public bool IsAuthenticated => CurrentTenantId != Guid.Empty;

    /// <summary>
    /// Intenta restaurar la sesión guardada desde SecureStorage.
    /// Llama a esto al inicio de la app para soportar arranque offline.
    /// </summary>
    public async Task<bool> TryRestoreSessionAsync()
    {
        try
        {
            var raw = await SecureStorage.Default.GetAsync(KeyTenantId);
            if (!Guid.TryParse(raw, out var id)) return false;

            CurrentTenantId = id;
            TenantName      = await SecureStorage.Default.GetAsync(KeyTenantName) ?? string.Empty;
            CurrentEmail    = await SecureStorage.Default.GetAsync(KeyEmail)      ?? string.Empty;
            return true;
        }
        catch
        {
            // SecureStorage puede fallar en algunos emuladores/configuraciones
            return false;
        }
    }

    /// <summary>
    /// Guarda la sesión del tenant después de un login exitoso.
    /// </summary>
    public async Task SetSessionAsync(Guid tenantId, string email, string tenantName)
    {
        CurrentTenantId = tenantId;
        TenantName      = tenantName;
        CurrentEmail    = email;

        await SecureStorage.Default.SetAsync(KeyTenantId,   tenantId.ToString());
        await SecureStorage.Default.SetAsync(KeyTenantName, tenantName);
        await SecureStorage.Default.SetAsync(KeyEmail,      email);
    }

    /// <summary>
    /// Limpia la sesión (logout). Después de esto IsAuthenticated = false.
    /// </summary>
    public Task ClearSessionAsync()
    {
        CurrentTenantId = Guid.Empty;
        TenantName      = string.Empty;
        CurrentEmail    = string.Empty;

        SecureStorage.Default.Remove(KeyTenantId);
        SecureStorage.Default.Remove(KeyTenantName);
        SecureStorage.Default.Remove(KeyEmail);

        return Task.CompletedTask;
    }
}
