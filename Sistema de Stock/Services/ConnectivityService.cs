#if WINDOWS || ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Networking;
#endif

namespace Sistema_de_Stock.Services;

/// <summary>
/// Wrapper sobre Connectivity de MAUI para detectar disponibilidad de internet.
/// </summary>
public class ConnectivityService
{
    /// <summary>
    /// Devuelve true si el dispositivo tiene acceso a internet en este momento.
    /// </summary>
    public bool IsOnline =>
#if WINDOWS || ANDROID || IOS || MACCATALYST
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
#else
        true; 
#endif
}
