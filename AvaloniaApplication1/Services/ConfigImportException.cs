using System;

namespace Archipolygo.Services;

/// <summary>The file is not a config export this version can read. <see cref="Exception.Message"/> is meant for the user.</summary>
public sealed class ConfigImportException : Exception
{
    public ConfigImportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
