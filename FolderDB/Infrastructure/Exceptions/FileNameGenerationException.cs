using System;

namespace FolderDB.Infrastructure.Exceptions;

public class FileNameGenerationException(string message, Exception? inner = null) : Exception(message, inner);