namespace HushNode.PushNotifications.Exceptions;

/// <summary>
/// Exception thrown when an FCM device token is invalid, expired, or unregistered.
/// Used to signal that a token should be deactivated.
/// </summary>
public class InvalidTokenException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="InvalidTokenException"/>.
    /// </summary>
    public InvalidTokenException()
        : base("The FCM device token is invalid or has been unregistered.")
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InvalidTokenException"/> with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidTokenException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InvalidTokenException"/> with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public InvalidTokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
