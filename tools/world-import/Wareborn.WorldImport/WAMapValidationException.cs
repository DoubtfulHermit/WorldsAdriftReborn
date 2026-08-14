namespace Wareborn.WorldImport;

public sealed class WAMapValidationException : Exception
{
    public WAMapValidationException(string message) : base(message)
    {
    }
}
