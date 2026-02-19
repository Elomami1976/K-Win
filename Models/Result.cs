namespace KWin.Models
{
    /// <summary>
    /// Generic result wrapper for all operations, providing success/failure status,
    /// data payload, and error information for consistent error handling.
    /// </summary>
    /// <typeparam name="T">The type of data returned on success.</typeparam>
    public class Result<T>
    {
        /// <summary>Whether the operation completed successfully.</summary>
        public bool Success { get; set; }

        /// <summary>The data payload returned on success.</summary>
        public T? Data { get; set; }

        /// <summary>Human-readable error message on failure.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Unique identifier for tracking this operation in logs.</summary>
        public string OperationId { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>Creates a successful result with data.</summary>
        public static Result<T> Ok(T data) => new()
        {
            Success = true,
            Data = data
        };

        /// <summary>Creates a failed result with error message.</summary>
        public static Result<T> Fail(string error) => new()
        {
            Success = false,
            ErrorMessage = error
        };

        /// <summary>Creates a failed result from an exception.</summary>
        public static Result<T> Fail(Exception ex) => new()
        {
            Success = false,
            ErrorMessage = $"{ex.GetType().Name}: {ex.Message}"
        };
    }
}
