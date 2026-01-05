namespace DXO.Services.NativeAgent;

/// <summary>
/// Exception thrown when reviewer recommendation fails
/// </summary>
public class RecommendationException : Exception
{
    public string ErrorCode { get; }

    public RecommendationException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
