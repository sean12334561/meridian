namespace Meridian.Api.Features.Imports.Parsing;

/// <summary>The file as a whole can't be read, so the import fails. Retrying won't help.</summary>
public class ImportFileException(string message) : Exception(message);
