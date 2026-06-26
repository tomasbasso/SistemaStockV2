using System;

namespace SistemaDeStockV3.Models
{
    /// <summary>
    /// Clase genérica para encapsular resultados de operaciones, estandarizando el manejo de éxito/error.
    /// Reemplaza tuplas (bool, string) y excepciones inconsistentes.
    /// </summary>
    public class Result<T>
    {
        public bool Success { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public T? Data { get; private set; }

        private Result(bool success, string message, T? data)
        {
            Success = success;
            Message = message;
            Data = data;
        }

        public static Result<T> Ok(T data) => new Result<T>(true, string.Empty, data);
        public static Result<T> Ok(string message = "") => new Result<T>(true, message, default);
        public static Result<T> Fail(string message) => new Result<T>(false, message, default);
    }
}