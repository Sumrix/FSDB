using System;

namespace FSDB.Infrastructure.Exceptions;

public class FileNameGenerationException(string message, Exception? inner = null) : Exception(message, inner);