namespace SistemaDeStockV3.Services
{
    /// <summary>
    /// Tipos de notificación disponibles para la UI.
    /// </summary>
    public enum NotificationType
    {
        Success,
        Error,
        Warning,
        Info
    }

    /// <summary>
    /// Modelo de un mensaje de notificación (toast).
    /// </summary>
    public class Notification
    {
        public string Message { get; init; } = string.Empty;
        public NotificationType Type { get; init; } = NotificationType.Info;
        public string Id { get; init; } = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Servicio global para emitir notificaciones toast a la interfaz de usuario.
    /// Se registra como Singleton y los componentes se suscriben via el evento OnNotify.
    /// </summary>
    public class NotificationService
    {
        public event Action<Notification>? OnNotify;

        /// <summary>Muestra una notificación de éxito (verde).</summary>
        public void Success(string message) => Notify(message, NotificationType.Success);

        /// <summary>Muestra una notificación de error (rojo).</summary>
        public void Error(string message) => Notify(message, NotificationType.Error);

        /// <summary>Muestra una notificación de advertencia (amarillo).</summary>
        public void Warning(string message) => Notify(message, NotificationType.Warning);

        /// <summary>Muestra una notificación informativa (azul).</summary>
        public void Info(string message) => Notify(message, NotificationType.Info);

        private void Notify(string message, NotificationType type)
        {
            OnNotify?.Invoke(new Notification { Message = message, Type = type });
        }
    }
}
